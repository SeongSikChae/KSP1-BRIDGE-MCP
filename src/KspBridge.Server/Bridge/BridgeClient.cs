using KspBridge.Server.Contracts;
using KspBridge.Shared;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KspBridge.Server.Bridge
{
    /// <summary>
    /// KSP Mod HTTP 클라이언트. 연결 실패와 시간 초과를 오류 코드로 바꿔 돌려주고 멈추지 않는다.
    /// </summary>
    public sealed class BridgeClient : IBridgeClient, IDisposable
    {
        private readonly HttpClient http;

        public BridgeClient(AppConfiguration configuration)
            : this(new HttpClient(), new Uri(configuration.ModUrl), TimeSpan.FromMilliseconds(configuration.RequestTimeoutMs!.Value))
        {
        }

        internal BridgeClient(HttpClient http, Uri baseAddress, TimeSpan timeout)
        {
            this.http = http;
            http.BaseAddress = baseAddress;
            http.Timeout = timeout;
            http.DefaultRequestHeaders.Add(Protocol.VersionHeader, Protocol.Version.ToString());
        }

        public Task<BridgeResult> CallAsync(string module, string op, JsonNode? args = null, CancellationToken cancellationToken = default)
            => SendAsync(HttpMethod.Post, $"{Protocol.ApiPrefix}{Uri.EscapeDataString(module)}/{Uri.EscapeDataString(op)}", args ?? new JsonObject(), cancellationToken);

        public Task<BridgeResult> GetLoaderStatusAsync(CancellationToken cancellationToken = default)
            => SendAsync(HttpMethod.Get, Protocol.LoaderStatusPath, null, cancellationToken);

        public Task<BridgeResult> ReloadModAsync(CancellationToken cancellationToken = default)
            => SendAsync(HttpMethod.Post, Protocol.LoaderReloadPath, new JsonObject(), cancellationToken);

        private async Task<BridgeResult> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = new(method, path);
            if (body is not null)
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                return BridgeResult.Failure(ErrorCodes.ModUnreachable,
                    $"KSP mod is not reachable at {http.BaseAddress} ({ex.Message}). Is KSP running with KspBridge installed?");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return BridgeResult.Failure(ErrorCodes.ModUnreachable,
                    $"KSP mod at {http.BaseAddress} did not respond within {http.Timeout.TotalMilliseconds:0} ms.");
            }

            using (response)
            {
                // Loader status is readable across protocol versions; get_status uses it to report what mismatches.
                if (path != Protocol.LoaderStatusPath
                    && response.Headers.TryGetValues(Protocol.VersionHeader, out IEnumerable<string>? values)
                    && values.FirstOrDefault() is string modVersion && modVersion != Protocol.Version.ToString())
                {
                    return BridgeResult.Failure(ErrorCodes.ProtocolMismatch,
                        $"Protocol mismatch: server {Protocol.Version}, mod {modVersion}. Deploy matching builds.");
                }

                string text = await response.Content.ReadAsStringAsync(cancellationToken);
                try
                {
                    if (JsonNode.Parse(text) is JsonObject obj && obj["ok"] is JsonValue okValue && okValue.TryGetValue(out bool ok))
                    {
                        if (ok)
                            return BridgeResult.Success(obj["data"]?.DeepClone());
                        JsonNode? error = obj["error"];
                        return BridgeResult.Failure(error?["code"]?.GetValue<string>() ?? ErrorCodes.Internal,
                            error?["message"]?.GetValue<string>() ?? "Unknown mod error.");
                    }
                }
                catch (JsonException)
                {
                }
                return BridgeResult.Failure(ErrorCodes.Internal, $"Unexpected mod response (HTTP {(int)response.StatusCode}): {Truncate(text)}");
            }
        }

        private static string Truncate(string text) => text.Length <= 300 ? text : text[..300] + "...";

        public void Dispose() => http.Dispose();
    }
}
