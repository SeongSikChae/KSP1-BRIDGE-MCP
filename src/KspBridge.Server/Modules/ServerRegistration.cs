using System.Diagnostics;
using System.Text.Json;

namespace KspBridge.Server.Modules
{
    /// <summary>
    /// 실행 중인 서버가 자기 모듈 폴더를 <c>%LOCALAPPDATA%\KspBridge\servers\&lt;pid&gt;.json</c>으로 알린다.
    /// <c>scripts/deploy.ps1</c>은 이 등록을 읽어, 빌드 설정에 경로를 적지 않아도 실행 중인 서버의 모듈 폴더로 배포한다.
    /// 종료할 때 지운다. 비정상 종료로 남은 파일은 pid와 프로세스 시작 시각이 맞지 않으므로 배포 스크립트가 무시하고 지운다.
    /// </summary>
    public sealed class ServerRegistration : IDisposable
    {
        private readonly string filePath;

        private ServerRegistration(string filePath) => this.filePath = filePath;

        public static string RegistryDir { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KspBridge", "servers");

        /// <summary>등록 파일을 쓴다. 실패해도 서버 실행에는 영향을 주지 않으므로 경고만 남기고 null을 돌려준다.</summary>
        public static ServerRegistration? Register(int port, string modulesDir, ILogger logger)
        {
            try
            {
                using Process self = Process.GetCurrentProcess();
                Directory.CreateDirectory(RegistryDir);
                string path = Path.Combine(RegistryDir, $"{self.Id}.json");
                string json = JsonSerializer.Serialize(new
                {
                    pid = self.Id,
                    startTime = self.StartTime.ToUniversalTime().ToString("o"),
                    port,
                    modulesDir,
                    exe = Environment.ProcessPath,
                }, new JsonSerializerOptions { WriteIndented = true });

                string temp = path + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, path, overwrite: true);
                logger.LogInformation("Registered modules dir {ModulesDir} at {Registration}", modulesDir, path);
                return new ServerRegistration(path);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Server registration failed; deploy.ps1 will not find this server");
                return null;
            }
        }

        public void Dispose()
        {
            try
            {
                File.Delete(filePath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
