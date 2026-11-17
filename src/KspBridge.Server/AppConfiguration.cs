using System.Configuration;
using System.Configuration.Annotation;

namespace KspBridge.Server
{
    public sealed class AppConfiguration : IValidatableConfiguration
    {
        [Property(PropertyType.STRING, required: true)]
        public string ContentRootPath { get; set; } = null!;

        /// <summary>MCP HTTP 포트. 127.0.0.1에만 바인딩한다.</summary>
        [Property(PropertyType.USHORT, required: true)]
        public ushort? Port { get; set; }

        /// <summary>KSP Mod HTTP 주소(예: http://127.0.0.1:8650).</summary>
        [Property(PropertyType.STRING, required: true)]
        public string ModUrl { get; set; } = null!;

        /// <summary>Mod 요청 시간 제한(ms).</summary>
        [Property(PropertyType.INT, required: true)]
        public int? RequestTimeoutMs { get; set; }

        /// <summary>서버 모듈 폴더(ContentRootPath 기준 상대 경로 허용). manifest.json을 감시한다.</summary>
        [Property(PropertyType.STRING, required: true)]
        public string ModulesDir { get; set; } = null!;

        public void Validate()
        {
            if (!Uri.TryCreate(ModUrl, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp))
                throw new ArgumentException($"ModUrl must be an absolute http URI: '{ModUrl}'.", nameof(ModUrl));
            if (!uri.IsLoopback)
                throw new ArgumentException($"ModUrl must point to a loopback address: '{ModUrl}'.", nameof(ModUrl));
            if (RequestTimeoutMs is null or < 100 or > 60000)
                throw new ArgumentOutOfRangeException(nameof(RequestTimeoutMs), RequestTimeoutMs, "RequestTimeoutMs must be 100..60000.");
        }

        public string ResolveModulesDir() => Path.GetFullPath(Path.Combine(ContentRootPath, ModulesDir));
    }
}
