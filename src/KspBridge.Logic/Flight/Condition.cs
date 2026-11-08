using System;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Flight
{
    /// <summary>
    /// A wait/trigger condition: a single {field, op, value} or a nestable {all:[...]} / {any:[...]} combination.
    /// </summary>
    public sealed class Condition
    {
        private const int MaxDepth = 4;
        private string _field;
        private Func<object, bool> _test;
        private List<Condition> _children;
        private bool _all;

        public bool IsSingle => _field != null;
        public string SingleField => _field;

        public static Condition Parse(Dictionary<string, object> node) => Parse(node, 0);

        private static Condition Parse(Dictionary<string, object> node, int depth)
        {
            if (depth > MaxDepth)
            {
                throw ApiException.BadRequest("condition nesting is limited to " + MaxDepth + " levels");
            }
            RequestBody b = new RequestBody(node);
            bool all = b.Has("all"), any = b.Has("any"), single = b.Has("field");
            if ((all ? 1 : 0) + (any ? 1 : 0) + (single ? 1 : 0) != 1)
            {
                throw ApiException.BadRequest("a condition has exactly one of field, all, any");
            }
            if (single)
            {
                string field = b.GetString("field");
                return new Condition { _field = field, _test = FlightFields.BuildTest(field, b.GetString("op"), b) };
            }
            string key = all ? "all" : "any";
            List<object> items = b.GetList(key);
            if (items.Count == 0)
            {
                throw ApiException.BadRequest(key + " must not be empty");
            }
            return new Condition
            {
                _all = all,
                _children = items.Select(o => Parse(o as Dictionary<string, object>
                    ?? throw ApiException.BadRequest("each condition must be an object"), depth + 1)).ToList(),
            };
        }

        /// <summary>Evaluates every node (no short-circuit) so <paramref name="values"/> holds all fields read.</summary>
        public bool Evaluate(Vessel v, Dictionary<string, object> values)
        {
            if (_field != null)
            {
                object value = FlightFields.Read(v, _field);
                values[_field] = value;
                return _test(value);
            }
            List<bool> r = _children.Select(c => c.Evaluate(v, values)).ToList();
            return _all ? r.All(x => x) : r.Any(x => x);
        }

        /// <summary>The reported value: the field's value for a single condition, else every field read.</summary>
        public object Report(Dictionary<string, object> values) =>
            IsSingle ? (values.TryGetValue(_field, out object x) ? x : null) : values;
    }
}
