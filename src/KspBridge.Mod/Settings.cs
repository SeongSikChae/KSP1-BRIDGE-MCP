using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace KspBridge
{
    /// <summary>
    /// GameData/KspBridge/settings.cfg:
    /// KSPBRIDGE { port = 8085  ATTITUDE { kp = 3  kd = 1.5  pitch_sign = 1  yaw_sign = 1  roll_sign = 1 } }
    /// </summary>
    public sealed class Settings
    {
        public const int DefaultPort = 8085;

        public static Settings Current { get; private set; } = new Settings();

        public int Port { get; private set; } = DefaultPort;
        public float Kp { get; private set; } = 3f;
        public float Kd { get; private set; } = 1.5f;
        public float PitchSign { get; private set; } = -1f;
        public float YawSign { get; private set; } = -1f;
        public float RollSign { get; private set; } = -1f;

        private static string _path;

        /// <summary>Re-reads the last loaded settings file (keeps the port).</summary>
        public static void Reload()
        {
            if (_path != null)
            {
                int port = Current.Port;
                Load(_path);
                Current.Port = port;
            }
        }

        public static Settings Load(string path)
        {
            _path = path;
            Settings settings = new Settings();
            Current = settings;
            if (!File.Exists(path))
            {
                Debug.Log("[KspBridge] settings.cfg not found, using defaults: " + path);
                return settings;
            }

            try
            {
                ConfigNode root = ConfigNode.Load(path);
                ConfigNode node = root?.GetNode("KSPBRIDGE");
                if (node == null)
                {
                    return settings;
                }
                if (node.HasValue("port"))
                {
                    if (int.TryParse(node.GetValue("port"), out int port) && port > 0 && port < 65536)
                    {
                        settings.Port = port;
                    }
                    else
                    {
                        Debug.LogWarning("[KspBridge] invalid port in settings.cfg, using " + DefaultPort);
                    }
                }
                ConfigNode att = node.GetNode("ATTITUDE");
                if (att != null)
                {
                    settings.Kp = Float(att, "kp", settings.Kp);
                    settings.Kd = Float(att, "kd", settings.Kd);
                    settings.PitchSign = Float(att, "pitch_sign", settings.PitchSign);
                    settings.YawSign = Float(att, "yaw_sign", settings.YawSign);
                    settings.RollSign = Float(att, "roll_sign", settings.RollSign);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KspBridge] failed to read settings.cfg, using defaults: " + e.Message);
            }
            return settings;
        }

        private static float Float(ConfigNode n, string key, float def)
        {
            return n.HasValue(key) && float.TryParse(n.GetValue(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;
        }
    }
}
