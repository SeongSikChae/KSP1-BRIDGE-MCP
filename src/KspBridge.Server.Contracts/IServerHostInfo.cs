namespace KspBridge.Server.Contracts
{
    /// <summary>서버 호스트와 현재 모듈 세대 정보. <c>get_status</c>가 쓴다.</summary>
    public interface IServerHostInfo
    {
        string HostVersion { get; }
        int ProtocolVersion { get; }
        int Generation { get; }
        DateTimeOffset? LoadedAt { get; }
        IReadOnlyList<ModuleVersion> Modules { get; }
        string? LastError { get; }
    }

    public sealed record ModuleVersion(string Name, string Version);
}
