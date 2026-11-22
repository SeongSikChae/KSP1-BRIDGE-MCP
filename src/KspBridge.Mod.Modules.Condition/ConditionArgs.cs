using KspBridge.Mod.Core;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Condition
{
    /// <summary>wait_until parameter checks. Bad input is bad_request, so it rejects the plan at submit time.</summary>
    internal static class ConditionArgs
    {
        private static readonly string[] Fields = { "value", "op", "to" };

        public static ConditionSet Parse(Dictionary<string, object> args)
        {
            List<object> all = args.Arr(ConditionSet.AllMode);
            List<object> any = args.Arr(ConditionSet.AnyMode);
            if ((all == null) == (any == null))
                throw BridgeException.BadRequest("give either 'all' or 'any' (a list of conditions)");
            string mode = all != null ? ConditionSet.AllMode : ConditionSet.AnyMode;
            List<object> items = all ?? any;
            if (items.Count == 0)
                throw BridgeException.BadRequest("'" + mode + "' must not be empty");
            List<Condition> conditions = new List<Condition>();
            for (int i = 0; i < items.Count; i++)
                conditions.Add(ParseOne(items[i], mode + "[" + i + "]"));
            return new ConditionSet(mode, conditions);
        }

        private static Condition ParseOne(object raw, string at)
        {
            if (!(raw is Dictionary<string, object> item))
                throw BridgeException.BadRequest(at + " must be an object {value, op, to}");
            string unknown = item.Keys.FirstOrDefault(k => !Fields.Contains(k));
            if (unknown != null)
                throw BridgeException.BadRequest(at + ": unknown field '" + unknown + "' (value, op, to)");

            string name = Text(item, "value", at);
            ConditionValue value = ConditionValues.Find(name)
                ?? throw BridgeException.BadRequest(at + ": unknown value '" + name + "' (one of "
                    + string.Join(", ", ConditionValues.All.Select(v => v.Name)) + ")");

            string op = Text(item, "op", at);
            string[] allowed = value.Kind == ConditionKind.Number
                ? new[] { Condition.AtLeast, Condition.AtMost }
                : new[] { Condition.Equal };
            if (!allowed.Contains(op))
            {
                throw BridgeException.BadRequest(at + ": '" + name + "' is compared with " + string.Join(" or ", allowed)
                    + " (got '" + op + "')");
            }

            item.TryGetValue("to", out object to);
            switch (value.Kind)
            {
                case ConditionKind.Number:
                    if (!(to is double))
                        throw BridgeException.BadRequest(at + ": 'to' must be a number");
                    break;
                case ConditionKind.Flag:
                    if (!(to is bool))
                        throw BridgeException.BadRequest(at + ": 'to' must be true or false");
                    break;
                default:
                    if (!(to is string text) || !ConditionValues.Situations.Contains(text))
                        throw BridgeException.BadRequest(at + ": 'to' must be one of " + string.Join(", ", ConditionValues.Situations));
                    break;
            }
            return new Condition(value, op, to);
        }

        private static string Text(Dictionary<string, object> item, string key, string at)
            => item.TryGetValue(key, out object raw) && raw is string text
                ? text
                : throw BridgeException.BadRequest(at + ": '" + key + "' must be a string");
    }
}
