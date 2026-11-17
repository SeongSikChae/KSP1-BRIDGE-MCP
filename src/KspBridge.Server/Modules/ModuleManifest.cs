using System.Text.Json;
using System.Text.Json.Serialization;

namespace KspBridge.Server.Modules
{
    /// <summary>
    /// 배포 스크립트가 모듈 DLL을 다 복사한 뒤 마지막에 쓰는 <c>manifest.json</c>.
    /// 이 파일이 바뀌면 새 모듈 세대를 읽는다.
    /// </summary>
    public sealed class ModuleManifest
    {
        [JsonPropertyName("build")]
        public string? Build { get; set; }

        /// <summary>읽을 모듈 어셈블리 파일 이름(모듈 폴더 기준).</summary>
        [JsonPropertyName("modules")]
        public List<string> Modules { get; set; } = [];

        public static ModuleManifest Read(string path)
        {
            // 배포 스크립트가 쓰는 중일 수 있으므로 공유 읽기로 연다.
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<ModuleManifest>(stream)
                ?? throw new InvalidDataException($"Empty manifest: {path}");
        }
    }
}
