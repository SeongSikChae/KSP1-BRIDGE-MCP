using KspBridge.Server.Contracts;
using ModelContextProtocol.Server;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using System.Text.Json;

// 이 테스트 어셈블리 자체를 모듈 폴더에 복사해 서버 모듈로 읽힌다(별도 픽스처 프로젝트 없이 세대 교체를 시험).
[assembly: KspBridgeModule("fixture")]

namespace KspBridge.Server.Tests;

[McpServerToolType]
public sealed class FixtureTools
{
    [McpServerTool(Name = "fixture_echo"), Description("Echoes text back.")]
    public static string Echo(string text) => "echo:" + text;

    [McpServerTool(Name = "fixture_host"), Description("Returns the server generation number.")]
    public static string Host(IServerHostInfo host) => JsonSerializer.Serialize(new { host.Generation });
}

internal static class FixtureModule
{
    public static string FileName => Path.GetFileName(typeof(FixtureTools).Assembly.Location);

    /// <summary>모듈 폴더에 이 어셈블리를 복사하고 매니페스트를 쓴다(서버 모듈 게시와 같은 순서: 모듈 먼저, 매니페스트 마지막).</summary>
    public static void Deploy(string modulesDir, params string[] extraEntries)
    {
        Directory.CreateDirectory(modulesDir);
        File.Copy(typeof(FixtureTools).Assembly.Location, Path.Combine(modulesDir, FileName), overwrite: true);
        WriteManifest(modulesDir, [FileName, .. extraEntries]);
    }

    public static void WriteManifest(string modulesDir, string[] modules)
        => File.WriteAllText(Path.Combine(modulesDir, KspBridge.Shared.Protocol.ManifestFileName),
            JsonSerializer.Serialize(new { build = DateTime.UtcNow.ToString("O"), modules }));
}
