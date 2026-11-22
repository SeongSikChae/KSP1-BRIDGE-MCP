using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Condition
{
    /// <summary>One comparison of wait_until.</summary>
    internal sealed class Condition
    {
        public const string AtLeast = ">=";
        public const string AtMost = "<=";
        public const string Equal = "==";

        public Condition(ConditionValue value, string op, object to)
        {
            Value = value;
            Op = op;
            To = to;
        }

        public ConditionValue Value { get; }
        public string Op { get; }

        /// <summary>double, string or bool, as the value's kind needs.</summary>
        public object To { get; }

        /// <summary>A missing value (null) and NaN never match.</summary>
        public bool Met(object actual)
        {
            switch (Op)
            {
                case AtLeast:
                    return actual is double atLeast && atLeast >= (double)To;
                case AtMost:
                    return actual is double atMost && atMost <= (double)To;
                default:
                    return actual != null && actual.Equals(To);
            }
        }
    }

    /// <summary>The judged conditions of one frame.</summary>
    internal sealed class ConditionOutcome
    {
        public bool Met;

        /// <summary>mode and, per condition, value, op, to, actual and met: step progress and the condition_met event.</summary>
        public Dictionary<string, object> Summary;
    }

    /// <summary>The conditions of one wait_until: all of them, or any one of them.</summary>
    internal sealed class ConditionSet
    {
        public const string AllMode = "all";
        public const string AnyMode = "any";

        public ConditionSet(string mode, IReadOnlyList<Condition> conditions)
        {
            Mode = mode;
            Conditions = conditions;
            UsesFlight = conditions.Any(c => c.Value.Path.Count > 0);
            UsesNoThrust = conditions.Any(c => c.Value.Name == ConditionValues.NoThrust);
        }

        public string Mode { get; }
        public IReadOnlyList<Condition> Conditions { get; }

        /// <summary>A condition needs <see cref="ConditionReading.Flight"/>.</summary>
        public bool UsesFlight { get; }

        /// <summary>A condition needs <see cref="ConditionReading.NoThrust"/>.</summary>
        public bool UsesNoThrust { get; }

        public ConditionOutcome Judge(ConditionReading reading)
        {
            int met = 0;
            List<object> rows = new List<object>();
            foreach (Condition condition in Conditions)
            {
                object actual = reading.Get(condition.Value);
                bool ok = condition.Met(actual);
                if (ok)
                    met++;
                rows.Add(new Dictionary<string, object>
                {
                    ["value"] = condition.Value.Name,
                    ["op"] = condition.Op,
                    ["to"] = condition.To,
                    ["actual"] = actual,
                    ["met"] = ok,
                });
            }
            return new ConditionOutcome
            {
                Met = Mode == AllMode ? met == Conditions.Count : met > 0,
                Summary = new Dictionary<string, object> { ["mode"] = Mode, ["conditions"] = rows },
            };
        }
    }
}
