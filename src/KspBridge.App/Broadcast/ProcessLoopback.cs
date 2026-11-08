using System.Runtime.InteropServices;

namespace KspBridge.App.Broadcast
{
    /// <summary>Source of captured PCM (48 kHz, 16-bit, stereo). Abstracted so the recorder can be tested without audio devices.</summary>
    public interface IAudioCaptureSource : IDisposable
    {
        void Start();

        /// <summary>Hands every packet available now to <paramref name="sink"/> (silent packets as zeros).</summary>
        void Read(Action<ReadOnlySpan<byte>> sink);
    }

    /// <summary>
    /// Captures the sound of one process (and its child processes) with the Windows process loopback API
    /// (Windows 10 2004+): ActivateAudioInterfaceAsync("VAD\Process_Loopback", PROCESS_LOOPBACK params).
    /// Other applications' sound is not included.
    /// </summary>
    public static class ProcessLoopback
    {
        public const int SampleRate = 48000;
        public const int Channels = 2;
        public const int BlockAlign = 4;   // 16-bit stereo

        private const string VirtualDevice = @"VAD\Process_Loopback";
        private const int ActivationTypeProcessLoopback = 1;
        private const int IncludeTargetProcessTree = 0;
        private const ushort VtBlob = 0x41;
        private const uint StreamFlagsLoopback = 0x00020000;
        private const uint StreamFlagsAutoConvertPcm = 0x80000000;
        private const uint StreamFlagsSrcDefaultQuality = 0x08000000;
        private const uint BufferFlagsSilent = 0x2;
        private static readonly Guid IidAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
        private static readonly Guid IidAudioCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

        /// <summary>Opens a capture of process <paramref name="pid"/>; error is a short reason when it fails (never throws).</summary>
        public static (IAudioCaptureSource? Source, string? Error) Open(int pid, TimeSpan timeout)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                return (null, "unsupported_os: process loopback needs Windows 10 2004 or later");
            }
            IntPtr activation = IntPtr.Zero, propVariant = IntPtr.Zero;
            try
            {
                activation = Marshal.AllocHGlobal(Marshal.SizeOf<ActivationParams>());
                Marshal.StructureToPtr(new ActivationParams
                {
                    ActivationType = ActivationTypeProcessLoopback,
                    TargetProcessId = pid,
                    ProcessLoopbackMode = IncludeTargetProcessTree,
                }, activation, false);
                // PROPVARIANT { VARTYPE vt; WORD reserved x3; BLOB { ULONG cbSize; BYTE* pBlobData } } = 24 bytes on x64.
                propVariant = Marshal.AllocHGlobal(24);
                Marshal.Copy(new byte[24], 0, propVariant, 24);
                Marshal.WriteInt16(propVariant, 0, unchecked((short)VtBlob));
                Marshal.WriteInt32(propVariant, 8, Marshal.SizeOf<ActivationParams>());
                Marshal.WriteIntPtr(propVariant, 16, activation);

                Completion completion = new();
                Guid iid = IidAudioClient;
                int hr = ActivateAudioInterfaceAsync(VirtualDevice, ref iid, propVariant, completion, out IActivateAudioInterfaceAsyncOperation _);
                if (hr < 0)
                {
                    return (null, $"activate_failed: 0x{hr:X8}");
                }
                if (!completion.Done.Task.Wait(timeout))
                {
                    return (null, "activate_timeout");
                }
                if (completion.Done.Task.Result is string activateError)
                {
                    return (null, activateError);
                }
                IAudioClient client = (IAudioClient)completion.Done.Task.Result;

                WaveFormatEx format = new()
                {
                    FormatTag = 1,
                    Channels = Channels,
                    SamplesPerSec = SampleRate,
                    AvgBytesPerSec = SampleRate * BlockAlign,
                    BlockAlign = BlockAlign,
                    BitsPerSample = 16,
                    Size = 0,
                };
                hr = client.Initialize(0, StreamFlagsLoopback | StreamFlagsAutoConvertPcm | StreamFlagsSrcDefaultQuality,
                    2_000_000 /* 200 ms in 100-ns units */, 0, ref format, IntPtr.Zero);
                if (hr < 0)
                {
                    Marshal.ReleaseComObject(client);
                    return (null, $"initialize_failed: 0x{hr:X8}");
                }
                Guid captureIid = IidAudioCaptureClient;
                hr = client.GetService(ref captureIid, out object captureObject);
                if (hr < 0)
                {
                    Marshal.ReleaseComObject(client);
                    return (null, $"capture_client_failed: 0x{hr:X8}");
                }
                return (new Capture(client, (IAudioCaptureClient)captureObject), null);
            }
            catch (Exception ex)
            {
                return (null, "capture_error: " + ex.Message);
            }
            finally
            {
                if (propVariant != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(propVariant);
                }
                if (activation != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(activation);
                }
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private sealed class Capture(IAudioClient client, IAudioCaptureClient capture) : IAudioCaptureSource
        {
            private byte[] _buffer = [];
            private bool _disposed;

            public void Start()
            {
                int hr = client.Start();
                if (hr < 0)
                {
                    throw new COMException("audio client start failed", hr);
                }
            }

            public void Read(Action<ReadOnlySpan<byte>> sink)
            {
                while (capture.GetNextPacketSize(out uint packet) >= 0 && packet > 0)
                {
                    if (capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _) < 0)
                    {
                        return;
                    }
                    int bytes = checked((int)frames * BlockAlign);
                    if (_buffer.Length < bytes)
                    {
                        _buffer = new byte[bytes];
                    }
                    if ((flags & BufferFlagsSilent) != 0 || data == IntPtr.Zero)
                    {
                        Array.Clear(_buffer, 0, bytes);
                    }
                    else
                    {
                        Marshal.Copy(data, _buffer, 0, bytes);
                    }
                    sink(_buffer.AsSpan(0, bytes));
                    capture.ReleaseBuffer(frames);
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                client.Stop();
                Marshal.ReleaseComObject(capture);
                Marshal.ReleaseComObject(client);
            }
        }

        // ---- interop ----

        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
        private static extern int ActivateAudioInterfaceAsync(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
            ref Guid riid,
            IntPtr activationParams,
            IActivateAudioInterfaceCompletionHandler completionHandler,
            out IActivateAudioInterfaceAsyncOperation activationOperation);

        [StructLayout(LayoutKind.Sequential)]
        private struct ActivationParams
        {
            public int ActivationType;
            public int TargetProcessId;
            public int ProcessLoopbackMode;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct WaveFormatEx
        {
            public ushort FormatTag;
            public ushort Channels;
            public uint SamplesPerSec;
            public uint AvgBytesPerSec;
            public ushort BlockAlign;
            public ushort BitsPerSample;
            public ushort Size;
        }

        /// <summary>Completion callback; agile because Windows calls it from its own thread.</summary>
        [ComVisible(true)]
        [ClassInterface(ClassInterfaceType.None)]
        private sealed class Completion : IActivateAudioInterfaceCompletionHandler, IAgileObject
        {
            /// <summary>IAudioClient on success, an error string on failure.</summary>
            public readonly TaskCompletionSource<object> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
            {
                try
                {
                    int hr = operation.GetActivateResult(out int activateHr, out object? unknown);
                    if (hr < 0 || activateHr < 0 || unknown is null)
                    {
                        Done.TrySetResult($"activate_failed: 0x{(hr < 0 ? hr : activateHr):X8}");
                    }
                    else
                    {
                        Done.TrySetResult(unknown);
                    }
                }
                catch (Exception ex)
                {
                    Done.TrySetResult("activate_failed: " + ex.Message);
                }
                return 0;
            }
        }

        [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceCompletionHandler
        {
            [PreserveSig]
            int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
        }

        [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceAsyncOperation
        {
            [PreserveSig]
            int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object? activatedInterface);
        }

        [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAgileObject
        {
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, ref WaveFormatEx format, IntPtr audioSessionGuid);
            [PreserveSig] int GetBufferSize(out uint bufferFrames);
            [PreserveSig] int GetStreamLatency(out long latency);
            [PreserveSig] int GetCurrentPadding(out uint paddingFrames);
            [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, IntPtr closestMatch);
            [PreserveSig] int GetMixFormat(out IntPtr format);
            [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr eventHandle);
            [PreserveSig] int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }

        [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
            [PreserveSig] int ReleaseBuffer(uint frames);
            [PreserveSig] int GetNextPacketSize(out uint frames);
        }
    }
}
