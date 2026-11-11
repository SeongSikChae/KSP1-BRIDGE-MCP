using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace KspBridge.Editor
{
    /// <summary>Opening the editor and craft files. Main thread only.</summary>
    public static class CraftFiles
    {
        /// <summary>Starts the VAB/SPH (optionally loading a saved craft). Returns at once; completion shows in get_status editor.ready.</summary>
        public static object Open(EditorFacility facility, string craft)
        {
            RequireOpenableScene();
            if (craft == null)
            {
                StartClean(facility);
            }
            else
            {
                string path = CraftPath(facility, craft);
                if (!File.Exists(path))
                {
                    throw ApiException.NotFound("no " + facility + " craft named '" + craft + "' (" + path + ")");
                }
                EditorDriver.StartAndLoadVessel(path, facility);
            }
            EditorScene.BeginStart();
            EditorScene.PendingName = null;
            return new Dictionary<string, object> { ["accepted"] = true, ["facility"] = facility.ToString(), ["craft"] = craft };
        }

        /// <summary>Restarts the current editor with an empty craft (unsaved changes are discarded). Returns at once.</summary>
        public static object New(string name)
        {
            if (HighLogic.LoadedScene != GameScenes.EDITOR)
            {
                throw new ApiException(409, "not_in_editor", "open the VAB or SPH first (scene: " + HighLogic.LoadedScene + ")");
            }
            EditorFacility facility = EditorDriver.editorFacility;
            if (EditorLogic.fetch != null && EditorLogic.fetch.ship != null && FlightGlobals.fetch != null)
            {
                FlightGlobals.PersistentVesselIds.Remove(EditorLogic.fetch.ship.persistentId);
            }
            StartClean(facility);
            EditorScene.BeginStart();
            EditorScene.PendingName = name;
            return new Dictionary<string, object> { ["accepted"] = true, ["facility"] = facility.ToString(), ["craft"] = name };
        }

        /// <summary>
        /// The state the editor's new-craft button sets (EditorLogic.OnNewShipConfirm: START_CLEAN, ShipConfig cleared), then a
        /// scene reload: without them the editor reloads its cached craft (EditorDriver.StartupBehaviour stays LOAD_FROM_CACHE,
        /// e.g. after a flight). Unlike clicking the VAB/SPH building, a cached craft of the same type is not resumed.
        /// </summary>
        private static void StartClean(EditorFacility facility)
        {
            EditorDriver.StartupBehaviour = EditorDriver.StartupBehaviours.START_CLEAN;
            ShipConstruction.ShipConfig = null;
            EditorDriver.StartEditor(facility);
        }

        private static readonly System.Reflection.MethodInfo OnSaveConfirm =
            typeof(EditorLogic).GetMethod("onSaveConfirm", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        /// <summary>
        /// Main thread. Saves the editor craft under a name like the editor's Save button: the name goes into the name field and
        /// the button's confirm step runs (EditorLogic.onSaveConfirm: name, description and flag into the craft, backup, saved-state
        /// bookkeeping, then ShipConstruction.SaveShipToPath with the thumbnail) into the editor's current save folder.
        /// An existing file is replaced only with overwrite (unlike the button, which re-saves the craft last saved under that
        /// name without asking); a craft with structure errors is not saved. Mission modes are not supported (the button's
        /// mission checks are not replayed).
        /// </summary>
        public static object Save(string name, bool overwrite)
        {
            ShipConstruct ship = EditorScene.Require();
            if (HighLogic.CurrentGame.Mode == Game.Modes.MISSION || HighLogic.CurrentGame.Mode == Game.Modes.MISSION_BUILDER)
            {
                throw new ApiException(409, "not_supported", "save_craft does not support mission games; use the editor's Save button");
            }
            name = (name ?? "").Trim();
            if (name.Length == 0)
            {
                throw ApiException.BadRequest("name is required");
            }
            if (name.Any(char.IsControl))
            {
                throw ApiException.BadRequest("name must not contain control characters");
            }
            if (ship.parts.Count == 0)
            {
                throw new ApiException(409, "unsaveable", "the craft is empty");
            }
            if (OnSaveConfirm == null)
            {
                throw new ApiException(500, "save_unavailable", "EditorLogic.onSaveConfirm was not found in this game version");
            }

            // The button saves the name field's text, which the field may shorten (the editor's craft name length limit),
            // so the name is put there first and every check below uses what the field holds.
            string previousText = EditorLogic.fetch.shipNameField.text;
            bool saved = false;
            try
            {
                EditorLogic.fetch.shipNameField.text = name;
                string fieldName = EditorLogic.fetch.shipNameField.text;
                // The same path the Save button checks and writes (the file name is the sanitized craft name).
                string path = Path.GetFullPath(ShipConstruction.GetSavePath(fieldName));
                if (File.Exists(path) && !overwrite)
                {
                    throw new ApiException(409, "name_exists", "a craft named '" + fieldName + "' already exists (" + path + "); set overwrite=true to replace it");
                }
                List<Dictionary<string, object>> issues = Check.StructureCheck.Run(ship);
                List<Dictionary<string, object>> errors = issues.Where(i => (string)i["severity"] == "error").ToList();
                if (errors.Count > 0)
                {
                    throw new ApiException(409, "unsaveable", "fix the structure errors first (check_craft): "
                        + string.Join("; ", errors.Select(e => (string)e["message"])));
                }
                try
                {
                    OnSaveConfirm.Invoke(EditorLogic.fetch, null);
                    saved = true;
                }
                catch (System.Reflection.TargetInvocationException e) when (e.InnerException != null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                    throw;
                }
                return new Dictionary<string, object>
                {
                    ["name"] = ship.shipName,
                    ["file"] = Path.GetFileNameWithoutExtension(path),
                    ["facility"] = EditorDriver.editorFacility.ToString(),
                    ["path"] = path,
                    ["warnings"] = issues.Where(i => (string)i["severity"] != "error").Cast<object>().ToList(),
                };
            }
            finally
            {
                if (!saved)
                {
                    EditorLogic.fetch.shipNameField.text = previousText;
                }
            }
        }

        /// <summary>
        /// The saved crafts of this save (Ships/VAB, Ships/SPH, not their subfolders): file name (use it in open_editor), part count
        /// (top-level PART nodes; parts stored in inventories are not counted), modified time. An unreadable file is listed with error.
        /// </summary>
        public static object List(EditorFacility? facility)
        {
            if (HighLogic.CurrentGame == null)
            {
                throw new ApiException(409, "no_game", "load a save first");
            }
            List<object> crafts = new List<object>();
            foreach (EditorFacility f in facility.HasValue ? new[] { facility.Value } : new[] { EditorFacility.VAB, EditorFacility.SPH })
            {
                string dir = ShipConstruction.GetShipsPathFor(HighLogic.SaveFolder, f);
                if (!Directory.Exists(dir))
                {
                    continue;
                }
                foreach (string file in Directory.GetFiles(dir, "*.craft").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    Dictionary<string, object> craft = new Dictionary<string, object>
                    {
                        ["name"] = Path.GetFileNameWithoutExtension(file),
                        ["facility"] = f.ToString(),
                    };
                    try
                    {
                        ConfigNode node = ConfigNode.Load(file);
                        craft["parts"] = node != null ? node.GetNodes("PART").Length : 0;
                        craft["modified"] = File.GetLastWriteTimeUtc(file).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        craft["error"] = e.Message;
                    }
                    crafts.Add(craft);
                }
            }
            return new Dictionary<string, object> { ["crafts"] = crafts };
        }

        /// <summary>
        /// Main thread. Launches the editor craft like the Launch button (EditorLogic.launchVessel: auto-save, crew, the game's
        /// pre-flight checks and their dialogs, then the flight scene). Returns at once; the result shows in get_status (scene FLIGHT).
        /// site null = the launch site picked in the editor. The game would silently swap an unknown site for its default, so an
        /// unknown site is refused here.
        /// </summary>
        public static object Launch(string site)
        {
            ShipConstruct ship = EditorScene.Require();
            if (ship.parts.Count == 0)
            {
                throw new ApiException(409, "unsaveable", "the craft is empty");
            }
            Game.Modes mode = HighLogic.CurrentGame.Mode;
            if (mode == Game.Modes.MISSION || mode == Game.Modes.MISSION_BUILDER || mode == Game.Modes.SCENARIO_NON_RESUMABLE)
            {
                throw new ApiException(409, "not_supported", "launch_craft does not support " + mode + " games; use the editor's Launch button");
            }
            // The Launch button is usable only while EDITOR_LAUNCH is unlocked (EditorLogic: launchBtn.interactable). A launch in
            // progress holds that lock (its pre-flight dialog included), as do the load/flag dialogs, CanLaunch=false and tutorials.
            // EditorDriver_AppFocus only blocks the player's input while the game window is in the background (OnApplicationFocus),
            // which is how an agent usually calls, so it does not count.
            List<string> launchLocks = InputLockManager.lockStack
                .Where(l => l.Key != "EditorDriver_AppFocus" && (l.Value & (ulong)ControlTypes.EDITOR_LAUNCH) != 0)
                .Select(l => l.Key).ToList();
            if (launchLocks.Count > 0)
            {
                throw new ApiException(409, "launch_locked", "launching is locked in the editor now (a launch or its pre-flight dialog " +
                    "in progress, another dialog open, or launching not allowed in this game); locks: " + string.Join(", ", launchLocks));
            }
            site = string.IsNullOrWhiteSpace(site) ? EditorLogic.fetch.launchSiteName : site.Trim();
            if (!EditorDriver.ValidLaunchSite(site))
            {
                throw ApiException.BadRequest("invalid launch site '" + site + "' (valid: " + string.Join(", ", EditorDriver.ValidLaunchSites) + ")");
            }
            List<Dictionary<string, object>> errors = Check.StructureCheck.Run(ship).Where(i => (string)i["severity"] == "error").ToList();
            if (errors.Count > 0)
            {
                throw new ApiException(409, "unsaveable", "fix the structure errors first (check_craft): "
                    + string.Join("; ", errors.Select(e => (string)e["message"])));
            }
            EditorLogic.fetch.launchVessel(site);
            return new Dictionary<string, object> { ["accepted"] = true, ["site"] = site };
        }

        public static string CraftPath(EditorFacility facility, string name)
        {
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw ApiException.BadRequest("invalid craft name: " + name);
            }
            return Path.Combine(ShipConstruction.GetShipsPathFor(HighLogic.SaveFolder, facility), name + ".craft");
        }

        public static EditorFacility ParseFacility(string text)
        {
            switch (text.ToUpperInvariant())
            {
                case "VAB":
                    return EditorFacility.VAB;
                case "SPH":
                    return EditorFacility.SPH;
                default:
                    throw ApiException.BadRequest("facility must be VAB or SPH");
            }
        }

        /// <summary>The editor may be opened from the space center or the editor; from flight it would abandon the vessel.</summary>
        private static void RequireOpenableScene()
        {
            if (HighLogic.CurrentGame == null)
            {
                throw new ApiException(409, "no_game", "load a save first");
            }
            if (HighLogic.LoadedScene != GameScenes.SPACECENTER && HighLogic.LoadedScene != GameScenes.EDITOR)
            {
                throw ApiException.SceneNotAllowed("open_editor");
            }
        }
    }
}
