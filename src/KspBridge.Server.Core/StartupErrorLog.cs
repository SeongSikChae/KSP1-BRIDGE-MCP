namespace KspBridge.Server.Core
{
    /// <summary>
    /// 로거를 만들기 전이나 호스트를 만드는 중에 난 시작 단계 오류를 <c>--log</c> 폴더의
    /// <see cref="FileName"/>에 한 건씩 덧붙여 쓴다. 서비스로 실행하면 stderr를 볼 수 없으므로 원인을 여기서 본다.
    /// 본 로그 파일과 따로 두어, 호스트 생성 중 본 로거가 연 파일과 잠금이 부딪치지 않게 한다.
    /// </summary>
    public static class StartupErrorLog
    {
        public const string FileName = "KspBridge.Server.startup.log";

        /// <summary>기록한다. 폴더 권한 등으로 실패해도 예외를 내지 않는다(stderr는 호출한 쪽이 이미 썼다).</summary>
        public static void Write(string logDirPath, string kind, string message, Exception? exception = null)
        {
            try
            {
                string dir = new DirectoryInfo(logDirPath).FullName;
                Directory.CreateDirectory(dir);
                string line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{kind}] {message}{Environment.NewLine}";
                if (exception is not null)
                    line += exception + Environment.NewLine;
                File.AppendAllText(Path.Combine(dir, FileName), line);
            }
            catch (Exception)
            {
            }
        }
    }
}
