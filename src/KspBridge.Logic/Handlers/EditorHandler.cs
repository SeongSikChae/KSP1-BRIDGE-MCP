using KspBridge.Catalog;
using KspBridge.Editor;
using System;
using System.Collections.Generic;

namespace KspBridge.Handlers
{
    /// <summary>Routes of the VAB/SPH craft design tools.</summary>
    public static class EditorHandler
    {
        public static IEnumerable<Route> Routes()
        {
            yield return new Route("POST", "/api/editor/open", (m, b, q) =>
                CraftFiles.Open(CraftFiles.ParseFacility(b.GetString("facility")), b.Has("craft") ? b.GetString("craft") : null));
            yield return new Route("POST", "/api/editor/new", (m, b, q) =>
                CraftFiles.New(b.Has("name") ? b.GetString("name") : null));
            yield return new Route("GET", "/api/parts", (m, b, q) =>
                PartCatalog.List(Empty(q["category"]), Empty(q["q"]), q["unlocked"] != "false"));
            yield return new Route("GET", "/api/parts/(?<name>[^/]+)", (m, b, q) =>
                PartCatalog.Get(Uri.UnescapeDataString(m.Groups["name"].Value)));
        }

        private static string Empty(string s) => string.IsNullOrEmpty(s) ? null : s;
    }
}
