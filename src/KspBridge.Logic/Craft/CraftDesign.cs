using System;
using System.Collections.Generic;
using System.Globalization;

namespace KspBridge.Craft
{
    /// <summary>
    /// One node of the design tree.
    /// Stack child: { "node": "bottom", "part": ..., children/radial/stage }.
    /// Radial entry: { "part": ..., "symmetry": 3, "angle": 0, "height": 0, children/radial/stage } (surface-attached around the parent).
    /// Child of a radial part with "node": "srf" is surface-attached on the parent's outer face.
    /// </summary>
    public sealed class PartSpec
    {
        public string Part;
        public string Node;
        public int Symmetry = 1;
        public float Angle;
        public float Height;
        public int? Stage;
        /// <summary>Engine thrust limiter in percent (VAB "Thrust Limiter"), null = game default 100.</summary>
        public float? ThrustLimit;
        /// <summary>Initial fill per resource name, 0..1 of maxAmount (null = part default, usually full).</summary>
        public Dictionary<string, double> Resources;
        public readonly List<PartSpec> Children = new List<PartSpec>();
        public readonly List<PartSpec> Radial = new List<PartSpec>();

        public static PartSpec Parse(object value, string path, bool isRadial = false)
        {
            if (!(value is Dictionary<string, object> d))
            {
                throw ApiException.BadRequest(path + ": must be an object");
            }

            PartSpec spec = new PartSpec
            {
                Part = d.TryGetValue("part", out object p) && p is string s && s.Length > 0
                    ? s
                    : throw ApiException.BadRequest(path + ".part: required"),
                Node = d.TryGetValue("node", out object n) ? n as string : null,
            };

            if (isRadial)
            {
                spec.Symmetry = (int)Num(d, "symmetry", 1, path);
                if (spec.Symmetry < 1 || spec.Symmetry > 8)
                {
                    throw ApiException.BadRequest(path + ".symmetry: 1..8");
                }
                spec.Angle = (float)Num(d, "angle", 0, path);
                spec.Height = (float)Num(d, "height", 0, path);
            }
            if (d.TryGetValue("stage", out object st) && st != null)
            {
                spec.Stage = (int)Num(d, "stage", 0, path);
            }
            if (d.TryGetValue("thrust_limit", out object tl) && tl != null)
            {
                double limit = Num(d, "thrust_limit", 100, path);
                if (limit < 0 || limit > 100)
                {
                    throw ApiException.BadRequest(path + ".thrust_limit: 0..100 (percent)");
                }
                spec.ThrustLimit = (float)limit;
            }
            if (d.TryGetValue("resources", out object rs) && rs != null)
            {
                Dictionary<string, object> map = rs as Dictionary<string, object>
                    ?? throw ApiException.BadRequest(path + ".resources: must be an object {resource name: fill 0..1}");
                spec.Resources = new Dictionary<string, double>();
                foreach (KeyValuePair<string, object> kv in map)
                {
                    if (!(kv.Value is double fill) || fill < 0 || fill > 1)
                    {
                        throw ApiException.BadRequest(path + ".resources." + kv.Key + ": fill fraction 0..1");
                    }
                    spec.Resources[kv.Key] = fill;
                }
            }

            if (d.TryGetValue("children", out object ch) && ch != null)
            {
                List<object> list = ch as List<object> ?? throw ApiException.BadRequest(path + ".children: must be an array");
                for (int i = 0; i < list.Count; i++)
                {
                    PartSpec c = Parse(list[i], path + ".children[" + i + "]");
                    if (string.IsNullOrEmpty(c.Node))
                    {
                        throw ApiException.BadRequest(path + ".children[" + i + "].node: required (attach node of the parent, or 'srf')");
                    }
                    spec.Children.Add(c);
                }
            }
            if (d.TryGetValue("radial", out object rd) && rd != null)
            {
                List<object> list = rd as List<object> ?? throw ApiException.BadRequest(path + ".radial: must be an array");
                for (int i = 0; i < list.Count; i++)
                {
                    spec.Radial.Add(Parse(list[i], path + ".radial[" + i + "]", true));
                }
            }
            return spec;
        }

        private static double Num(Dictionary<string, object> d, string key, double def, string path)
        {
            if (!d.TryGetValue(key, out object v) || v == null)
            {
                return def;
            }
            if (v is double x)
            {
                return x;
            }
            throw ApiException.BadRequest(path + "." + key + ": must be a number");
        }
    }

    internal static class Fmt
    {
        public static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        public static string V(UnityEngine.Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
        public static string Q(UnityEngine.Quaternion q) => F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w);
        public static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
        public static string U(uint v) => v.ToString(CultureInfo.InvariantCulture);

        public static string SafeName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                if (name.IndexOf(c) >= 0)
                {
                    throw ApiException.BadRequest("craft name contains an invalid character: '" + c + "'");
                }
            }
            if (name.Trim().Length == 0 || name.Length > 64)
            {
                throw ApiException.BadRequest("craft name must be 1..64 characters");
            }
            return name.Trim();
        }
    }
}
