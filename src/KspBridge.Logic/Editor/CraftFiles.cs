using System.Collections.Generic;
using System.IO;

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
                EditorDriver.StartEditor(facility);
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
            EditorDriver.StartEditor(facility);
            EditorScene.BeginStart();
            EditorScene.PendingName = name;
            return new Dictionary<string, object> { ["accepted"] = true, ["facility"] = facility.ToString(), ["craft"] = name };
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
