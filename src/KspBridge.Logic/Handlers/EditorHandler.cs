using KspBridge.Catalog;
using KspBridge.Editor;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Handlers
{
    /// <summary>Routes of the VAB/SPH craft design tools.</summary>
    public static class EditorHandler
    {
        public static IEnumerable<Route> Routes(MainThreadDispatcher dispatcher)
        {
            yield return new Route("POST", "/api/editor/open", (m, b, q) =>
                CraftFiles.Open(CraftFiles.ParseFacility(b.GetString("facility")), b.Has("craft") ? b.GetString("craft") : null));
            yield return new Route("POST", "/api/editor/new", (m, b, q) =>
                CraftFiles.New(b.Has("name") ? b.GetString("name") : null));
            yield return new Route("GET", "/api/editor/craft", (m, b, q) => GetCraft());
            // HTTP thread: add_part waits across frames for Part.Start (see PartPlacer.Add).
            yield return new Route("POST", "/api/editor/parts", (m, b, q) => PartPlacer.Add(dispatcher, ParseAdd(b)), onHttpThread: true);
            yield return new Route("GET", "/api/parts", (m, b, q) =>
                PartCatalog.List(Empty(q["category"]), Empty(q["q"]), q["unlocked"] != "false"));
            yield return new Route("GET", "/api/parts/(?<name>[^/]+)", (m, b, q) =>
                PartCatalog.Get(Uri.UnescapeDataString(m.Groups["name"].Value)));
        }

        private static string Empty(string s) => string.IsNullOrEmpty(s) ? null : s;

        private static AddRequest ParseAdd(RequestBody b)
        {
            AddRequest r = new AddRequest { Part = b.GetString("part") };
            if (b.Has("parent"))
            {
                r.Parent = ParseId(b.GetDouble("parent"), "parent");
            }
            if (b.Has("node") && b.Has("surface"))
            {
                throw ApiException.BadRequest("give either node (stack attach) or surface (surface attach), not both");
            }
            if (b.Has("node"))
            {
                RequestBody node = new RequestBody(b.GetObject("node"));
                r.ParentNode = node.GetString("parent");
                r.ChildNode = node.Has("child") ? node.GetString("child") : null;
            }
            if (b.Has("surface"))
            {
                RequestBody s = new RequestBody(b.GetObject("surface"));
                double height = s.GetDouble("height");
                if (height < 0 || height > 1)
                {
                    throw ApiException.BadRequest("surface.height must be 0..1");
                }
                r.Surface = new SurfaceSpec { Azimuth = (float)s.GetDouble("azimuth"), Height = (float)height, Angle = (float)s.GetDouble("angle", 0) };
            }
            if (b.Has("symmetry"))
            {
                if (r.Surface == null)
                {
                    throw ApiException.BadRequest("symmetry needs a surface attach (stack symmetry is not supported)");
                }
                RequestBody sym = new RequestBody(b.GetObject("symmetry"));
                string mode = sym.GetString("mode").ToLowerInvariant();
                int count = (int)sym.GetDouble("count");
                if (mode == "radial" && (count < 1 || count > 8))
                {
                    throw ApiException.BadRequest("radial symmetry.count must be 1..8");
                }
                if (mode == "mirror" && count != 2)
                {
                    throw ApiException.BadRequest("mirror symmetry.count must be 2");
                }
                if (mode != "radial" && mode != "mirror")
                {
                    throw ApiException.BadRequest("symmetry.mode must be radial or mirror");
                }
                r.Symmetry = new SymmetrySpec { Mode = mode == "mirror" ? SymmetryMethod.Mirror : SymmetryMethod.Radial, Count = count };
            }
            return r;
        }

        private static uint ParseId(double value, string name)
        {
            if (value < 0 || value > uint.MaxValue || value != Math.Floor(value))
            {
                throw ApiException.BadRequest(name + " must be a part id from get_craft");
            }
            return (uint)value;
        }

        private static object GetCraft()
        {
            ShipConstruct ship = EditorScene.Require();
            return new Dictionary<string, object>
            {
                ["editor"] = EditorScene.Status(),
                ["parts"] = ship.parts.Select(p => (object)PartIndex.Summary(ship, p)).ToList(),
                ["stages"] = new List<object>(),   // filled by U9 (Staging.List)
            };
        }
    }
}
