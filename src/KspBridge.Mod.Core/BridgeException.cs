using KspBridge.Shared;
using System;

namespace KspBridge.Mod.Core
{
    /// <summary>An expected request failure that becomes {"ok":false,"error":{code,message}}.</summary>
    public sealed class BridgeException : Exception
    {
        public BridgeException(string code, string message) : base(message) => Code = code;

        public string Code { get; }

        public static BridgeException BadRequest(string message) => new BridgeException(ErrorCodes.BadRequest, message);
        public static BridgeException NotReady(string message) => new BridgeException(ErrorCodes.NotReady, message);
    }
}
