using KspBridge.Editor;
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
        }
    }
}
