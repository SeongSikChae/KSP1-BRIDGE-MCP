using KspBridge.Check;
using KspBridge.Editor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace KspBridge.Render
{
    /// <summary>Pictures of the editor craft from fixed or given directions (render_craft). Main thread only.</summary>
    public static class CraftRenderer
    {
        public const int MaxViews = 4;
        public const int MinResolution = 64;
        public const int MaxResolution = 1024;
        private const float FieldOfView = 30f;
        private static readonly Color Background = new Color(0.18f, 0.2f, 0.23f);

        /// <summary>A view direction: the camera looks at the craft from Direction (root-part space, unit length).</summary>
        public struct View
        {
            public string Name;
            public Vector3 Direction;
        }

        /// <summary>
        /// A view by name (front = from +Y, side = from +X, top = from +Z, iso = from (1,1,1)) or "azimuth,elevation" in degrees:
        /// azimuth around the root part's +Y (0 = +Z, 90 = +X), elevation from its XZ plane toward +Y.
        /// </summary>
        public static View ParseView(string text)
        {
            string name = (text ?? "").Trim();
            switch (name.ToLowerInvariant())
            {
                case "front": return new View { Name = "front", Direction = Vector3.up };
                case "side": return new View { Name = "side", Direction = Vector3.right };
                case "top": return new View { Name = "top", Direction = Vector3.forward };
                case "iso": return new View { Name = "iso", Direction = new Vector3(1, 1, 1).normalized };
            }
            string[] parts = name.Split(',');
            if (parts.Length == 2
                && float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float az)
                && float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float el)
                && !float.IsNaN(az) && !float.IsInfinity(az) && el >= -90 && el <= 90)
            {
                float a = az * Mathf.Deg2Rad, e = el * Mathf.Deg2Rad;
                return new View { Name = name, Direction = new Vector3(Mathf.Cos(e) * Mathf.Sin(a), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Cos(a)) };
            }
            throw ApiException.BadRequest("view '" + text + "' must be front, side, top, iso or \"azimuth,elevation\" in degrees (elevation -90..90)");
        }

        public static object Render(ShipConstruct ship, List<View> views, int resolution, bool markers)
        {
            Part root = PartIndex.Root(ship);
            List<Renderer> renderers = ship.parts.Where(p => p != null)
                .SelectMany(p => p.GetComponentsInChildren<Renderer>())
                .Where(r => r.enabled && r.gameObject.activeInHierarchy && (r is MeshRenderer || r is SkinnedMeshRenderer))
                .ToList();
            if (renderers.Count == 0)
            {
                throw new ApiException(409, "nothing_to_render", "the craft has no visible parts");
            }
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers.Skip(1))
            {
                bounds.Encapsulate(r.bounds);
            }
            // Only the layers the craft's own renderers are on, so the building, floor and crew walking around stay out.
            int mask = renderers.Aggregate(0, (m, r) => m | (1 << r.gameObject.layer));
            List<(Vector3 World, Color Color)> points = markers ? MarkerPoints(root) : null;

            GameObject go = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                go = new GameObject("KspBridgeRenderCamera");
                rt = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                tex = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
                Camera cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Background;
                cam.cullingMask = mask;
                cam.fieldOfView = FieldOfView;
                cam.targetTexture = rt;
                List<Vector3> corners = renderers.SelectMany(r => Corners(r.bounds)).ToList();
                float tanHalf = Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
                List<object> images = new List<object>();
                foreach (View v in views)
                {
                    Vector3 dir = root.transform.rotation * v.Direction;
                    // Keep the root part's +Y up on screen; looking along it, use +Z instead.
                    Vector3 up = Mathf.Abs(Vector3.Dot(v.Direction, Vector3.up)) > 0.99f ? root.transform.forward : root.transform.up;
                    cam.transform.position = bounds.center + dir;
                    cam.transform.LookAt(bounds.center, up);
                    Vector3 right = cam.transform.right, camUp = cam.transform.up;
                    // Aim at the middle of the craft as seen from this side, then back off until every corner fits.
                    float xMin = corners.Min(c => Vector3.Dot(c - bounds.center, right)), xMax = corners.Max(c => Vector3.Dot(c - bounds.center, right));
                    float yMin = corners.Min(c => Vector3.Dot(c - bounds.center, camUp)), yMax = corners.Max(c => Vector3.Dot(c - bounds.center, camUp));
                    Vector3 target = bounds.center + right * ((xMin + xMax) * 0.5f) + camUp * ((yMin + yMax) * 0.5f);
                    float distance = 0.1f, zMin = 0f, zMax = 0f;
                    foreach (Vector3 c in corners)
                    {
                        Vector3 rel = c - target;
                        float z = Vector3.Dot(rel, dir);
                        float spread = Mathf.Max(Mathf.Abs(Vector3.Dot(rel, right)), Mathf.Abs(Vector3.Dot(rel, camUp)));
                        distance = Mathf.Max(distance, spread / tanHalf + z);
                        zMin = Mathf.Min(zMin, z);
                        zMax = Mathf.Max(zMax, z);
                    }
                    distance *= 1.05f;
                    cam.transform.position = target + dir * distance;
                    cam.transform.LookAt(target, up);
                    cam.nearClipPlane = Mathf.Max(0.01f, distance - zMax - 0.5f);
                    cam.farClipPlane = distance - zMin + 0.5f;
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                    if (points != null)
                    {
                        foreach ((Vector3 world, Color color) in points)
                        {
                            Dot(tex, cam.WorldToScreenPoint(world), Mathf.Max(3, resolution / 80), color);
                        }
                    }
                    images.Add(new Dictionary<string, object>
                    {
                        ["view"] = v.Name,
                        ["width"] = resolution,
                        ["height"] = resolution,
                        ["png"] = Convert.ToBase64String(tex.EncodeToPNG()),
                    });
                }
                return new Dictionary<string, object> { ["images"] = images };
            }
            finally
            {
                RenderTexture.active = previous;
                if (tex != null)
                {
                    UnityEngine.Object.Destroy(tex);
                }
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.Destroy(rt);
                }
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                }
            }
        }

        private static IEnumerable<Vector3> Corners(Bounds b)
        {
            Vector3 c = b.center, e = b.extents;
            for (int i = 0; i < 8; i++)
            {
                yield return c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            }
        }

        /// <summary>CoM yellow, CoT red, CoL blue, from the same editor marker functions as check_craft; missing ones are left out.</summary>
        private static List<(Vector3, Color)> MarkerPoints(Part root)
        {
            List<(Vector3, Color)> points = new List<(Vector3, Color)> { (EditorMarker_CoM.findCenterOfMass(root), Color.yellow) };
            Ray cot = EditorMarker_CoT.FindCoT(root);
            if (cot.direction != Vector3.zero)
            {
                points.Add((cot.origin, Color.red));
            }
            Ray? col = PerformanceCheck.CenterOfLift(root);
            if (col.HasValue)
            {
                points.Add((col.Value.origin, Color.blue));
            }
            return points;
        }

        /// <summary>A filled dot with a black rim at a screen point (pixels from the bottom left, as ReadPixels stores them).</summary>
        private static void Dot(Texture2D tex, Vector3 screen, int radius, Color color)
        {
            if (screen.z <= 0)
            {
                return;
            }
            int cx = Mathf.RoundToInt(screen.x), cy = Mathf.RoundToInt(screen.y);
            int rim = radius + 2;
            for (int y = cy - rim; y <= cy + rim; y++)
            {
                for (int x = cx - rim; x <= cx + rim; x++)
                {
                    if (x < 0 || y < 0 || x >= tex.width || y >= tex.height)
                    {
                        continue;
                    }
                    int d2 = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                    if (d2 <= radius * radius)
                    {
                        tex.SetPixel(x, y, color);
                    }
                    else if (d2 <= rim * rim)
                    {
                        tex.SetPixel(x, y, Color.black);
                    }
                }
            }
        }
    }
}
