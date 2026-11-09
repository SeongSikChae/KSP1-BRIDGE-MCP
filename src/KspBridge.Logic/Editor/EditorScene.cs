using System.Collections.Generic;

namespace KspBridge.Editor
{
    /// <summary>State of the VAB/SPH editor scene. Main thread only.</summary>
    public static class EditorScene
    {
        /// <summary>True when the editor scene has finished loading and holds a craft that tools may read or edit.</summary>
        public static bool Ready =>
            HighLogic.LoadedScene == GameScenes.EDITOR
            && EditorLogic.fetch != null
            && EditorLogic.fetch.ship != null
            && EditorDriver.fetch != null
            && !EditorDriver.fetch.restartingEditor;

        /// <summary>The editor field of /api/status; null outside the editor scene.</summary>
        public static object Status()
        {
            if (HighLogic.LoadedScene != GameScenes.EDITOR)
            {
                return null;
            }
            ShipConstruct ship = EditorLogic.fetch != null ? EditorLogic.fetch.ship : null;
            return new Dictionary<string, object>
            {
                ["facility"] = EditorDriver.editorFacility.ToString(),
                ["ready"] = Ready,
                ["craft"] = ship?.shipName,
                ["part_count"] = ship != null ? ship.parts.Count : 0,
            };
        }
    }
}
