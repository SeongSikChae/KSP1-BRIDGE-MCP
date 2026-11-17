using KspBridge.Server.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;

namespace KspBridge.Server.Tests;

[TestClass]
public sealed class ModuleHostTests
{
    private string root = null!;

    [TestInitialize]
    public void Init() => root = TestServers.NewTempDir();

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private ModuleHost NewHost()
    {
        AppConfiguration configuration = TestServers.Config(root, 1);
        ServiceProvider services = new ServiceCollection().AddSingleton(configuration).BuildServiceProvider();
        return new ModuleHost(services, configuration, NullLogger<ModuleHost>.Instance);
    }

    [TestMethod]
    public void Reload_LoadsModuleTools_AndHostToolStays()
    {
        ModuleHost host = NewHost();
        FixtureModule.Deploy(host.ModulesDir);

        Assert.IsTrue(host.Reload(), host.LastError);

        CollectionAssert.IsSubsetOf(new[] { "reload", "fixture_echo", "fixture_host" }, host.Tools.PrimitiveNames.ToArray());
        Assert.AreEqual(1, host.Generation);
        Assert.AreEqual("fixture", host.Modules.Single().Name);
    }

    [TestMethod]
    public void Reload_SwapsGeneration_RaisesOneChange_AndUnloadsPrevious()
    {
        ModuleHost host = NewHost();
        FixtureModule.Deploy(host.ModulesDir);
        Assert.IsTrue(host.Reload(), host.LastError);

        int changes = 0;
        host.Tools.Changed += (_, _) => changes++;
        Assert.IsTrue(host.Reload(), host.LastError);

        Assert.AreEqual(2, host.Generation);
        Assert.AreEqual(1, changes, "tool swap must raise a single list_changed");
        Assert.AreEqual(3, host.Tools.Count);
        Assert.IsTrue(WaitCollected(host.LastUnloadedContext!), "previous generation ALC was not unloaded");
    }

    [TestMethod]
    public void Reload_Failure_KeepsCurrentGeneration()
    {
        ModuleHost host = NewHost();
        FixtureModule.Deploy(host.ModulesDir);
        Assert.IsTrue(host.Reload(), host.LastError);

        FixtureModule.WriteManifest(host.ModulesDir, [FixtureModule.FileName, "Missing.Module.dll"]);

        Assert.IsFalse(host.Reload());
        Assert.AreEqual(1, host.Generation);
        Assert.IsNotNull(host.LastError);
        CollectionAssert.Contains(host.Tools.PrimitiveNames.ToArray(), "fixture_echo");
    }

    [TestMethod]
    public void Reload_WithoutManifest_ReportsError()
    {
        ModuleHost host = NewHost();

        Assert.IsFalse(host.Reload());
        Assert.AreEqual(0, host.Generation);
        StringAssert.Contains(host.LastError, "manifest");
        CollectionAssert.AreEqual(new[] { "reload" }, host.Tools.PrimitiveNames.ToArray());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool WaitCollected(WeakReference reference)
    {
        for (int i = 0; i < 20 && reference.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        return !reference.IsAlive;
    }
}
