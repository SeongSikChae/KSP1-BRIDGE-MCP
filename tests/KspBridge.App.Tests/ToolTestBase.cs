using System.Net;
using System.Text;
using System.Text.Json;

namespace KspBridge.App.Tests
{
    internal sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage>? respond = null) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string PathAndQuery, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery, body));
            return respond?.Invoke(request)
                ?? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"ok":true,"data":{}}""", Encoding.UTF8, "application/json") };
        }
    }

    internal static class ToolTest
    {
        public static (KspBridgeClient Client, RecordingHandler Handler) Client()
        {
            RecordingHandler handler = new();
            HttpClient http = new(handler) { BaseAddress = new Uri("http://127.0.0.1:8085/"), Timeout = Timeout.InfiniteTimeSpan };
            return (new KspBridgeClient(http, new KspBridgeClientOptions(TimeSpan.FromSeconds(10))), handler);
        }

        public static void AssertJson(string expected, string? actual)
        {
            Assert.IsNotNull(actual);
            Assert.AreEqual(
                JsonSerializer.Serialize(JsonDocument.Parse(expected).RootElement),
                JsonSerializer.Serialize(JsonDocument.Parse(actual).RootElement));
        }
    }
}
