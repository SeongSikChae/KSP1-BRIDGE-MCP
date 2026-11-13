using System;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge
{
    /// <summary>get_status.input_locks: the game's current input locks (InputLockManager.lockStack), read only.</summary>
    public static class InputLocks
    {
        private static KeyValuePair<ulong, string>[] _bits;   // built on first use: no static initializer that could poison the type

        /// <summary>Single-bit ControlTypes members (group members such as ALL_SHIP_CONTROLS overlap and would hide which bits
        /// are set); aliases of one value are joined "A|B".</summary>
        private static KeyValuePair<ulong, string>[] Bits()
        {
            return _bits ?? (_bits = Enum.GetNames(typeof(ControlTypes))
                .Select(n => new KeyValuePair<ulong, string>((ulong)(ControlTypes)Enum.Parse(typeof(ControlTypes), n), n))
                .Where(p => p.Key != 0 && (p.Key & (p.Key - 1)) == 0)
                .GroupBy(p => p.Key)
                .Select(g => new KeyValuePair<ulong, string>(g.Key, string.Join("|", g.Select(p => p.Value).OrderBy(n => n, StringComparer.Ordinal).ToArray())))
                .OrderBy(p => p.Key)
                .ToArray());
        }

        public static List<object> Describe()
        {
            if (Debugging.DebugState.InputLocksError)
            {
                throw new InvalidOperationException("debug: injected input lock listing failure");
            }
            KeyValuePair<ulong, string>[] bits = Bits();
            List<object> locks = new List<object>();
            foreach (KeyValuePair<string, ulong> l in InputLockManager.lockStack.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                List<string> controls = new List<string>();
                ulong rest = l.Value;
                for (int i = 0; i < bits.Length; i++)
                {
                    if ((l.Value & bits[i].Key) != 0)
                    {
                        controls.Add(bits[i].Value);
                        rest &= ~bits[i].Key;
                    }
                }
                if (rest != 0)
                {
                    controls.Add("0x" + rest.ToString("X"));   // bits without a member name are not hidden
                }
                locks.Add(new Dictionary<string, object>
                {
                    ["id"] = l.Key,
                    ["mask"] = "0x" + l.Value.ToString("X"),
                    ["controls"] = controls,
                });
            }
            return locks;
        }
    }
}
