using System;
using System.Collections.Generic;

namespace KspBridge.Flight
{
    /// <summary>Condition tree of a flight plan (until / abort). Leaves keep their own for_s timers.</summary>
    public abstract class Condition
    {
        /// <summary>Evaluates every leaf (timers update even when the result is already decided).</summary>
        public abstract bool Eval(Telemetry t, double ut);

        /// <summary>The condition as it was given, for plan_aborted.condition.</summary>
        public abstract object Describe();

        public static Condition Parse(object raw, string path)
        {
            if (raw is List<object> list)
            {
                return new All(ParseList(list, path));
            }
            Dictionary<string, object> o = raw as Dictionary<string, object> ?? throw Plan.Invalid(path, "condition must be an object or array");
            if (o.TryGetValue("all", out object all))
            {
                OnlyKey(o, "all", path);
                return new All(ParseList(all, path + ".all"));
            }
            if (o.TryGetValue("any", out object any))
            {
                OnlyKey(o, "any", path);
                return new Any(ParseList(any, path + ".any"));
            }
            return Leaf.Parse(o, path);
        }

        private static void OnlyKey(Dictionary<string, object> o, string key, string path)
        {
            if (o.Count != 1)
            {
                throw Plan.Invalid(path, "'" + key + "' must be the only field of a group");
            }
        }

        private static List<Condition> ParseList(object raw, string path)
        {
            List<object> list = raw as List<object> ?? throw Plan.Invalid(path, "must be an array");
            if (list.Count == 0)
            {
                throw Plan.Invalid(path, "must not be empty");
            }
            List<Condition> items = new List<Condition>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                items.Add(Parse(list[i], path + "[" + i + "]"));
            }
            return items;
        }

        private sealed class All : Condition
        {
            private readonly List<Condition> _items;

            public All(List<Condition> items) => _items = items;

            public override bool Eval(Telemetry t, double ut)
            {
                bool result = true;
                for (int i = 0; i < _items.Count; i++)
                {
                    result &= _items[i].Eval(t, ut);   // no short circuit
                }
                return result;
            }

            public override object Describe()
            {
                List<object> l = new List<object>(_items.Count);
                for (int i = 0; i < _items.Count; i++)
                {
                    l.Add(_items[i].Describe());
                }
                return new Dictionary<string, object> { ["all"] = l };
            }
        }

        private sealed class Any : Condition
        {
            private readonly List<Condition> _items;

            public Any(List<Condition> items) => _items = items;

            public override bool Eval(Telemetry t, double ut)
            {
                bool result = false;
                for (int i = 0; i < _items.Count; i++)
                {
                    result |= _items[i].Eval(t, ut);   // no short circuit
                }
                return result;
            }

            public override object Describe()
            {
                List<object> l = new List<object>(_items.Count);
                for (int i = 0; i < _items.Count; i++)
                {
                    l.Add(_items[i].Describe());
                }
                return new Dictionary<string, object> { ["any"] = l };
            }
        }
    }

    /// <summary>{var, op, value, for_s?}. A value that is not known (NaN) is false and resets the for_s timer.</summary>
    public sealed class Leaf : Condition
    {
        private static readonly string[] Ops = { "<", "<=", ">", ">=", "==", "!=" };

        private readonly string _var;
        private readonly string _op;
        private readonly double _num;
        private readonly string _str;
        private readonly bool _isString;
        private readonly double _forS;
        private double _trueSince = double.NaN;

        private Leaf(string var, string op, double num, string str, bool isString, double forS)
        {
            _var = var;
            _op = op;
            _num = num;
            _str = str;
            _isString = isString;
            _forS = forS;
        }

        public static Leaf Parse(Dictionary<string, object> o, string path)
        {
            foreach (string key in o.Keys)
            {
                if (key != "var" && key != "op" && key != "value" && key != "for_s")
                {
                    throw Plan.Invalid(path, "unknown field " + key);
                }
            }
            string var = o.TryGetValue("var", out object v) ? v as string : null;
            if (string.IsNullOrEmpty(var))
            {
                throw Plan.Invalid(path, "'var' is required");
            }
            bool isString = Array.IndexOf(Telemetry.StrVars, var) >= 0;
            if (!isString && Array.IndexOf(Telemetry.NumVars, var) < 0)
            {
                throw Plan.Invalid(path, "unknown variable " + var);
            }
            string op = o.TryGetValue("op", out object p) ? p as string : null;
            if (op == null || Array.IndexOf(Ops, op) < 0)
            {
                throw Plan.Invalid(path, "unknown operator " + (op ?? "(missing)") + " (" + string.Join(", ", Ops) + ")");
            }
            if (isString && op != "==" && op != "!=")
            {
                throw Plan.Invalid(path, "string variable " + var + " allows only == and !=");
            }
            o.TryGetValue("value", out object value);
            double num = double.NaN;
            string str = null;
            if (isString)
            {
                str = value as string ?? throw Plan.Invalid(path, "'value' must be a string for " + var);
            }
            else
            {
                if (!(value is double d) || double.IsNaN(d) || double.IsInfinity(d))
                {
                    throw Plan.Invalid(path, "'value' must be a number for " + var);
                }
                num = d;
            }
            double forS = 0;
            if (o.TryGetValue("for_s", out object f))
            {
                if (!(f is double fs) || fs < 0 || double.IsInfinity(fs))
                {
                    throw Plan.Invalid(path, "'for_s' must be a number >= 0");
                }
                forS = fs;
            }
            return new Leaf(var, op, num, str, isString, forS);
        }

        public override bool Eval(Telemetry t, double ut)
        {
            if (!Compare(t))
            {
                _trueSince = double.NaN;
                return false;
            }
            if (double.IsNaN(_trueSince))
            {
                _trueSince = ut;
            }
            return ut - _trueSince >= _forS;
        }

        private bool Compare(Telemetry t)
        {
            if (_isString)
            {
                bool eq = string.Equals(t.Str[_var], _str, StringComparison.Ordinal);
                return _op == "==" ? eq : !eq;
            }
            double x = t.Num[_var];
            if (double.IsNaN(x))
            {
                return false;
            }
            switch (_op)
            {
                case "<": return x < _num;
                case "<=": return x <= _num;
                case ">": return x > _num;
                case ">=": return x >= _num;
                case "==": return x == _num;
                default: return x != _num;
            }
        }

        public override object Describe()
        {
            Dictionary<string, object> d = new Dictionary<string, object>
            {
                ["var"] = _var,
                ["op"] = _op,
                ["value"] = _isString ? (object)_str : _num,
            };
            if (_forS > 0)
            {
                d["for_s"] = _forS;
            }
            return d;
        }
    }
}
