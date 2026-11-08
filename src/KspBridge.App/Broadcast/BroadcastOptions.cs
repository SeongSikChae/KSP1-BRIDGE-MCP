using System.Configuration.Annotation;

namespace KspBridge.App.Broadcast
{
    /// <summary>Broadcast recording section (start_broadcast). Optional: without it the broadcast tools return not_configured.</summary>
    internal sealed class BroadcastOptions
    {
        /// <summary>ffmpeg.exe with the gfxcapture filter and -/filter_complex (checked with 9.0.1).</summary>
        [Property(PropertyType.STRING, required: true)]
        public string FfmpegPath { get; set; } = null!;

        /// <summary>Output folder, relative to ContentRootPath or absolute.</summary>
        [Property(PropertyType.STRING)]
        public string OutputDir { get; set; } = "broadcasts";

        /// <summary>KSP window title (whole-title match) captured with gfxcapture.</summary>
        [Property(PropertyType.STRING)]
        public string WindowTitle { get; set; } = "Kerbal Space Program";

        [Property(PropertyType.INT)]
        public int FrameRate { get; set; } = 30;

        /// <summary>Subtitle font (must support Hangul).</summary>
        [Property(PropertyType.STRING)]
        public string Font { get; set; } = "Malgun Gothic";

        /// <summary>Record the KSP process sound (process loopback, Windows 10 2004+). Default true.</summary>
        [Property(PropertyType.BOOL)]
        public bool GameAudio { get; set; } = true;

        /// <summary>Game sound volume multiplier in the final mix, 0 or more (default 1; 2 = +6 dB, 4 = +12 dB).</summary>
        [Property(PropertyType.DOUBLE)]
        public double GameVolume { get; set; } = 1.0;

        /// <summary>Ducking: narration level that starts lowering the game sound, 0.001..1 (default 0.02).</summary>
        [Property(PropertyType.DOUBLE)]
        public double DuckThreshold { get; set; } = 0.02;

        /// <summary>Ducking strength, 1..20 (default 8, about -20 dB; 1 = off).</summary>
        [Property(PropertyType.DOUBLE)]
        public double DuckRatio { get; set; } = 8;

        /// <summary>Ducking attack in ms, 0.01..2000 (default 20).</summary>
        [Property(PropertyType.DOUBLE)]
        public double DuckAttack { get; set; } = 20;

        /// <summary>Ducking release in ms (time to come back after narration), 0.01..9000 (default 400).</summary>
        [Property(PropertyType.DOUBLE)]
        public double DuckRelease { get; set; } = 400;

        /// <summary>Compress loud game sounds (explosions) before the volume is applied. Default true.</summary>
        [Property(PropertyType.BOOL)]
        public bool GameCompress { get; set; } = true;

        /// <summary>Compressor threshold in dBFS, -60..0 (default -18).</summary>
        [Property(PropertyType.DOUBLE)]
        public double CompThreshold { get; set; } = -18;

        /// <summary>Compressor ratio, 1..20 (default 4).</summary>
        [Property(PropertyType.DOUBLE)]
        public double CompRatio { get; set; } = 4;

        /// <summary>Compressor attack in ms, 0.01..2000 (default 5).</summary>
        [Property(PropertyType.DOUBLE)]
        public double CompAttack { get; set; } = 5;

        /// <summary>Compressor release in ms, 0.01..9000 (default 200).</summary>
        [Property(PropertyType.DOUBLE)]
        public double CompRelease { get; set; } = 200;

        /// <summary>Process name whose sound is recorded (without .exe).</summary>
        [Property(PropertyType.STRING)]
        public string GameProcess { get; set; } = "KSP_x64";

        public SpeechOptions? Speech { get; set; }
    }

    /// <summary>Azure Speech (Foundry MAI-Voice) text to speech.</summary>
    internal sealed class SpeechOptions
    {
        /// <summary>Azure region of the Speech resource with MAI voices, e.g. southeastasia (koreacentral has none).</summary>
        [Property(PropertyType.STRING, required: true)]
        public string Region { get; set; } = null!;

        /// <summary>Speech resource key. Plain text is encrypted to DEKENC: with OsKms at startup.</summary>
        [Property(PropertyType.STRING, required: true)]
        public string ApiKey { get; set; } = null!;

        [Property(PropertyType.STRING)]
        public string Voice { get; set; } = "ko-KR-SunHiNeural";

        /// <summary>mstts:express-as style of the voice (Harper: narrator, educational, ...). Empty = none.</summary>
        [Property(PropertyType.STRING)]
        public string? Style { get; set; }

        /// <summary>SSML prosody rate, e.g. +30%. Empty = normal speed.</summary>
        [Property(PropertyType.STRING)]
        public string? Rate { get; set; } = "+10%";
    }

    /// <summary>KEK from the OS credential store (skill dotnet-kms-config-secrets, backend B).</summary>
    internal sealed class OsKmsOptions
    {
        /// <summary>Credential store service name; must be a URI (Git Credential Manager target), e.g. kspbridge-app://kek.</summary>
        [Property(PropertyType.STRING)]
        public string CredentialService { get; set; } = "kspbridge-app://kek";

        [Property(PropertyType.STRING)]
        public string CredentialAccount { get; set; } = "kek";

        /// <summary>Base64 PBKDF2 salt; generated and written on first start.</summary>
        public string? Salt { get; set; }

        [Property(PropertyType.INT)]
        public int Iterations { get; set; } = 600000;

        /// <summary>Wrapped DEK file, relative to ContentRootPath.</summary>
        [Property(PropertyType.STRING)]
        public string SecretsDekFilePath { get; set; } = "secrets/dek.bin";
    }
}
