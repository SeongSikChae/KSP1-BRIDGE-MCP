using System.Text.Json.Nodes;

namespace KspBridge.Server.Contracts
{
    /// <summary>
    /// KSP Mod HTTP 엔드포인트를 부르는 클라이언트. 서버 호스트가 제공하고, 모듈 도구는 DI로 받는다.
    /// 실패는 예외가 아니라 <see cref="BridgeResult"/>의 오류 코드로 돌려준다.
    /// </summary>
    public interface IBridgeClient
    {
        /// <summary>POST /api/{module}/{op}</summary>
        Task<BridgeResult> CallAsync(string module, string op, JsonNode? args = null, CancellationToken cancellationToken = default);

        /// <summary>GET /loader/status</summary>
        Task<BridgeResult> GetLoaderStatusAsync(CancellationToken cancellationToken = default);

        /// <summary>POST /loader/reload</summary>
        Task<BridgeResult> ReloadModAsync(CancellationToken cancellationToken = default);
    }
}
