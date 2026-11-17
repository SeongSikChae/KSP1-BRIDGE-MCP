using System.Text.Json;
using System.Text.Json.Nodes;

namespace KspBridge.Server.Contracts
{
    /// <summary>Mod 응답 <c>{"ok":true,"data":...}</c> 또는 <c>{"ok":false,"error":{"code","message"}}</c>.</summary>
    public sealed class BridgeResult
    {
        public bool Ok { get; init; }
        public JsonNode? Data { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }

        public static BridgeResult Success(JsonNode? data) => new() { Ok = true, Data = data };
        public static BridgeResult Failure(string code, string message) => new() { Ok = false, ErrorCode = code, ErrorMessage = message };

        /// <summary>도구 응답 JSON 출력 옵션. 들여쓰고, 한글 같은 비ASCII 문자를 이스케이프하지 않는다(도구 응답은 HTML이 아님).</summary>
        public static JsonSerializerOptions OutputOptions { get; } = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>도구 응답으로 돌려줄 JSON 문자열.</summary>
        public string ToJson() => ToNode().ToJsonString(OutputOptions);

        public JsonObject ToNode() => Ok
            ? new JsonObject { ["ok"] = true, ["data"] = Data?.DeepClone() }
            : new JsonObject { ["ok"] = false, ["error"] = new JsonObject { ["code"] = ErrorCode, ["message"] = ErrorMessage } };
    }
}
