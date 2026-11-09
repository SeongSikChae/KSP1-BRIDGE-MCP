using System.Collections.Generic;

namespace KspBridge.Editor
{
    /// <summary>State of the VAB/SPH editor scene. Main thread only.</summary>
    public static class EditorScene
    {
        /// <summary>Craft name requested by new_craft; applied once the restarted editor is ready.</summary>
        public static string PendingName;

        // An editor (re)start keeps the previous scene's EditorLogic alive for a while (EDITOR -> EDITOR takes ~2 s),
        // so after a start request only a different, started EditorLogic counts as ready.
        private static bool _starting;
        private static EditorLogic _previousEditor;

        /// <summary>Call right after asking the game to (re)start the editor.</summary>
        public static void BeginStart()
        {
            _starting = true;
            _previousEditor = EditorLogic.fetch;
        }

        /// <summary>True when the editor scene has finished loading and holds a craft that tools may read or edit.</summary>
        public static bool Ready
        {
            get
            {
                EditorLogic editor = EditorLogic.fetch;
                bool ready = HighLogic.LoadedScene == GameScenes.EDITOR
                    && editor != null
                    && editor.FSMStarted
                    && editor.ship != null
                    && EditorDriver.fetch != null
                    && !EditorDriver.fetch.restartingEditor
                    && !(_starting && ReferenceEquals(editor, _previousEditor));
                if (ready && _starting)
                {
                    _starting = false;
                    _previousEditor = null;
                }
                return ready;
            }
        }

        /// <summary>The ready editor craft, or 409 not_in_editor / editor_not_ready.</summary>
        public static ShipConstruct Require()
        {
            if (HighLogic.LoadedScene != GameScenes.EDITOR)
            {
                throw new ApiException(409, "not_in_editor", "open the VAB or SPH first (scene: " + HighLogic.LoadedScene + ")");
            }
            if (!Ready)
            {
                throw new ApiException(409, "editor_not_ready", "the editor is still loading; poll get_status until editor.ready");
            }
            ApplyPendingName();
            return EditorLogic.fetch.ship;
        }

        /// <summary>The editor field of /api/status; null outside the editor scene.</summary>
        public static object Status()
        {
            if (HighLogic.LoadedScene != GameScenes.EDITOR)
            {
                return null;
            }
            if (Ready)
            {
                ApplyPendingName();
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

        /// <summary>After a successful edit: refresh the editor (cost, DeltaV, staging) and record one Undo step.</summary>
        public static void Commit(ShipConstruct ship)
        {
            GameEvents.onEditorShipModified.Fire(ship);
            EditorLogic.fetch.SetBackup();
        }

        /// <summary>Sets the craft name shown in the editor and used by the ship.</summary>
        public static void SetName(string name)
        {
            EditorLogic.fetch.ship.shipName = name;
            EditorLogic.fetch.shipNameField.text = name;
        }

        private static void ApplyPendingName()
        {
            if (PendingName != null)
            {
                SetName(PendingName);
                PendingName = null;
            }
        }
    }
}
