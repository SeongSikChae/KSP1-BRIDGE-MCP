using System.Diagnostics;

namespace KspBridge.Server.Tests;

/// <summary>빌드된 서버를 프로세스로 실행해 종료 코드를 확인한다.</summary>
[TestClass]
public sealed class ExitCodeTests
{
    private static (int ExitCode, string StdErr) Run(params string[] args)
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "KspBridge.Server.dll");
        ProcessStartInfo info = new("dotnet") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        info.ArgumentList.Add(dll);
        foreach (string arg in args)
            info.ArgumentList.Add(arg);
        using Process process = Process.Start(info)!;
        string stdErr = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        Assert.IsTrue(process.WaitForExit(30000), "server did not exit");
        return (process.ExitCode, stdErr);
    }

    [TestMethod]
    public void NoArguments_Returns1()
        => Assert.AreEqual(1, Run().ExitCode);

    [TestMethod]
    public void Help_Returns0()
        => Assert.AreEqual(0, Run("--help").ExitCode);

    [TestMethod]
    public void InvalidConfig_Returns2()
    {
        string dir = TestServers.NewTempDir();
        string config = Path.Combine(dir, "bad.yaml");
        File.WriteAllText(config, "ContentRootPath: .\nPort: 1\nModUrl: http://example.com:8650\nRequestTimeoutMs: 3000\nModulesDir: modules\n");

        (int exitCode, string stdErr) = Run("--config", config, "--log", Path.Combine(dir, "log"));

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(stdErr, "loopback");
    }

    [TestMethod]
    public void MissingConfigFile_Returns2()
        => Assert.AreEqual(2, Run("--config", Path.Combine(TestServers.NewTempDir(), "none.yaml"), "--log", "log").ExitCode);
}
