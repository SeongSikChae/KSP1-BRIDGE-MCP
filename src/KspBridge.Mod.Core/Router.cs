using KspBridge.Shared;
using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Core
{
    /// <summary>Maps "module/op" to handlers and wraps results as {"ok":...}.</summary>
    public sealed class Router
    {
        private readonly Dictionary<string, RequestHandler> handlers = new Dictionary<string, RequestHandler>(StringComparer.Ordinal);
        private readonly Action<string> logError;

        public Router(Action<string> logError) => this.logError = logError;

        public IEnumerable<string> Routes => handlers.Keys;

        public void Add(string route, RequestHandler handler)
        {
            if (handlers.ContainsKey(route))
                throw new InvalidOperationException("Duplicate route " + route);
            handlers[route] = handler;
        }

        public string Handle(string route, string body)
        {
            if (!handlers.TryGetValue(route, out RequestHandler handler))
                return Error(ErrorCodes.UnknownRoute, "Unknown route /api/" + route);
            try
            {
                Dictionary<string, object> args;
                try
                {
                    args = Json.ParseObject(body);
                }
                catch (FormatException ex)
                {
                    return Error(ErrorCodes.BadRequest, "Invalid JSON body: " + ex.Message);
                }
                object data = handler(args);
                return Json.Write(new Dictionary<string, object> { ["ok"] = true, ["data"] = data });
            }
            catch (BridgeException ex)
            {
                return Error(ex.Code, ex.Message);
            }
            catch (Exception ex)
            {
                logError?.Invoke("handler " + route + " failed: " + ex);
                return Error(ErrorCodes.Internal, ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static string Error(string code, string message)
            => Json.Write(new Dictionary<string, object>
            {
                ["ok"] = false,
                ["error"] = new Dictionary<string, object> { ["code"] = code, ["message"] = message },
            });
    }

    /// <summary>Step types registered by modules.</summary>
    public sealed class StepRegistry
    {
        private readonly Dictionary<string, IStepType> types = new Dictionary<string, IStepType>(StringComparer.Ordinal);

        public IEnumerable<IStepType> Types => types.Values;

        public void Add(IStepType type)
        {
            if (types.ContainsKey(type.Name))
                throw new InvalidOperationException("Duplicate step type " + type.Name);
            types[type.Name] = type;
        }

        public IStepType Find(string name) => name != null && types.TryGetValue(name, out IStepType type) ? type : null;
    }
}
