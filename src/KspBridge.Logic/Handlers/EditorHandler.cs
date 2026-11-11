using KspBridge.Catalog;
using KspBridge.Check;
using KspBridge.Editor;
using KspBridge.Render;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

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
            yield return new Route("POST", "/api/editor/render", (m, b, q) => Render(b));
            yield return new Route("GET", "/api/editor/check", (m, b, q) => Check(Empty(q["body"]), Empty(q["altitude"]), q["vacuum"] == "true"));
            // HTTP thread: add_part waits across frames for Part.Start (see PartPlacer.Add).
            yield return new Route("POST", "/api/editor/parts", (m, b, q) => PartPlacer.Add(dispatcher, ParseAdd(b)), onHttpThread: true);
            yield return new Route("DELETE", "/api/editor/parts/(?<id>[0-9]+)", (m, b, q) => PartPlacer.Remove(PathId(m)));
            yield return new Route("POST", "/api/editor/parts/(?<id>[0-9]+)/offset", (m, b, q) =>
                PartPlacer.Offset(PathId(m), new Vector3(Finite(b, "x"), Finite(b, "y"), Finite(b, "z"))));
            yield return new Route("POST", "/api/editor/parts/(?<id>[0-9]+)/rotate", (m, b, q) =>
                PartPlacer.Rotate(PathId(m), ParseAxis(b.GetString("axis")), Finite(b, "angle", required: true)));
            yield return new Route("POST", "/api/editor/parts/(?<id>[0-9]+)/field", (m, b, q) =>
                PartSettings.SetField(PathId(m), b.GetString("module"), b.GetString("field"),
                    b.GetRaw("value") ?? throw ApiException.BadRequest("'value' is required")));
            yield return new Route("POST", "/api/editor/parts/(?<id>[0-9]+)/stage", (m, b, q) =>
                Staging.Move(PathId(m), OptionalIndex(b, "stage"), OptionalIndex(b, "new_stage_at")));
            yield return new Route("POST", "/api/editor/parts/(?<id>[0-9]+)/resource", (m, b, q) =>
                PartSettings.SetResource(PathId(m), b.GetString("resource"), b.GetDouble("fraction")));
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

        private static uint PathId(Match m)
        {
            if (!uint.TryParse(m.Groups["id"].Value, out uint id))
            {
                throw ApiException.BadRequest("id must be a part id from get_craft");
            }
            return id;
        }

        /// <summary>A whole number from the body, or null when omitted.</summary>
        private static int? OptionalIndex(RequestBody b, string name)
        {
            if (!b.Has(name))
            {
                return null;
            }
            double v = b.GetDouble(name);
            if (v != Math.Floor(v) || v < 0 || v > 1000)
            {
                throw ApiException.BadRequest(name + " must be a whole number 0..1000");
            }
            return (int)v;
        }

        /// <summary>A number from the body; 0 when omitted unless required.</summary>
        private static float Finite(RequestBody b, string name, bool required = false)
        {
            double v = required ? b.GetDouble(name) : b.GetDouble(name, 0);
            if (double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > 1000)
            {
                throw ApiException.BadRequest(name + " must be a finite number within ±1000");
            }
            return (float)v;
        }

        private static Vector3 ParseAxis(string axis)
        {
            switch (axis.ToLowerInvariant())
            {
                case "x": return Vector3.right;
                case "y": return Vector3.up;
                case "z": return Vector3.forward;
                default: throw ApiException.BadRequest("axis must be x, y or z (root-part space)");
            }
        }

        private static uint ParseId(double value, string name)
        {
            if (value < 0 || value > uint.MaxValue || value != Math.Floor(value))
            {
                throw ApiException.BadRequest(name + " must be a part id from get_craft");
            }
            return (uint)value;
        }

        private static object Render(RequestBody b)
        {
            List<CraftRenderer.View> views = b.Has("views")
                ? b.GetList("views").Select(v => CraftRenderer.ParseView(v as string ?? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture))).ToList()
                : new[] { "side", "front", "iso" }.Select(CraftRenderer.ParseView).ToList();
            if (views.Count == 0 || views.Count > CraftRenderer.MaxViews)
            {
                throw ApiException.BadRequest("views must have 1.." + CraftRenderer.MaxViews + " entries");
            }
            double resolution = b.GetDouble("resolution", 512);
            if (resolution != Math.Floor(resolution) || resolution < CraftRenderer.MinResolution || resolution > CraftRenderer.MaxResolution)
            {
                throw ApiException.BadRequest("resolution must be a whole number " + CraftRenderer.MinResolution + ".." + CraftRenderer.MaxResolution);
            }
            ShipConstruct ship = EditorScene.Require();
            if (ship.parts.Count == 0)
            {
                throw new ApiException(409, "nothing_to_render", "the craft is empty");
            }
            return CraftRenderer.Render(ship, views, (int)resolution, b.Has("markers") && b.GetBool("markers"));
        }

        private static object Check(string bodyName, string altitudeText, bool vacuum)
        {
            ShipConstruct ship = EditorScene.Require();
            CelestialBody body = PerformanceCheck.Body(bodyName);
            double altitude = 0;
            if (altitudeText != null && (!double.TryParse(altitudeText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out altitude) || double.IsNaN(altitude) || altitude < 0 || altitude > 1e9))
            {
                throw ApiException.BadRequest("altitude must be a number of metres 0..1e9");
            }
            return new Dictionary<string, object>
            {
                ["structure"] = Structure(ship),
                ["performance"] = ship.parts.Count == 0 ? null : PerformanceCheck.Run(ship, body, altitude, vacuum),
            };
        }

        private static Dictionary<string, object> Structure(ShipConstruct ship)
        {
            List<Dictionary<string, object>> issues = StructureCheck.Run(ship);
            return new Dictionary<string, object>
            {
                ["ok"] = issues.All(i => (string)i["severity"] != "error"),
                ["issues"] = issues.Cast<object>().ToList(),
            };
        }

        private static object GetCraft()
        {
            ShipConstruct ship = EditorScene.Require();
            return new Dictionary<string, object>
            {
                ["editor"] = EditorScene.Status(),
                ["parts"] = ship.parts.Select(p => (object)PartIndex.Summary(ship, p)).ToList(),
                ["stages"] = Staging.List(),
            };
        }
    }
}
