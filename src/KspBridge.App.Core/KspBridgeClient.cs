using System.Net.Http.Json;
using System.Text.Json;

namespace KspBridge.App
{
    public sealed record KspBridgeClientOptions(TimeSpan DefaultTimeout);

    /// <summary>HTTP client for the KspBridge game mod. Returns the response data JSON, or an "error: ..." text.</summary>
    public sealed class KspBridgeClient(HttpClient http, KspBridgeClientOptions options)
    {
        internal const string NotRunningMessage =
            "error: KSP is not running or the KspBridge mod is not loaded (connection refused).";

        /// <param name="timeout">Per-request timeout; null uses the configured default. HttpClient.Timeout must be infinite.</param>
        public async Task<string> SendAsync(
            HttpMethod method, string path, object? body = null, CancellationToken ct = default, TimeSpan? timeout = null)
        {
            using HttpRequestMessage request = new HttpRequestMessage(method, path)
            {
                Content = body is null ? null : JsonContent.Create(body),
            };

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout ?? options.DefaultTimeout);

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, cts.Token);
            }
            catch (HttpRequestException)
            {
                return NotRunningMessage;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return "error: timeout: no response from the KspBridge mod.";
            }

            using (response)
            {
                string text = await response.Content.ReadAsStringAsync(ct);
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(text);
                    JsonElement root = doc.RootElement;
                    if (root.GetProperty("ok").GetBoolean())
                    {
                        return root.GetProperty("data").GetRawText();
                    }

                    JsonElement error = root.GetProperty("error");
                    return $"error: {error.GetProperty("code").GetString()}: {error.GetProperty("message").GetString()}";
                }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
                {
                    return $"error: invalid_response: HTTP {(int)response.StatusCode}: {text}";
                }
            }
        }
    }
}
