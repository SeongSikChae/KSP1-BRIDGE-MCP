using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Editor
{
    /// <summary>One page of a filtered list.</summary>
    public sealed class Page<T>
    {
        public Page(int total, int offset, List<T> items)
        {
            Total = total;
            Offset = offset;
            Items = items;
        }

        /// <summary>Items matching the filter, before paging.</summary>
        public int Total { get; }
        public int Offset { get; }
        public List<T> Items { get; }
    }

    /// <summary>KSP-independent rules of the editor module: craft names, part filters, paging. Unit tested.</summary>
    public static class EditorQuery
    {
        public const int DefaultLimit = 50;
        public const int MaxLimit = 200;

        // Fixed set (not Path.GetInvalidFileNameChars) so the rule is the same on every platform the game runs on.
        private static readonly char[] ForbiddenNameChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

        /// <summary>Why <paramref name="name"/> cannot be a craft file name, or null when it can. Names are never rewritten.</summary>
        public static string CraftNameError(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "craft name is empty";
            if (name != name.Trim())
                return "craft name must not start or end with spaces";
            if (name.IndexOfAny(ForbiddenNameChars) >= 0)
                return "craft name must not contain any of / \\ : * ? \" < > |";
            if (name.Any(char.IsControl))
                return "craft name must not contain control characters";
            if (name.Contains(".."))
                return "craft name must not contain '..'";
            if (name.EndsWith(".", StringComparison.Ordinal))
                return "craft name must not end with '.'";
            return null;
        }

        /// <summary>Throws bad_request when the name is not usable.</summary>
        public static void RequireCraftName(string name)
        {
            string error = CraftNameError(name);
            if (error != null)
                throw BridgeException.BadRequest(error);
        }

        /// <summary>
        /// Part names of a craft that are not installed, each once, in ordinal order. An empty name (a PART node without a
        /// part value) is reported as "(unnamed)".
        /// </summary>
        public static List<string> MissingParts(IEnumerable<string> partNames, Func<string, bool> isInstalled)
        {
            return partNames
                .Select(n => string.IsNullOrEmpty(n) ? null : n)
                .Distinct()
                .Where(n => n == null || !isInstalled(n))
                .Select(n => n ?? "(unnamed)")
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Part filter: <paramref name="category"/> equals the part category (case-insensitive) and <paramref name="search"/>
        /// is a case-insensitive substring of the part name or title. A null or empty filter matches everything.
        /// </summary>
        public static bool MatchesPart(string partName, string partTitle, string partCategory, string category, string search)
        {
            if (!string.IsNullOrEmpty(category) && !string.Equals(partCategory, category, StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.IsNullOrEmpty(search))
                return true;
            return Contains(partName, search) || Contains(partTitle, search);
        }

        private static bool Contains(string text, string part)
            => text != null && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Pages <paramref name="items"/>. limit defaults to <see cref="DefaultLimit"/> and is capped at <see cref="MaxLimit"/>;
        /// limit &lt; 1 or offset &lt; 0 is bad_request. An offset past the end gives an empty page.
        /// </summary>
        public static Page<T> Paginate<T>(IList<T> items, double? limit, double? offset)
        {
            int take = limit.HasValue ? ToInt(limit.Value, "limit") : DefaultLimit;
            int skip = offset.HasValue ? ToInt(offset.Value, "offset") : 0;
            if (take < 1)
                throw BridgeException.BadRequest("limit must be at least 1");
            if (skip < 0)
                throw BridgeException.BadRequest("offset must not be negative");
            take = Math.Min(take, MaxLimit);
            return new Page<T>(items.Count, skip, items.Skip(skip).Take(take).ToList());
        }

        private static int ToInt(double value, string name)
        {
            if (double.IsNaN(value) || value != Math.Floor(value) || value > int.MaxValue || value < int.MinValue)
                throw BridgeException.BadRequest(name + " must be an integer");
            return (int)value;
        }
    }
}
