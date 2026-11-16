using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

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

        /// <summary>The input lock EditorLogic.launchVessel holds over its launch flow (Lock(..., "EditorLogic_launchSequence")).</summary>
        public const string LaunchLock = "EditorLogic_launchSequence";

        private static readonly FieldInfo MissionDialogs =
            typeof(EditorLogic).GetField("missionDialogs", BindingFlags.Instance | BindingFlags.NonPublic);

        private static int _lockFreeSince = -1;   // Time.frameCount when the launch lock was first seen with nothing holding it

        /// <summary>
        /// Every physics tick (BridgeLogic.FixedUpdate); never throws. Releases an orphaned launch lock. Only the editor's launch
        /// flow sets and releases it, and it holds it legitimately only while the pre-flight dialog (TestWarning) or a recovery
        /// dialog (missionDialogs, goForLaunch waits for them) is up, plus one frame (goForLaunch DelayedCallback(1) / DelayedAbort
        /// yield null) (decompile checked). A dialog destroyed with its scene never calls OnDismiss, so its lock stays forever
        /// (measured: launch_craft refused launch_locked until the game was left by hand).
        /// </summary>
        public static void Tick()
        {
            try
            {
                if (!Flight.FlightReset.SceneReady || !InputLockManager.lockStack.ContainsKey(LaunchLock))
                {
                    _lockFreeSince = -1;
                    return;
                }
                if (HighLogic.LoadedScene != GameScenes.EDITOR)
                {
                    Orphan("outside the editor, which alone can release it");
                    return;
                }
                if (MissionDialogs == null)
                {
                    _lockFreeSince = -1;   // recovery dialogs cannot be seen: never guess inside the editor
                    return;
                }
                if (GameObject.Find(CraftFiles.PreflightDialog + " dialog handler") != null || MissionDialogsOpen())
                {
                    _lockFreeSince = -1;
                    return;
                }
                if (_lockFreeSince < 0)
                {
                    _lockFreeSince = Time.frameCount;
                    return;
                }
                if (Time.frameCount - _lockFreeSince >= 2)   // the game's own hold without a dialog is one frame
                {
                    Orphan("no pre-flight or recovery dialog for 2+ frames");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[KspBridge] launch lock check failed: " + e);
            }
        }

        private static bool MissionDialogsOpen() =>
            EditorLogic.fetch != null && MissionDialogs.GetValue(EditorLogic.fetch) is System.Collections.ICollection c && c.Count > 0;

        private static void Orphan(string why)
        {
            InputLockManager.RemoveControlLock(LaunchLock);
            _lockFreeSince = -1;
            Debug.LogWarning("[KspBridge] orphaned " + LaunchLock + " lock removed (" + why + ")");
        }

        /// <summary>Call right after asking the game to (re)start the editor.</summary>
        public static void BeginStart()
        {
            _starting = true;
            _previousEditor = EditorLogic.fetch;
        }

        // EditorLogic.Start: ... fsm.StartFSM -> yield return null -> InitAnalytics (timeInMode) -> availableCargoParts, its last
        // statement (decompile checked). FSMStarted is true one frame before that tail, and launchVessel then throws in
        // FinalizeAnalytics on the null timeInMode (measured: open_editor then launch_craft at once). The tail's last field marks
        // the coroutine done.
        private static readonly FieldInfo CargoParts =
            typeof(EditorLogic).GetField("availableCargoParts", BindingFlags.Instance | BindingFlags.NonPublic);

        private static int _tailLoggedFor;   // instance id of the EditorLogic whose held start tail was logged (not a reference)

        /// <summary>True when the editor scene has finished loading and holds a craft that tools may read or edit.</summary>
        public static bool Ready
        {
            get
            {
                EditorLogic editor = EditorLogic.fetch;
                bool started = HighLogic.LoadedScene == GameScenes.EDITOR
                    && editor != null
                    && editor.FSMStarted
                    && editor.ship != null
                    && EditorDriver.fetch != null
                    && !EditorDriver.fetch.restartingEditor
                    && !(_starting && ReferenceEquals(editor, _previousEditor));
                bool ready = started && (CargoParts == null || CargoParts.GetValue(editor) != null);
                if (started && !ready && editor.GetInstanceID() != _tailLoggedFor)
                {
                    _tailLoggedFor = editor.GetInstanceID();   // once per EditorLogic: proof this condition held a call
                    Debug.Log("[KspBridge] editor ready held: EditorLogic.Start tail pending");
                }
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

        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static MethodInfo _refreshCrewAssignment;
        private static MethodInfo _getPartExistsFilter;

        /// <summary>
        /// After Commit (SetBackup): the editor's private RefreshCrewAssignment(ShipConstruction.ShipConfig, GetPartExistsFilter()),
        /// which rebuilds the crew manifest from the backed-up craft so the crew dialog lists crewable parts that were added
        /// and drops removed ones. The editor calls it after attaching a part with crew capacity and after every delete.
        /// The editor refreshes the data only (the crew tab cannot be open while parts are edited by hand); tools can edit with
        /// the crew tab open, so the dialog is redrawn from the refreshed manifest as well.
        /// Never throws: it runs after the edit is committed, so a failure is logged instead of failing the finished edit.
        /// </summary>
        public static void RefreshCrew()
        {
            try
            {
                if (_refreshCrewAssignment == null || _getPartExistsFilter == null)
                {
                    _refreshCrewAssignment = typeof(EditorLogic).GetMethod("RefreshCrewAssignment", Private);
                    _getPartExistsFilter = typeof(EditorLogic).GetMethod("GetPartExistsFilter", Private);
                    if (_refreshCrewAssignment == null || _getPartExistsFilter == null)
                    {
                        Debug.LogError("[KspBridge] crew refresh skipped: EditorLogic.RefreshCrewAssignment / GetPartExistsFilter not found");
                        return;
                    }
                }
                EditorLogic editor = EditorLogic.fetch;
                object filter = _getPartExistsFilter.Invoke(editor, null);
                _refreshCrewAssignment.Invoke(editor, new[] { ShipConstruction.ShipConfig, filter });
                if (KSP.UI.CrewAssignmentDialog.Instance != null)
                {
                    KSP.UI.CrewAssignmentDialog.Instance.RefreshCrewLists(ShipConstruction.ShipManifest, false, true);
                }
            }
            catch (Exception e)
            {
                Exception cause = e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e;
                Debug.LogError("[KspBridge] crew refresh failed: " + cause);
            }
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
