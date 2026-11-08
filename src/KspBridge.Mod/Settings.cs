using System;
using System.IO;
using UnityEngine;

namespace KspBridge
{
    /// <summary>
    /// GameData/KspBridge/settings.cfg: KSPBRIDGE { port = 8085 }
    /// </summary>
    public sealed class Settings
    {
        public const int DefaultPort = 8085;

        public static Settings Current { get; private set; } = new Settings();

        public int Port { get; private set; } = DefaultPort;

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
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KspBridge] failed to read settings.cfg, using defaults: " + e.Message);
            }
            return settings;
        }
    }
}
