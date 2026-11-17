using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KspBridge.Mod.Modules.Editor
{
    /// <summary>
    /// Craft files inside the editor: save the ship, load a craft into the editor, list crafts. Lookup order is the current
    /// save's Ships/&lt;facility&gt; first, then the stock Ships/&lt;facility&gt;. Launching a craft from outside the editor is #35.
    /// </summary>
    internal static class CraftFiles
    {
        private const string SourceSave = "save";
        private const string SourceStock = "stock";

        private static string ShipsDir(string source, EditorFacility facility)
        {
            string root = KSPUtil.ApplicationRootPath;
            string dir = source == SourceSave
                ? Path.Combine(Path.Combine(Path.Combine(root, "saves"), HighLogic.SaveFolder), "Ships")
                : Path.Combine(root, "Ships");
            return Path.GetFullPath(Path.Combine(dir, facility.ToString()));
        }

        private static string CraftPath(string source, EditorFacility facility, string name)
            => Path.Combine(ShipsDir(source, facility), name + ".craft");

        /// <summary>Saves to the current save's Ships/&lt;facility&gt;. Existing files need overwrite=true; empty ships are refused.</summary>
        public static object Save(ShipConstruct ship, Dictionary<string, object> args)
        {
            string name = args.Str("name") ?? ship.shipName;
            EditorQuery.RequireCraftName(name);
            if (ship.parts.Count == 0)
                throw BridgeException.BadRequest("the ship has no parts; nothing to save");

            string path = CraftPath(SourceSave, EditorDriver.editorFacility, name);
            bool exists = File.Exists(path);
            if (exists && !args.Bool("overwrite"))
                throw BridgeException.BadRequest("craft '" + name + "' already exists at " + path + "; pass overwrite=true to replace it");

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            ship.shipName = name;
            if (EditorLogic.fetch.shipNameField != null)
                EditorLogic.fetch.shipNameField.text = name;
            ConfigNode node = ship.SaveShip();
            node.Save(path);
            return new Dictionary<string, object>
            {
                ["saved"] = true,
                ["name"] = name,
                ["path"] = path,
                ["overwritten"] = exists,
            };
        }

        /// <summary>
        /// Loads a craft into the editor, replacing the ship being built (unsaved changes are discarded).
        /// The game's loader does not report failure: an unreadable file or a part that is not installed only logs and leaves
        /// an empty editor. So the file is checked first (nothing is discarded when it is refused), and the editor's part count
        /// is compared with the file afterwards (the game loads synchronously inside LoadShipFromFile).
        /// </summary>
        public static object Load(Dictionary<string, object> args, Action<string> log)
        {
            string name = args.Str("name", required: true);
            EditorQuery.RequireCraftName(name);
            string facilityArg = args.Str("facility");
            EditorFacility facility = facilityArg != null ? EditorModule.ParseFacility(facilityArg) : EditorDriver.editorFacility;

            foreach (string source in new[] { SourceSave, SourceStock })
            {
                string path = CraftPath(source, facility, name);
                if (!File.Exists(path))
                    continue;
                int expected = CheckLoadable(name, path);
                log("editor load " + path);
                EditorLogic.LoadShipFromFile(path);

                ShipConstruct ship = EditorLogic.fetch != null ? EditorLogic.fetch.ship : null;
                int loaded = ship != null ? ship.parts.Count : 0;
                if (loaded != expected)
                    throw BridgeException.BadRequest("the game did not load craft '" + name + "' (" + loaded + " of " + expected
                        + " parts are in the editor); the previous ship was discarded; see Player.log");
                return new Dictionary<string, object>
                {
                    ["loaded"] = true,
                    ["name"] = name,
                    ["facility"] = facility.ToString(),
                    ["source"] = source,
                    ["path"] = path,
                    ["part_count"] = loaded,
                };
            }
            throw BridgeException.BadRequest("craft '" + name + "' not found in Ships/" + facility + " (save or stock); see editor_list_crafts");
        }

        /// <summary>Part count of a craft file the game can load; bad_request (before anything is discarded) otherwise.</summary>
        private static int CheckLoadable(string name, string path)
        {
            ConfigNode root = ConfigNode.Load(path);
            if (root == null)
                throw BridgeException.BadRequest("craft '" + name + "' at " + path + " cannot be read; nothing was loaded");
            ConfigNode[] partNodes = root.GetNodes("PART");
            if (partNodes.Length == 0)
                throw BridgeException.BadRequest("craft '" + name + "' has no parts; nothing was loaded");
            List<string> missing = EditorQuery.MissingParts(
                partNodes.Select(n => KSPUtil.GetPartName(n.GetValue("part") ?? string.Empty)),
                partName => PartLoader.getPartInfoByName(partName) != null);
            if (missing.Count > 0)
                throw BridgeException.BadRequest("craft '" + name + "' uses parts that are not installed: "
                    + string.Join(", ", missing.ToArray()) + "; nothing was loaded");
            return partNodes.Length;
        }

        /// <summary>Crafts of one facility from the current save and the stock folder, with part counts read from the files.</summary>
        public static object List(EditorFacility facility)
        {
            List<object> crafts = new List<object>();
            foreach (string source in new[] { SourceSave, SourceStock })
            {
                string dir = ShipsDir(source, facility);
                if (!Directory.Exists(dir))
                    continue;
                foreach (string path in Directory.GetFiles(dir, "*.craft").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    crafts.Add(new Dictionary<string, object>
                    {
                        ["name"] = Path.GetFileNameWithoutExtension(path),
                        ["source"] = source,
                        ["part_count"] = PartCount(path),
                    });
                }
            }
            return new Dictionary<string, object>
            {
                ["facility"] = facility.ToString(),
                ["crafts"] = crafts,
            };
        }

        /// <summary>Number of PART nodes, or null when the file cannot be read as a craft.</summary>
        private static object PartCount(string path)
        {
            ConfigNode node = ConfigNode.Load(path);
            return node != null ? (object)node.GetNodes("PART").Length : null;
        }
    }
}
