namespace KspBridge.Server.Contracts
{
    /// <summary>
    /// 서버 모듈 어셈블리 표식. 서버 호스트는 이 attribute가 있는 어셈블리에서
    /// <c>[McpServerToolType]</c> 클래스의 <c>[McpServerTool]</c> 메서드를 도구로 등록한다.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class KspBridgeModuleAttribute : Attribute
    {
        public KspBridgeModuleAttribute(string name) => Name = name;

        /// <summary>모듈 이름. Mod 쪽 짝 모듈의 이름과 같게 쓴다(예: core, wait).</summary>
        public string Name { get; }
    }
}
