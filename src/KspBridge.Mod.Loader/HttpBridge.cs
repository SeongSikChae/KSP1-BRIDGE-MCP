using KspBridge.Shared;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace KspBridge.Mod.Loader
{
    /// <summary>
    /// One HTTP request waiting for the Unity main thread. Exactly one side wins it: the main thread takes it
    /// (<see cref="TryTake"/>) and runs it, or the HTTP thread gives up (<see cref="TryAbandon"/>) and answers mod_busy.
    /// An abandoned request never runs, so a client that was told "busy" never sees a late side effect.
    /// </summary>
    internal sealed class PendingRequest
    {
        private const int Waiting = 0;
        private const int Taken = 1;
        private const int Abandoned = 2;
        private int state = Waiting;

        public string Method;
        public string Path;
        public string Body;
        public string ProtocolHeader;

        // Written only by the main thread after TryTake, read by the HTTP thread after Done is set.
        public string Response;
        public int Status = 200;
        public readonly ManualResetEvent Done = new ManualResetEvent(false);

        public bool TryTake() => Interlocked.CompareExchange(ref state, Taken, Waiting) == Waiting;

        public bool TryAbandon() => Interlocked.CompareExchange(ref state, Abandoned, Waiting) == Waiting;
    }

    /// <summary>
    /// HttpListener on 127.0.0.1. Requests are queued to the main thread (KSP/Unity APIs are main-thread only);
    /// a request the main thread does not finish within <see cref="MainThreadWait"/> gets mod_busy.
    /// Lives in the loader so the port stays open across logic reloads.
    /// </summary>
    internal sealed class HttpBridge
    {
        private static readonly TimeSpan MainThreadWait = TimeSpan.FromSeconds(2);
        private readonly HttpListener listener = new HttpListener();
        private readonly ConcurrentQueue<PendingRequest> queue = new ConcurrentQueue<PendingRequest>();
        private readonly Action<string> logError;
        private Thread thread;

        public HttpBridge(int port, Action<string> logError)
        {
            Port = port;
            this.logError = logError;
            listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
        }

        public int Port { get; }

        public void Start()
        {
            listener.Start();
            thread = new Thread(Loop) { IsBackground = true, Name = "KspBridge.Http" };
            thread.Start();
        }

        public void Stop()
        {
            try { listener.Close(); } catch (Exception) { }
        }

        public bool TryDequeue(out PendingRequest request) => queue.TryDequeue(out request);

        private void Loop()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = listener.GetContext();
                }
                catch (Exception)
                {
                    return;   // listener closed
                }
                ThreadPool.QueueUserWorkItem(_ => Serve(context));
            }
        }

        private void Serve(HttpListenerContext context)
        {
            PendingRequest request = new PendingRequest();
            try
            {
                request.Method = context.Request.HttpMethod;
                request.Path = context.Request.Url.AbsolutePath;
                request.ProtocolHeader = context.Request.Headers[Protocol.VersionHeader];
                using (StreamReader reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                    request.Body = reader.ReadToEnd();

                queue.Enqueue(request);
                if (!request.Done.WaitOne(MainThreadWait))
                {
                    if (request.TryAbandon())
                    {
                        // Not started: it will be skipped, so "busy" is the truth.
                        Write(context, 503, LoaderJson.Error(ErrorCodes.ModBusy,
                            "KSP main thread did not take the request within " + MainThreadWait.TotalSeconds +
                            " s (scene loading?). It was not executed; retry later."));
                        return;
                    }
                    // The main thread already started it: report its real result.
                    request.Done.WaitOne();
                }
                Write(context, request.Status, request.Response);
            }
            catch (Exception ex)
            {
                logError("HTTP serve failed: " + ex);
                try { Write(context, 500, LoaderJson.Error(ErrorCodes.Internal, ex.Message)); } catch (Exception) { }
            }
        }

        private static void Write(HttpListenerContext context, int status, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json ?? LoaderJson.Error(ErrorCodes.Internal, "empty response"));
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers[Protocol.VersionHeader] = Protocol.Version.ToString();
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.Close();
        }
    }
}
