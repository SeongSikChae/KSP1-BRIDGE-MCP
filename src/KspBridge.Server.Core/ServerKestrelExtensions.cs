using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Net;

namespace KspBridge.Server.Core
{
    public static class ServerKestrelExtensions
    {
        /// <summary>
        /// 설정 포트로 Kestrel을 루프백 주소(127.0.0.1)에만 HTTP/1.1로 바인딩한다.
        /// 인증 없이 기체를 조종하는 엔드포인트이므로 외부 인터페이스에 열지 않는다. TLS는 쓰지 않는다.
        /// </summary>
        public static IWebHostBuilder ConfigureServerKestrel(this IWebHostBuilder webHost, ushort port)
            => webHost.ConfigureKestrel(options => ConfigureLoopback(options, port));

        internal static void ConfigureLoopback(KestrelServerOptions options, ushort port)
            => options.Listen(IPAddress.Loopback, port, listen => listen.Protocols = HttpProtocols.Http1);
    }
}
