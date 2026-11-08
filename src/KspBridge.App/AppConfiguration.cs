using System.Configuration.Annotation;

namespace KspBridge.App
{
    internal sealed class AppConfiguration
    {
        [Property(PropertyType.STRING, required: true)]
        public string ContentRootPath { get; set; } = null!;

        /// <summary>KspBridge mod HTTP base URL, e.g. http://127.0.0.1:8085</summary>
        [Property(PropertyType.STRING, required: true)]
        public string BaseUrl { get; set; } = null!;

        [Property(PropertyType.INT, required: true)]
        public int TimeoutSeconds { get; set; }
    }
}
