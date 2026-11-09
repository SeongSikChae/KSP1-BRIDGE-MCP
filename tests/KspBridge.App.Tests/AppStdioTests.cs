using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace KspBridge.App.Tests
{
    /// <summary>Runs the built App executable over stdio, as an MCP client would.</summary>
    [TestClass]
    public sealed class AppStdioTests
    {
        private string _dir = null!;

        [TestInitialize]
        public void Init()
        {
            string appOutput = AppContext.BaseDirectory.Replace(
                Path.Combine("tests", "KspBridge.App.Tests"), Path.Combine("src", "KspBridge.App"), StringComparison.OrdinalIgnoreCase);
            Assert.IsTrue(File.Exists(Path.Combine(appOutput, "KspBridge.App.exe")), "App build output not found: " + appOutput);

            _dir = Path.Combine(Path.GetTempPath(), "KspBridge.AppStdioTests." + Guid.NewGuid().ToString("N"));
            CopyDirectory(appOutput, Path.Combine(_dir, "app"));
            Directory.CreateDirectory(Path.Combine(_dir, "logs"));
            // Port 1 is never served: tool calls must report that KSP is not running.
            File.WriteAllText(Path.Combine(_dir, "config.yaml"),
                $"ContentRootPath: {_dir}\nBaseUrl: http://127.0.0.1:1\nTimeoutSeconds: 5\n");
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                Directory.Delete(_dir, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            }
            foreach (string dir in Directory.GetDirectories(source))
            {
                CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
            }
        }

        // initialize-handshake revision: such clients receive session-wide list_changed broadcasts without subscribing.
        private Task<McpClient> StartAsync() => McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "KspBridge.App",
                Command = Path.Combine(_dir, "app", "KspBridge.App.exe"),
                Arguments = ["--config", Path.Combine(_dir, "config.yaml"), "--log", Path.Combine(_dir, "logs")],
            }),
            new McpClientOptions { ProtocolVersion = "2025-11-25" });

        private static async Task<string[]> ToolNamesAsync(McpClient client)
            => (await client.ListToolsAsync()).Select(t => t.Name).Order().ToArray();

        private static string Text(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

        [TestMethod]
        public async Task ListsShellAndLoadedTools()
        {
            await using McpClient client = await StartAsync();

            CollectionAssert.AreEqual(ExpectedTools.WithShell, await ToolNamesAsync(client));
            Assert.AreEqual(KspBridgeClient.NotRunningMessage, Text(await client.CallToolAsync("get_status")));
        }

        [TestMethod]
        public async Task ReloadAppSwapsToolsAndNotifiesListChanged()
        {
            await using McpClient client = await StartAsync();
            TaskCompletionSource listChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using IAsyncDisposable handler = client.RegisterNotificationHandler(
                NotificationMethods.ToolListChangedNotification, (_, _) => { listChanged.TrySetResult(); return ValueTask.CompletedTask; });

            using JsonDocument result = JsonDocument.Parse(Text(await client.CallToolAsync("reload_app")));

            Assert.AreEqual(2, result.RootElement.GetProperty("load_count").GetInt32());
            Assert.AreEqual(JsonValueKind.String, result.RootElement.GetProperty("version_before").ValueKind);
            await listChanged.Task.WaitAsync(TimeSpan.FromSeconds(10));
            CollectionAssert.AreEqual(ExpectedTools.WithShell, await ToolNamesAsync(client));
            Assert.AreEqual(KspBridgeClient.NotRunningMessage, Text(await client.CallToolAsync("get_status")));
        }

        [TestMethod]
        public async Task MissingToolsDllStartsWithReloadAppOnly()
        {
            Directory.Delete(Path.Combine(_dir, "app", "Tools"), recursive: true);
            await using McpClient client = await StartAsync();

            CollectionAssert.AreEqual(new[] { "reload_app" }, await ToolNamesAsync(client));
            StringAssert.StartsWith(Text(await client.CallToolAsync("reload_app")), "error: reload_failed:");
        }
    }
}
