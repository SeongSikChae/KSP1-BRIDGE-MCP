using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace KspBridge.App.Tests
{
    [TestClass]
    public sealed class ToolHostTests
    {
        private static readonly string BuiltToolsPath = Path.Combine(AppContext.BaseDirectory, "KspBridge.App.Tools.dll");

        private string _dir = null!;

        [TestInitialize]
        public void Init()
        {
            _dir = Path.Combine(Path.GetTempPath(), "KspBridge.ToolHostTests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
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
        }

        private string CopyTools()
        {
            string path = Path.Combine(_dir, "KspBridge.App.Tools.dll");
            File.Copy(BuiltToolsPath, path, overwrite: true);
            return path;
        }

        private static (ToolHost Host, McpServerPrimitiveCollection<McpServerTool> Collection) Create(string toolsPath, params McpServerTool[] shellTools)
        {
            ServiceCollection services = new();
            services.AddSingleton(new KspBridgeClient(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1/") }, new KspBridgeClientOptions(TimeSpan.FromSeconds(1))));
            McpServerPrimitiveCollection<McpServerTool> collection = new();
            foreach (McpServerTool tool in shellTools)
            {
                collection.Add(tool);
            }
            return (new ToolHost(toolsPath, services.BuildServiceProvider(), collection, NullLogger<ToolHost>.Instance), collection);
        }

        private static McpServerTool ShellTool(string name) => McpServerTool.Create(() => "ok", new McpServerToolCreateOptions { Name = name });

        private static string[] Names(McpServerPrimitiveCollection<McpServerTool> collection)
            => collection.Select(t => t.ProtocolTool.Name).Order().ToArray();

        [TestMethod]
        public void LoadRegistersToolsBesideShellTools()
        {
            (ToolHost host, var collection) = Create(CopyTools(), ShellTool("reload_app"));

            using JsonDocument result = JsonDocument.Parse(host.Reload());

            CollectionAssert.AreEqual(new[] { "get_status", "reload_app", "reload_mod" }, Names(collection));
            Assert.AreEqual(1, result.RootElement.GetProperty("load_count").GetInt32());
            Assert.AreEqual(JsonValueKind.Null, result.RootElement.GetProperty("version_before").ValueKind);
            CollectionAssert.AreEquivalent(new[] { "get_status", "reload_mod" },
                result.RootElement.GetProperty("tools").EnumerateArray().Select(e => e.GetString()).ToArray());
        }

        [TestMethod]
        public void SecondLoadReplacesToolsWithoutDuplicates()
        {
            (ToolHost host, var collection) = Create(CopyTools(), ShellTool("reload_app"));
            host.Reload();
            McpServerTool firstStatus = collection.Single(t => t.ProtocolTool.Name == "get_status");
            int changed = 0;
            collection.Changed += (_, _) => changed++;

            using JsonDocument result = JsonDocument.Parse(host.Reload());

            Assert.AreEqual(1, changed);
            CollectionAssert.AreEqual(new[] { "get_status", "reload_app", "reload_mod" }, Names(collection));
            Assert.AreNotSame(firstStatus, collection.Single(t => t.ProtocolTool.Name == "get_status"));
            Assert.AreEqual(2, result.RootElement.GetProperty("load_count").GetInt32());
            Assert.AreEqual(JsonValueKind.String, result.RootElement.GetProperty("version_before").ValueKind);
        }

        [TestMethod]
        public void MissingFileKeepsPreviousTools()
        {
            string path = CopyTools();
            (ToolHost host, var collection) = Create(path, ShellTool("reload_app"));
            host.Reload();
            File.Delete(path);

            string result = host.Reload();

            StringAssert.StartsWith(result, "error: reload_failed: FileNotFoundException");
            StringAssert.EndsWith(result, "(previous tools kept)");
            CollectionAssert.AreEqual(new[] { "get_status", "reload_app", "reload_mod" }, Names(collection));
        }

        [TestMethod]
        public void CorruptFileKeepsPreviousTools()
        {
            string path = CopyTools();
            (ToolHost host, var collection) = Create(path, ShellTool("reload_app"));
            host.Reload();
            File.WriteAllBytes(path, [1, 2, 3, 4]);

            string result = host.Reload();

            StringAssert.StartsWith(result, "error: reload_failed: BadImageFormatException");
            CollectionAssert.AreEqual(new[] { "get_status", "reload_app", "reload_mod" }, Names(collection));
        }

        [TestMethod]
        public void InitialFailureLeavesOnlyShellTools()
        {
            (ToolHost host, var collection) = Create(Path.Combine(_dir, "missing.dll"), ShellTool("reload_app"));

            StringAssert.StartsWith(host.Reload(), "error: reload_failed:");
            CollectionAssert.AreEqual(new[] { "reload_app" }, Names(collection));
        }

        [TestMethod]
        public void ToolNameReservedByShellFailsLoad()
        {
            (ToolHost host, var collection) = Create(CopyTools(), ShellTool("reload_app"), ShellTool("get_status"));

            StringAssert.Contains(host.Reload(), "tool name 'get_status' is reserved by the server");
            CollectionAssert.AreEqual(new[] { "get_status", "reload_app" }, Names(collection));
        }

        [TestMethod]
        public void LoadedFileIsNotLocked()
        {
            string path = CopyTools();
            (ToolHost host, _) = Create(path, ShellTool("reload_app"));
            host.Reload();

            File.Copy(BuiltToolsPath, path, overwrite: true);
            File.Delete(path);
        }
    }
}
