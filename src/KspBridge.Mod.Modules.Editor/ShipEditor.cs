using KspBridge.Mod.Core;
using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace KspBridge.Mod.Modules.Editor
{
    /// <summary>
    /// KSP side of ship editing. Structural edits rebuild the craft node and reload it through the game's own loader:
    /// save the ship (the original, kept for rollback) → edit a KSP-independent copy (CraftDoc) → check it → write a temp
    /// craft → EditorLogic.LoadShipFromFile. The loader leaves work for later frames (each part's Start, the stage manager's
    /// sort two frames later), and request handlers run inside one frame, so an edit is not answered in the same call:
    /// it stays "pending" and the server asks edit_result until the editor has settled, then the result is checked against
    /// what the edit expected and either returned or rolled back. Only one edit is pending at a time.
    /// </summary>
    internal sealed class ShipEditor
    {
        /// <summary>Frames to wait after a reload at least (the stage manager sorts two frames after the editor starts).</summary>
        private const int SettleFrames = 3;

        private static readonly FieldInfo SortRoutineField =
            typeof(StageManager).GetField("sortRoutine", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly Action<string> log;
        private int nextEditId = 1;
        private PendingEdit pending;
        private FinishedEdit last;

        public ShipEditor(Action<string> log) => this.log = log;

        public bool HasPending => pending != null;

        // ---- craft files and conversion ----

        private static string TempCraftPath
            => Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KspBridge/PluginData/editor/edit.craft"));

        /// <summary>Where the game writes the thumbnail of a craft outside a save: Ships/@thumbs/&lt;folder&gt;/&lt;file&gt;.png.</summary>
        private static string TempThumbPath
            => Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "Ships/@thumbs/editor/edit.png"));

        public static CfgNode ToCfg(ConfigNode node)
        {
            CfgNode result = new CfgNode(node.name);
            foreach (ConfigNode.Value value in node.values)
                result.AddValue(value.name, value.value);
            foreach (ConfigNode child in node.nodes)
                result.AddNode(ToCfg(child));
            return result;
        }

        public static ConfigNode ToConfigNode(CfgNode node)
        {
            ConfigNode result = new ConfigNode(node.Name);
            foreach (KeyValuePair<string, string> value in node.Values)
                result.AddValue(value.Key, value.Value);
            foreach (CfgNode child in node.Nodes)
                result.AddNode(ToConfigNode(child));
            return result;
        }

        /// <summary>The ship as a craft node (what the game writes on save).</summary>
        public static CfgNode SaveCurrent(ShipConstruct ship) => ToCfg(ship.SaveShip());

        /// <summary>
        /// Writes <paramref name="craft"/> to the temp file and loads it into the editor (synchronous; the editor restarts
        /// without a scene load). Returns the frame of the load, from which settling is counted.
        /// </summary>
        public int Reload(CfgNode craft)
        {
            string path = TempCraftPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            ToConfigNode(craft).Save(path);
            KeepThumbnail(path);
            log("editor reload " + path + " (" + craft.GetNodes("PART").Count + " parts)");
            EditorLogic.LoadShipFromFile(path);
            return Time.frameCount;
        }

        /// <summary>
        /// The loader renders a thumbnail one frame later unless the existing one is newer than the craft
        /// (ShipConstruction.ThumbnailIsOutdated). A tiny placeholder dated after the craft keeps it from rendering per edit.
        /// </summary>
        private static void KeepThumbnail(string craftPath)
        {
            string thumb = TempThumbPath;
            Directory.CreateDirectory(Path.GetDirectoryName(thumb));
            if (!File.Exists(thumb))
                File.WriteAllBytes(thumb, PlaceholderPng);
            DateTime craftTime = File.GetLastWriteTimeUtc(craftPath);
            File.SetLastWriteTimeUtc(thumb, craftTime.AddMinutes(1));
        }

        /// <summary>1×1 transparent PNG.</summary>
        private static readonly byte[] PlaceholderPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

        // ---- settling ----

        /// <summary>
        /// True once the game's post-load work is done: <see cref="SettleFrames"/> frames since the load, every part has run
        /// its Start in the editor (Part.editorStarted; Part.started is only set in flight), and the stage manager's sort
        /// coroutine has finished. There is no public flag for the coroutine, so its private field is read (never written);
        /// when the field is missing (another game version) editing is refused rather than answering too early.
        /// </summary>
        public static bool IsSettled(int loadFrame)
        {
            if (Time.frameCount < loadFrame + SettleFrames)
                return false;
            EditorLogic logic = EditorLogic.fetch;
            if (logic == null || logic.ship == null)
                return false;
            if (logic.ship.parts.Any(p => p == null || !p.editorStarted))
                return false;
            return !SortRunning();
        }

        /// <summary>
        /// Refuses before anything changes when the settle check cannot work in this game version (called where an edit
        /// starts: Apply, SetRoot before MakeRoot, SetPartSettings), so an edit is never applied and left unfinished.
        /// </summary>
        private static void RequireSettleSignal()
        {
            if (SortRoutineField == null)
                throw BridgeException.NotReady("cannot tell when the stage manager has finished (StageManager.sortRoutine not found in this game version); editing is unavailable");
        }

        private static bool SortRunning()
        {
            RequireSettleSignal();
            StageManager manager = StageManager.Instance;
            return manager != null && SortRoutineField.GetValue(manager) != null;
        }

        // ---- pending edits ----

        /// <summary>One edit waiting for the editor to settle, then for its check (and maybe its rollback).</summary>
        internal sealed class PendingEdit
        {
            public int Id;
            public string What;
            public CfgNode Original;
            public bool OriginalEmpty;
            public EditorFacility Facility;
            public int LoadFrame;
            /// <summary>Why the loaded ship differs from what the edit expected, or null when it matches.</summary>
            public Func<ShipConstruct, string> Verify;
            /// <summary>The success answer, built once the ship has settled.</summary>
            public Func<ShipConstruct, object> Answer;
            /// <summary>Set when the check failed and the original is being reloaded.</summary>
            public string RollbackReason;
        }

        private sealed class FinishedEdit
        {
            public int Id;
            public object Result;
            public BridgeException Error;
        }

        /// <summary>
        /// Registers an applied edit as pending and returns the answer for the first call ({edit, pending}).
        /// <paramref name="loadFrame"/> is the frame of the reload (or of the in-place change for settings).
        /// </summary>
        public Dictionary<string, object> StartPending(string what, CfgNode original, bool originalEmpty, int loadFrame,
            Func<ShipConstruct, string> verify, Func<ShipConstruct, object> answer)
        {
            pending = new PendingEdit
            {
                Id = nextEditId++,
                What = what,
                Original = original,
                OriginalEmpty = originalEmpty,
                Facility = EditorDriver.editorFacility,
                LoadFrame = loadFrame,
                Verify = verify,
                Answer = answer,
            };
            return new Dictionary<string, object> { ["edit"] = (double)pending.Id, ["pending"] = true };
        }

        /// <summary>
        /// Called before any request that changes, saves or leaves the ship. With no pending edit it does nothing. Before the
        /// editor settles it refuses (not_ready). After that it finishes the pending edit; when that turns into a rollback the
        /// request is refused too, because the rollback does not finish inside this call.
        /// </summary>
        public void RequireNoPending()
        {
            if (pending == null)
                return;
            if (!IsSettled(pending.LoadFrame))
                throw BridgeException.NotReady("another edit (" + pending.What + ", edit " + pending.Id + ") is still in progress; retry shortly");
            string what = pending.What;
            if (Finish(out _) != Finished.Ended)
                throw BridgeException.NotReady("rolling back the previous edit (" + what + "); retry shortly");
        }

        /// <summary>How finishing a settled edit ended.</summary>
        private enum Finished
        {
            /// <summary>Success or failure reported; nothing more happens.</summary>
            Ended,
            /// <summary>The original is being reloaded; the edit stays pending until that settles.</summary>
            ReloadingOriginal,
            /// <summary>The failure is reported but an empty editor is being reopened (a scene load).</summary>
            ReopeningEmpty,
        }

        /// <summary>edit_result: pending until settled, then the final answer (kept so the same id answers the same).</summary>
        public object Result(int id)
        {
            if (last != null && last.Id == id)
                return Replay(last);
            if (pending == null || pending.Id != id)
                throw BridgeException.BadRequest("unknown edit " + id + "; the last finished edit is " + (last != null ? last.Id.ToString() : "none"));
            if (!IsSettled(pending.LoadFrame))
                return PendingAnswer(pending.Id);
            return Finish(out object answer) == Finished.ReloadingOriginal ? answer : Replay(last);
        }

        /// <summary>
        /// Finishes the settled pending edit: the answer (or failure) is kept in <see cref="last"/>, except while the
        /// original is reloaded, when <paramref name="answer"/> is the pending answer. Anything but Ended means a rollback
        /// is still running, so another request must not be handled in the same call.
        /// </summary>
        private Finished Finish(out object answer)
        {
            answer = null;
            PendingEdit edit = pending;
            ShipConstruct ship = EditorLogic.fetch.ship;
            if (edit.RollbackReason != null)
            {
                End(edit, null, BridgeException.BadRequest(edit.RollbackReason + "; the ship was restored to before the edit"));
                return Finished.Ended;
            }

            string mismatch;
            try
            {
                mismatch = edit.Verify(ship);
                if (mismatch == null)
                {
                    End(edit, edit.Answer(ship), null);
                    return Finished.Ended;
                }
            }
            catch (BridgeException e)
            {
                mismatch = e.Message;
            }

            string reason = "the game loaded a different ship than the edit expected (" + mismatch + ")";
            log("editor edit " + edit.Id + " rolled back: " + reason);
            if (edit.OriginalEmpty)
            {
                // A craft without parts cannot be loaded (StartEditor reads ship.parts[0]); reopen an empty editor instead.
                // The edit ends first, so the scene change it causes does not drop it.
                End(edit, null, BridgeException.BadRequest(reason + "; the editor was reopened empty (a scene load): poll get_status until ready=true"));
                StartClean(edit.Facility);
                return Finished.ReopeningEmpty;
            }
            edit.RollbackReason = reason;
            edit.LoadFrame = Reload(edit.Original);
            answer = PendingAnswer(edit.Id);
            return Finished.ReloadingOriginal;
        }

        private void End(PendingEdit edit, object result, BridgeException error)
        {
            last = new FinishedEdit { Id = edit.Id, Result = result, Error = error };
            pending = null;
        }

        private static object Replay(FinishedEdit edit)
        {
            if (edit.Error != null)
                throw edit.Error;
            return edit.Result;
        }

        private static Dictionary<string, object> PendingAnswer(int id)
            => new Dictionary<string, object> { ["edit"] = (double)id, ["pending"] = true };

        /// <summary>
        /// A scene change we did not ask for (the player left the editor) drops the pending edit; its id then answers
        /// not_ready saying so. Our own scene loads happen only after the pending edit has finished.
        /// </summary>
        public void DropPending(string why)
        {
            if (pending == null)
                return;
            log("editor edit " + pending.Id + " dropped: " + why);
            End(pending, null, BridgeException.NotReady("the edit (" + pending.What + ") was dropped: " + why + "; check the ship with editor_get_ship"));
        }

        /// <summary>Opens the editor of <paramref name="facility"/> with an empty ship (scene load).</summary>
        public static void StartClean(EditorFacility facility)
        {
            // StartEditor only sets the facility; the startup behaviour is a separate static that stays LOAD_FROM_CACHE
            // after returning from a flight, which would bring the last launched ship back.
            EditorDriver.StartupBehaviour = EditorDriver.StartupBehaviours.START_CLEAN;
            EditorDriver.StartEditor(facility);
        }

        // ---- editing ----

        /// <summary>
        /// Runs a structural edit: saves the original, applies <paramref name="op"/> to a copy, checks it, reloads it and
        /// registers the pending edit whose check compares the settled ship with the edited craft.
        /// </summary>
        /// <param name="original">What a rollback restores (default: the ship now).</param>
        /// <param name="source">What the edit starts from (default: the original).</param>
        /// <param name="before">Relations to compute "changed" against (default: the original's).</param>
        /// <param name="cleared">Parts whose symmetry the edit cleared, from the relations before and the settled ship.</param>
        private object Apply(ShipConstruct ship, string what, Func<CraftDoc, EditOutcome> op, CfgNode original = null, CfgNode source = null,
            Dictionary<uint, PartRelation> before = null, Func<Dictionary<uint, PartRelation>, ShipConstruct, IEnumerable<uint>> cleared = null)
        {
            RequireSettleSignal();
            original = original ?? SaveCurrent(ship);
            bool originalEmpty = original.GetNodes("PART").Count == 0;
            CraftDoc doc = new CraftDoc((source ?? original).Clone(), RulesOf, Staging.StagedMap(ship));
            before = before ?? new CraftDoc(original.Clone(), RulesOf, Staging.StagedMap(ship)).Relations();
            EditOutcome outcome = op(doc);
            doc.RequireIntegrity();
            Dictionary<uint, PartRelation> expected = doc.Relations();
            Dictionary<uint, int> expectedStages = doc.StageMap();
            bool dropped = HeldPartDropped(ship);
            int frame = Reload(doc.Craft);
            return StartPending(what, original, originalEmpty, frame,
                live => Mismatch(expected, expectedStages, outcome, live),
                live => ShipView.EditResult(live, CraftDoc.Changed(before, ShipView.Relations(live), outcome), outcome.Removed,
                    cleared != null ? cleared(before, live) : null, dropped));
        }

        /// <summary>How the settled ship differs from the edited craft, or null.</summary>
        private static string Mismatch(Dictionary<uint, PartRelation> expected, Dictionary<uint, int> expectedStages, EditOutcome outcome, ShipConstruct ship)
        {
            Dictionary<uint, PartRelation> live = ShipView.Relations(ship);
            if (live.Count != expected.Count)
                return live.Count + " parts instead of " + expected.Count;
            foreach (KeyValuePair<uint, PartRelation> pair in expected)
            {
                if (!live.TryGetValue(pair.Key, out PartRelation actual))
                    return "part " + pair.Key + " is missing";
                if (!actual.Equals(pair.Value))
                    return "part " + pair.Key + " is " + actual + " instead of " + pair.Value;
            }
            return Staging.StageMismatch(expectedStages, Staging.LiveStages(ship), outcome.Added, outcome.StageRequested);
        }

        private static PartRules RulesOf(string name)
        {
            AvailablePart info = PartLoader.getPartInfoByName(name);
            return info?.partPrefab != null ? RulesOf(info.partPrefab) : new PartRules();
        }

        private static PartRules RulesOf(Part prefab)
        {
            AttachRules rules = prefab.attachRules;
            return new PartRules
            {
                AllowRoot = rules.allowRoot,
                CanSurfaceAttach = rules.srfAttach && prefab.srfAttachNode != null,
                AcceptsSurface = rules.allowSrfAttach,
            };
        }

        /// <summary>The kind to add, for a buildable part name (bad_request otherwise).</summary>
        private static PartTemplate TemplateFor(string name)
        {
            AvailablePart info = PartCatalog.RequireBuildable(name);
            Part prefab = info.partPrefab;
            AttachNode srf = prefab.srfAttachNode;
            return new PartTemplate
            {
                Name = info.name,
                Node = NewPartNode(info),
                Rules = RulesOf(prefab),
                SurfaceNode = srf != null ? new NodeDef(srf.id, V(srf.position), V(srf.orientation)) : (NodeDef?)null,
            };
        }

        public object PlaceRoot(ShipConstruct ship, string part, double? stage)
        {
            RequireNoPending();
            PartTemplate template = TemplateFor(part);
            Vec3 at = EditorDriver.editorFacility == EditorFacility.SPH ? new Vec3(0, 5, 0) : new Vec3(0, 15, 0);
            return Apply(ship, "place_root " + part, doc => doc.PlaceRoot(template, at, stage));
        }

        public object AttachNode(ShipConstruct ship, uint target, string targetNode, string part, string node, double? stage)
        {
            RequireNoPending();
            PartTemplate template = TemplateFor(part);
            return Apply(ship, "attach_node " + part, doc => doc.AttachNode(target, targetNode, template, node, stage));
        }

        public object AttachSurface(ShipConstruct ship, uint target, string part, double height, double angle, int symmetry, double? stage)
        {
            RequireNoPending();
            PartTemplate template = TemplateFor(part);
            Part live = RequirePart(ship, target);
            bool grouped = live.symmetryCounterparts.Count > 0;
            List<double> angles = grouped || symmetry < 1 ? new List<double> { angle } : AttachMath.RadialAngles(angle, Math.Min(symmetry, 8));
            List<SurfaceHit> hits = angles.Select(a => Probe(live, height, a)).ToList();
            return Apply(ship, "attach_surface " + part, doc => doc.AttachSurface(target, template, hits, symmetry, stage));
        }

        public object Remove(ShipConstruct ship, uint id)
        {
            RequireNoPending();
            return Apply(ship, "remove " + id, doc => doc.Remove(id));
        }

        public object Replace(ShipConstruct ship, uint id, string part)
        {
            RequireNoPending();
            PartTemplate template = TemplateFor(part);
            return Apply(ship, "replace " + id + " with " + part, doc => doc.Replace(id, template));
        }

        public object Move(ShipConstruct ship, uint id, Vec3? offset, Vec3? rotate)
        {
            RequireNoPending();
            return Apply(ship, "move " + id, doc => doc.Move(id, offset, rotate));
        }

        public object SetStage(ShipConstruct ship, uint id, double stage)
        {
            RequireNoPending();
            return Apply(ship, "set_stage " + id, doc => doc.SetStage(id, stage));
        }

        public object SwapStages(ShipConstruct ship, double a, double b)
        {
            RequireNoPending();
            return Apply(ship, "swap_stages " + a + "/" + b, doc => doc.SwapStages(a, b));
        }

        /// <summary>
        /// Makes <paramref name="id"/> the root with the game's own re-root (EditorReRootUtil.MakeRoot: flips surface
        /// attachments, clears symmetry that no longer fits). It changes the live ship in place, so the original is saved
        /// first and reloaded on any failure after it; the re-rooted ship is then reloaded with the new root first.
        /// </summary>
        public object SetRoot(ShipConstruct ship, uint id)
        {
            RequireNoPending();
            Part part = RequirePart(ship, id);
            Part root = ship.parts.FirstOrDefault(p => p.parent == null);
            if (part == root)
                throw BridgeException.BadRequest("part " + id + " is already the root part");
            if (!part.attachRules.allowRoot)
                throw BridgeException.BadRequest("part " + id + " (" + part.partInfo?.name + ") cannot be the root part");
            RequireSettleSignal();
            CfgNode original = SaveCurrent(ship);
            Dictionary<uint, PartRelation> before = ShipView.Relations(ship);
            if (!EditorReRootUtil.MakeRoot(part, root))
                throw BridgeException.BadRequest("the game refused to make part " + id + " the root (it cannot be the root here)");
            try
            {
                CfgNode rerooted = SaveCurrent(ship);
                MoveFirst(rerooted, id);
                return Apply(ship, "set_root " + id, doc => new EditOutcome(), original, rerooted, before, SymmetryCleared);
            }
            catch (Exception e)
            {
                // MakeRoot already changed the live ship: reload the original and keep it pending like any rollback, so
                // nothing touches the ship before it settles; the failure is answered once it has.
                string reason = e is BridgeException b ? b.Message : e.GetType().Name + ": " + e.Message;
                log("editor set_root " + id + " failed after MakeRoot (" + reason + "); restoring the original");
                pending = new PendingEdit
                {
                    Id = nextEditId++,
                    What = "set_root " + id,
                    Original = original,
                    OriginalEmpty = false,
                    Facility = EditorDriver.editorFacility,
                    Verify = _ => null,
                    Answer = _ => null,
                    RollbackReason = "set_root " + id + " failed after the re-root (" + reason + ")",
                };
                pending.LoadFrame = Reload(original);
                return PendingAnswer(pending.Id);
            }
        }

        /// <summary>
        /// Part settings change the live parts (no reload). A staging toggle can make the game re-sort stages on a later
        /// frame, so the answer also waits for the editor to settle like a structural edit.
        /// </summary>
        public object SetPartSettings(ShipConstruct ship, uint id, List<object> resources, List<object> fields, bool symmetry)
        {
            RequireNoPending();
            Part part = RequirePart(ship, id);
            RequireSettleSignal();
            CfgNode original = SaveCurrent(ship);
            bool dropped = HeldPartDropped(ship);
            List<uint> changed = PartSettings.Apply(ship, part, resources, fields, symmetry).Select(p => p.craftID).OrderBy(x => x).ToList();
            return StartPending("set_part_settings " + id, original, false, Time.frameCount,
                live => null,
                live => ShipView.EditResult(live, changed, null, null, dropped));
        }

        public static object PartSettingsOf(ShipConstruct ship, uint id) => PartSettings.Describe(RequirePart(ship, id));

        /// <summary>Parts that had symmetry partners before and have different ones in the settled ship.</summary>
        private static IEnumerable<uint> SymmetryCleared(Dictionary<uint, PartRelation> before, ShipConstruct ship)
        {
            Dictionary<uint, PartRelation> now = ShipView.Relations(ship);
            return before
                .Where(b => b.Value.Symmetry.Count > 0 && now.TryGetValue(b.Key, out PartRelation n)
                            && !n.Symmetry.OrderBy(x => x).SequenceEqual(b.Value.Symmetry.OrderBy(x => x)))
                .Select(b => b.Key).OrderBy(x => x).ToList();
        }

        /// <summary>Puts the PART of <paramref name="id"/> before every other PART (the loader takes the first as root).</summary>
        private static void MoveFirst(CfgNode craft, uint id)
        {
            CfgNode first = craft.GetNodes("PART").First(p => CraftDoc.IdOf(p) == id);
            craft.RemoveNode(first);
            int at = craft.Nodes.ToList().FindIndex(n => n.Name == "PART");
            craft.InsertNode(at < 0 ? craft.Nodes.Count : at, first);
        }

        private static Part RequirePart(ShipConstruct ship, uint id)
            => ship.parts.FirstOrDefault(p => p.craftID == id)
               ?? throw BridgeException.BadRequest("no part with id " + id + " in the ship; see editor_get_ship");

        /// <summary>Hit of the target's colliders by a ray toward its axis at height/angle; bad_request when it misses.</summary>
        private static SurfaceHit Probe(Part target, double height, double angle)
        {
            // The editor turns part colliders into triggers; Collider.Raycast still hits them. Child parts sit under the
            // target in the hierarchy, so only colliders owned by the target itself count.
            List<Collider> colliders = target.GetComponentsInChildren<Collider>(false)
                .Where(c => c != null && c.enabled && c.gameObject.activeInHierarchy && c.GetComponentInParent<Part>() == target)
                .ToList();
            if (colliders.Count == 0)
                throw BridgeException.BadRequest("part " + target.craftID + " has no surface to attach to");
            Vector3 center = target.transform.position;
            float reach = 5f + 2f * colliders.Max(c => (c.bounds.center - center).magnitude + c.bounds.extents.magnitude);
            Pose pose = new Pose(V(target.transform.position), Q(target.transform.rotation));
            (Vec3 origin, Vec3 direction) = AttachMath.SurfaceProbe(pose, height, angle, reach);
            Ray ray = new Ray(U(origin), U(direction));
            RaycastHit best = default;
            bool found = false;
            foreach (Collider collider in colliders)
            {
                if (collider.Raycast(ray, out RaycastHit hit, reach * 2f) && (!found || hit.distance < best.distance))
                {
                    best = hit;
                    found = true;
                }
            }
            if (!found)
                throw BridgeException.BadRequest("no surface of part " + target.craftID + " at height " + height + " m, angle " + angle + "°");
            return new SurfaceHit(V(best.point), V(best.normal));
        }

        // ---- Unity ↔ pure types ----

        internal static Vec3 V(Vector3 v) => new Vec3(v.x, v.y, v.z);

        internal static Vector3 U(Vec3 v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        internal static Quat Q(Quaternion q) => new Quat(q.x, q.y, q.z, q.w);

        // ---- parts ----

        /// <summary>A part held by the mouse is not in the ship and is not saved, so a reload drops it.</summary>
        public static bool HeldPartDropped(ShipConstruct ship)
        {
            Part held = EditorLogic.SelectedPart;
            return held != null && !ship.parts.Contains(held);
        }

        /// <summary>
        /// PART node of a new part with the game's defaults, written straight from the loaded prefab (never instantiated or
        /// changed). Value lines follow ShipConstruct.SaveShip; MODULE and RESOURCE nodes are written by the prefab's own
        /// modules and resources, so their default state (variants, staging toggles, amounts) is what the game would place.
        /// Cloning the prefab into a temporary ShipConstruct and calling SaveShip throws in the editor (T006). EVENTS,
        /// ACTIONS and PARTDATA are left out: the loader keeps the prefab state without them. Position, links, stage and the
        /// craftID are set afterwards by CraftDoc; persistentId 0 is renumbered by the loader.
        /// </summary>
        public static CfgNode NewPartNode(AvailablePart info)
        {
            Part prefab = info.partPrefab;
            CfgNode node = new CfgNode("PART");
            node.AddValue("part", info.name + "_0");
            node.AddValue("partName", prefab.partName);
            node.AddValue("persistentId", "0");
            node.AddValue("pos", "0,0,0");
            node.AddValue("attPos", "0,0,0");
            node.AddValue("attPos0", "0,0,0");
            node.AddValue("rot", "0,0,0,1");
            node.AddValue("attRot", "0,0,0,1");
            node.AddValue("attRot0", "0,0,0,1");
            node.AddValue("mir", "1,1,1");
            node.AddValue("symMethod", SymmetryMethod.Radial.ToString());
            node.AddValue("autostrutMode", prefab.autoStrutMode.ToString());
            node.AddValue("rigidAttachment", prefab.rigidAttachment.ToString());
            node.AddValue("istg", "-1");
            node.AddValue("resPri", "0");
            node.AddValue("dstg", "0");
            node.AddValue("sidx", "-1");
            node.AddValue("sqor", "-1");
            node.AddValue("sepI", "-1");
            node.AddValue("attm", "0");
            node.AddValue("sameVesselCollision", "False");
            node.AddValue("modCost", "0");
            node.AddValue("modMass", "0");
            node.AddValue("modSize", "0,0,0");
            foreach (AttachNode attach in prefab.attachNodes)
                node.AddValue("attN", attach.id + ",Null_0_" + KSPUtil.WriteVector(attach.originalPosition, "|") + "_"
                    + KSPUtil.WriteVector(attach.originalOrientation, "|") + "_" + KSPUtil.WriteVector(attach.position, "|") + "_"
                    + KSPUtil.WriteVector(attach.orientation, "|"));
            foreach (PartModule module in prefab.Modules)
            {
                ConfigNode saved = new ConfigNode("MODULE");
                module.Save(saved);
                node.AddNode(ToCfg(saved));
            }
            foreach (PartResource resource in prefab.Resources)
            {
                ConfigNode saved = new ConfigNode("RESOURCE");
                resource.Save(saved);
                node.AddNode(ToCfg(saved));
            }
            return node;
        }
    }
}
