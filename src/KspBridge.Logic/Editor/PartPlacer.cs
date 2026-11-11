using KspBridge.Catalog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace KspBridge.Editor
{
    /// <summary>add_part request: stack attach (node) or surface attach (surface, optional symmetry).</summary>
    public sealed class AddRequest
    {
        public string Part;
        public uint? Parent;
        public string ParentNode;
        public string ChildNode;
        public SurfaceSpec Surface;
        public SymmetrySpec Symmetry;
    }

    public sealed class SurfaceSpec
    {
        /// <summary>Degrees around the parent +Y axis, 0 = parent +Z, 90 = parent +X.</summary>
        public float Azimuth;
        /// <summary>0..1 from the bottom to the top of the parent's colliders.</summary>
        public float Height;
        /// <summary>Degrees about the surface normal.</summary>
        public float Angle;
    }

    public sealed class SymmetrySpec
    {
        public SymmetryMethod Mode;
        public int Count;
    }

    /// <summary>Adds, removes and moves editor parts. Every public call changes nothing when it fails.</summary>
    public static class PartPlacer
    {
        private static readonly TimeSpan MainThreadTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan PartStartTimeout = TimeSpan.FromSeconds(5);

        private static string TempDir => KSPUtil.ApplicationRootPath + "GameData/KspBridge/PluginData/tmp";

        /// <summary>Spawned parts waiting for Part.Start before they are attached. Poses are kept relative to the parent.</summary>
        private sealed class Pending
        {
            public readonly List<Part> Children = new List<Part>();
            public readonly List<Vector3> LocalPositions = new List<Vector3>();
            public readonly List<Quaternion> LocalRotations = new List<Quaternion>();
            public int SpawnFrame;
            public uint ParentId;
            public bool Surface;
            public string ParentNode;
            public string ChildNode;
            public string SurfaceMeshName;
            public SymmetryMethod SymmetryMode;

            public void Add(Part parent, Part child)
            {
                Children.Add(child);
                LocalPositions.Add(parent.transform.InverseTransformPoint(child.transform.position));
                LocalRotations.Add(Quaternion.Inverse(parent.transform.rotation) * child.transform.rotation);
            }
        }

        /// <summary>
        /// HTTP thread. A part's staging icon is created by Part.Start (stackIcon.CreateIcon, with no yield before it in the
        /// editor), and StageManager only stages icons that exist when the part is picked and placed. Unity runs Start for an
        /// object created during Update on the next frame, before any Update. So an attach spawns the parts, waits until the
        /// frame count has moved on, then attaches them on the main thread. (Part.started is only set in flight, not in the editor.)
        /// </summary>
        public static object Add(MainThreadDispatcher dispatcher, AddRequest r)
        {
            object begun = dispatcher.Invoke(() => Begin(r), MainThreadTimeout);
            if (!(begun is Pending pending))
            {
                return begun;
            }

            DateTime deadline = DateTime.UtcNow + PartStartTimeout;
            while (!(bool)dispatcher.Invoke(() => Time.frameCount > pending.SpawnFrame, MainThreadTimeout))
            {
                if (DateTime.UtcNow > deadline)
                {
                    dispatcher.Invoke(() => { DiscardAll(EditorLogic.fetch?.ship, pending.Children); return null; }, MainThreadTimeout);
                    throw new ApiException(504, "part_start_timeout", pending.Children[0].partInfo.name + " did not finish starting within " + PartStartTimeout.TotalSeconds + "s");
                }
                System.Threading.Thread.Sleep(20);
            }
            return dispatcher.Invoke(() => Finish(pending), MainThreadTimeout);
        }

        /// <summary>Main thread. Checks the request; places the root at once, or spawns the parts to attach and returns them pending.</summary>
        private static object Begin(AddRequest r)
        {
            ShipConstruct ship = EditorScene.Require();
            AvailablePart ap = PartCatalog.RequireUsable(r.Part);
            if (ship.parts.Count == 0)
            {
                if (r.Parent.HasValue)
                {
                    throw ApiException.BadRequest("the craft is empty: omit parent to place the root part");
                }
                return AddRoot(ap);
            }
            if (!r.Parent.HasValue)
            {
                throw ApiException.BadRequest("parent is required: the craft already has a root part");
            }
            Part parent = PartIndex.Find(ship, r.Parent.Value);
            return r.Surface != null ? BeginSurface(parent, ap, r.Surface, r.Symmetry) : BeginStack(parent, ap, r);
        }

        private static Pending BeginStack(Part parent, AvailablePart ap, AddRequest r)
        {
            // Check everything before creating anything.
            AttachNode pn = FreeNode(parent, r.ParentNode);
            if (!ap.partPrefab.attachRules.stack)
            {
                throw AttachFailed(ap.name + " cannot be stack-attached");
            }
            string childNodeId = r.ChildNode ?? DefaultChildNode(pn.id, ap.partPrefab.attachNodes);
            if (childNodeId == null || ap.partPrefab.attachNodes.All(n => n.id != childNodeId))
            {
                throw AttachFailed(ap.name + " has no node '" + (childNodeId ?? "?") + "' (nodes: " + NodeIds(ap.partPrefab.attachNodes) + ")");
            }

            Part child = Spawn(ap);
            PlaceAtNode(parent, pn, child, child.FindAttachNode(childNodeId));
            Pending p = new Pending { SpawnFrame = Time.frameCount, ParentId = parent.persistentId, ParentNode = pn.id, ChildNode = childNodeId };
            p.Add(parent, child);
            return p;
        }

        private static Pending BeginSurface(Part parent, AvailablePart ap, SurfaceSpec s, SymmetrySpec sym)
        {
            // Check everything before creating anything.
            if (!parent.attachRules.allowSrfAttach)
            {
                throw AttachFailed(parent.partInfo.name + " does not accept surface-attached parts");
            }
            if (!ap.partPrefab.attachRules.srfAttach)
            {
                throw AttachFailed(ap.name + " cannot be surface-attached");
            }
            if (!SurfaceHit(parent, s.Azimuth, s.Height, out RaycastHit hit))
            {
                Collider[] all = parent.GetComponentsInChildren<Collider>(true);
                Collider[] own = all.Where(c => c.GetComponentInParent<Part>() == parent).ToArray();
                throw AttachFailed("no surface of " + parent.partInfo.name + " at azimuth " + s.Azimuth + ", height " + s.Height
                    + " (colliders: " + all.Length + " in hierarchy, " + own.Length + " own, "
                    + own.Count(c => c.enabled && c.gameObject.activeInHierarchy) + " own usable; own: "
                    + string.Join(", ", own.Select(c => c.GetType().Name + " " + c.name + " active=" + c.gameObject.activeInHierarchy + " enabled=" + c.enabled + " trigger=" + c.isTrigger + " layer=" + c.gameObject.layer).ToArray()) + ")");
            }
            int count = sym?.Count ?? 1;

            Pending p = new Pending
            {
                SpawnFrame = Time.frameCount,
                ParentId = parent.persistentId,
                Surface = true,
                SurfaceMeshName = hit.collider.name,
                SymmetryMode = sym?.Mode ?? SymmetryMethod.Radial,
            };
            try
            {
                Part first = Spawn(ap);
                PlaceOnSurface(parent, first, hit, s.Angle);
                p.Add(parent, first);
                for (int k = 1; k < count; k++)
                {
                    Part copy = Spawn(ap);
                    if (p.SymmetryMode == SymmetryMethod.Mirror)
                    {
                        PlaceMirrored(parent, first, copy);
                    }
                    else
                    {
                        PlaceRadial(parent, first, copy, k, count);
                    }
                    p.Add(parent, copy);
                }
            }
            catch
            {
                DiscardAll(null, p.Children);
                throw;
            }
            return p;
        }

        /// <summary>Main thread, after Part.Start. Re-checks the parent (the craft may have changed meanwhile) and attaches.</summary>
        private static object Finish(Pending p)
        {
            ShipConstruct ship = null;
            try
            {
                ship = EditorScene.Require();
                foreach (Part c in p.Children)
                {
                    if (c.hasStagingIcon && c.stackIcon.StageIcon == null)
                    {
                        throw new ApiException(500, "staging_icon_missing", c.partInfo.name + " has no staging icon after Part.Start");
                    }
                }
                Part parent = PartIndex.Find(ship, p.ParentId);
                AttachNode pn = p.Surface ? null : FreeNode(parent, p.ParentNode);
                for (int i = 0; i < p.Children.Count; i++)
                {
                    p.Children[i].transform.SetPositionAndRotation(
                        parent.transform.TransformPoint(p.LocalPositions[i]), parent.transform.rotation * p.LocalRotations[i]);
                }
                if (p.Children.Count > 1)
                {
                    foreach (Part c in p.Children)
                    {
                        c.symMethod = p.SymmetryMode;
                        c.symmetryCounterparts = p.Children.Where(o => o != c).ToList();
                    }
                }

                // Editor order: picked -> attachPart (each symmetry part) -> placed. Placing after all are linked lets
                // StageManager group the held icons of symmetry counterparts.
                foreach (Part c in p.Children)
                {
                    GameEvents.onEditorPartPicked.Fire(c);
                }
                foreach (Part c in p.Children)
                {
                    if (p.Surface)
                    {
                        c.attachMode = AttachModes.SRF_ATTACH;
                        c.srfAttachNode.attachedPart = parent;
                        c.srfAttachNode.srfAttachMeshName = p.SurfaceMeshName;
                    }
                    else
                    {
                        AttachNode cn = c.FindAttachNode(p.ChildNode);
                        c.attachMode = AttachModes.STACK;
                        pn.attachedPart = c;
                        cn.attachedPart = parent;
                    }
                    Link(ship, parent, c);
                }
                foreach (Part c in p.Children)
                {
                    GameEvents.onEditorPartPlaced.Fire(c);
                }
            }
            catch
            {
                DiscardAll(ship, p.Children);
                throw;
            }
            EditorScene.Commit(ship);
            if (p.Children.Any(c => c.CrewCapacity > 0))
            {
                EditorScene.RefreshCrew();
            }
            return EditResult.Added(ship, p.Children);
        }

        /// <summary>
        /// Main thread. Removes a part with its symmetry counterparts and everything attached below them. The root cannot be removed.
        /// The editor's delete works on the part held by the mouse (its symmetry step too), so its steps are replayed here for the
        /// given part: pick (detachPart, then each counterpart still in the craft: PartSymmetryDeleted, detachPart, deletePart,
        /// then wipeSymmetry, onEditorPartPicked, PartDetached) and DestroySelectedPart (PartDeleted, deletePart,
        /// onEditorPartDeleted with the cleared selection), then one Undo step and the crew refresh.
        /// If a part module throws midway, parts already taken out of the craft are still destroyed, and the editor refresh and
        /// Undo step still run; the module's exception is the one reported.
        /// </summary>
        public static object Remove(uint id)
        {
            ShipConstruct ship = EditorScene.Require();
            Part p = PartIndex.Find(ship, id);
            if (p.parent == null)
            {
                throw ApiException.BadRequest("cannot remove the root part; use new_craft for an empty craft");
            }
            List<Part> group = SymmetryGroup(p).Where(s => ship.parts.Contains(s)).ToList();
            List<uint> removed = group.SelectMany(Subtree).Select(x => x.persistentId).Distinct().ToList();
            HashSet<Part> deleted = new HashSet<Part>();
            Exception failure = null;

            try
            {
                Detach(ship, p);
                foreach (Part s in group.Skip(1))
                {
                    GameEvents.onEditorPartEvent.Fire(ConstructionEventType.PartSymmetryDeleted, s);
                    Detach(ship, s);
                    Delete(s, deleted);
                }
                WipeSymmetry(p);
                GameEvents.onEditorPartPicked.Fire(p);
                GameEvents.onEditorPartEvent.Fire(ConstructionEventType.PartDetached, p);
                GameEvents.onEditorPartEvent.Fire(ConstructionEventType.PartDeleted, p);
                Delete(p, deleted);
            }
            catch (Exception e)
            {
                failure = e;
                // Parts already out of the craft must not stay in the scene.
                foreach (Part s in group)
                {
                    if (s != null && !ship.parts.Contains(s))
                    {
                        try
                        {
                            Delete(s, deleted);
                        }
                        catch (Exception cleanup)
                        {
                            Debug.LogError("[KspBridge] remove_part cleanup of " + s.partInfo.name + " failed: " + cleanup);
                        }
                    }
                }
                throw;
            }
            finally
            {
                try
                {
                    GameEvents.onEditorPartDeleted.Fire(null);
                    EditorScene.Commit(ship);
                }
                catch (Exception cleanup) when (failure != null)
                {
                    Debug.LogError("[KspBridge] remove_part refresh after " + failure.GetType().Name + " failed: " + cleanup);
                }
                EditorScene.RefreshCrew();
            }
            return EditResult.Removed(ship, removed);
        }

        /// <summary>
        /// EditorLogic.deletePart: the part and its children, children first; parts already deleted are skipped.
        /// (EditorLogic.DeletePart itself needs a part held by the mouse: it refreshes node icons for EditorLogic.SelectedPart
        /// and throws when there is none.)
        /// </summary>
        private static void Delete(Part part, HashSet<Part> deleted)
        {
            List<Part> doomed = Subtree(part).Where(x => !deleted.Contains(x)).ToList();
            for (int i = doomed.Count - 1; i >= 0; i--)
            {
                deleted.Add(doomed[i]);
                doomed[i].OnDelete();
                UnityEngine.Object.Destroy(doomed[i].gameObject);
            }
        }

        /// <summary>EditorLogic.wipeSymmetry: the part loses its counterparts; its children keep only counterparts inside its subtree.</summary>
        private static void WipeSymmetry(Part part)
        {
            part.symmetryCounterparts = new List<Part>();
            List<Part> children = EditorLogicBase.FindPartsInChildren(part);
            HashSet<Part> inside = new HashSet<Part>(children);
            foreach (Part c in children)
            {
                c.symmetryCounterparts.RemoveAll(x => !inside.Contains(x));
            }
        }

        /// <summary>
        /// Main thread. Moves a part and its symmetry counterparts (with everything attached below them) by a root-space distance.
        /// Like the editor offset tool on the reference transform, plus attPos; counterparts move by the mirrored or turned distance.
        /// Moving the root moves the whole craft.
        /// </summary>
        public static object Offset(uint id, Vector3 deltaRoot)
        {
            ShipConstruct ship = EditorScene.Require();
            Part p = PartIndex.Find(ship, id);
            Transform root = PartIndex.Root(ship).transform;
            Vector3 world = root.TransformVector(deltaRoot);
            List<Part> group = SymmetryGroup(p);
            foreach (Part s in group)
            {
                Transform t = s.GetReferenceTransform();
                t.position += SymmetricVector(p, s, world, root);
                s.attPos = t.localPosition - s.attPos0;
            }
            GameEvents.onEditorPartEvent.Fire(ConstructionEventType.PartOffset, p);
            EditorScene.Commit(ship);
            return EditResult.Changed(ship, group);
        }

        /// <summary>
        /// Main thread. Turns a part and its symmetry counterparts (with everything attached below them) about a root-space axis
        /// through the node that attaches it to its parent (the part origin for the root), like the editor rotate tool, including
        /// its attRotation update. Mirror counterparts turn about the mirrored axis the other way; radial ones about the turned axis.
        /// </summary>
        public static object Rotate(uint id, Vector3 axisRoot, float angleDeg)
        {
            ShipConstruct ship = EditorScene.Require();
            Part p = PartIndex.Find(ship, id);
            Transform root = PartIndex.Root(ship).transform;
            Vector3 axis = root.TransformDirection(axisRoot);
            List<Part> group = SymmetryGroup(p);
            // Each counterpart's turn, worked out before p itself is turned.
            List<Quaternion> turns = group.Select(s => Quaternion.AngleAxis(
                s != p && p.symMethod == SymmetryMethod.Mirror ? -angleDeg : angleDeg, SymmetricVector(p, s, axis, root))).ToList();
            for (int k = 0; k < group.Count; k++)
            {
                Part s = group[k];
                Quaternion q = turns[k];
                Transform t = s.GetReferenceTransform();
                AttachNode toParent = s.parent != null ? s.FindAttachNodeByPart(s.parent) : null;
                Vector3 pivot = toParent != null ? t.TransformPoint(toParent.position) : t.position;
                Quaternion attRotation = s.attRotation;
                Quaternion attRotation0 = s.attRotation0;
                t.rotation = q * t.rotation;
                t.position = pivot + q * (t.position - pivot);
                s.attRotation0 = s.transform.localRotation;
                s.attRotation = attRotation * Quaternion.Inverse(attRotation0) * s.transform.localRotation;
            }
            GameEvents.onEditorPartEvent.Fire(ConstructionEventType.PartRotated, p);
            EditorScene.Commit(ship);
            return EditResult.Changed(ship, group);
        }

        /// <summary>The part followed by its symmetry counterparts.</summary>
        internal static List<Part> SymmetryGroup(Part p) =>
            new[] { p }.Concat(p.symmetryCounterparts ?? new List<Part>()).Where(s => s != null).Distinct().ToList();

        private static IEnumerable<Part> Subtree(Part p) => new[] { p }.Concat(EditorLogicBase.FindPartsInChildren(p)).Distinct();

        /// <summary>
        /// A world vector of part p carried over to counterpart s: mirror counterparts reflect it through the root part's
        /// up/forward plane (as EditorLogic.UpdateSymmetry places them), radial ones turn it as s is turned from p.
        /// </summary>
        private static Vector3 SymmetricVector(Part p, Part s, Vector3 v, Transform root)
        {
            if (s == p)
            {
                return v;
            }
            if (p.symMethod == SymmetryMethod.Mirror)
            {
                return v - 2f * Vector3.Dot(v, root.right) * root.right;
            }
            return s.transform.rotation * Quaternion.Inverse(p.transform.rotation) * v;
        }

        /// <summary>EditorLogic.detachPart: out of the craft with its children, node links to the parent cleared, unparented.</summary>
        private static void Detach(ShipConstruct ship, Part part)
        {
            foreach (Part x in Subtree(part).ToList())
            {
                ship.Remove(x);
            }
            Part parent = part.parent;
            if (parent != null)
            {
                ClearNodes(parent, part);
                ClearNodes(part, parent);
            }
            part.setParent();
            part.transform.parent = null;
            part.onDetach();
        }

        /// <summary>EditorLogicBase.clearAttachNodes, one direction: the nodes of owner that point at other.</summary>
        private static void ClearNodes(Part owner, Part other)
        {
            foreach (AttachNode n in owner.attachNodes)
            {
                if (n.attachedPart == other)
                {
                    n.attachedPart = null;
                }
            }
            if (owner.srfAttachNode != null && owner.srfAttachNode.attachedPart == other)
            {
                owner.srfAttachNode.attachedPart = null;
            }
        }

        private static AttachNode FreeNode(Part parent, string nodeId)
        {
            if (nodeId == null)
            {
                throw ApiException.BadRequest("give node {parent} for a stack attach or surface {azimuth, height} for a surface attach");
            }
            AttachNode pn = parent.FindAttachNode(nodeId)
                ?? throw AttachFailed(parent.partInfo.name + " has no node '" + nodeId + "' (nodes: " + NodeIds(parent.attachNodes) + ")");
            if (pn.attachedPart != null)
            {
                throw AttachFailed("node '" + pn.id + "' of " + parent.partInfo.name + " is already used by part " + pn.attachedPart.persistentId);
            }
            return pn;
        }

        /// <summary>
        /// The root of an empty craft. EditorLogic.RootPart is read-only and an empty editor sits in its pod-select FSM state,
        /// so the root goes in through the game's own craft loading: a one-part craft file loaded with LoadShipFromFile.
        /// </summary>
        private static object AddRoot(AvailablePart ap)
        {
            string shipName = EditorLogic.fetch.ship.shipName;
            Directory.CreateDirectory(TempDir);
            string path = Path.Combine(TempDir, "root-" + Guid.NewGuid().ToString("N") + ".craft");
            Part p = Spawn(ap);
            // Where the editor puts a first part dropped into an empty craft; the craft file keeps this placement.
            p.transform.SetPositionAndRotation(EditorLogic.fetch.initialPodPosition, EditorLogic.VesselRotation);
            try
            {
                ShipConstruct one = new ShipConstruct(shipName, "", p) { shipFacility = EditorDriver.editorFacility };
                one.SaveShip().Save(path);
            }
            finally
            {
                UnityEngine.Object.Destroy(p.gameObject);
            }

            EditorLogic.LoadShipFromFile(path);
            ShipConstruct ship = EditorLogic.fetch.ship;
            if (ship.parts.Count != 1)
            {
                // Left in place for diagnosis: the load did not finish within this call.
                throw new ApiException(500, "root_load_pending", "the root part did not load synchronously (parts: " + ship.parts.Count + ", file " + path + ")");
            }
            File.Delete(path);
            EditorScene.SetName(shipName);
            return EditResult.Added(ship, ship.parts);
        }

        private static Part Spawn(AvailablePart ap)
        {
            Part p = UnityEngine.Object.Instantiate(ap.partPrefab);
            p.gameObject.SetActive(true);
            p.name = ap.name;
            p.partInfo = ap;
            p.persistentId = FlightGlobals.CheckPartpersistentId(p.persistentId, p, false, true, 0);
            return p;
        }

        /// <summary>Turns the child so its node faces the parent node and puts the two nodes together.</summary>
        private static void PlaceAtNode(Part parent, AttachNode pn, Part child, AttachNode cn)
        {
            Quaternion rot = parent.transform.rotation * Quaternion.FromToRotation(cn.orientation, -pn.orientation);
            Vector3 pos = parent.transform.TransformPoint(pn.position) - rot * cn.position;
            child.transform.SetPositionAndRotation(pos, rot);
        }

        /// <summary>
        /// The parent's collider surface at an azimuth (around the parent's +Y through its colliders' centre, 0 = +Z) and a height
        /// (0..1 of the colliders' extent): a ray from outside toward that line, tested against the parent's own colliders only.
        /// </summary>
        private static bool SurfaceHit(Part parent, float azimuthDeg, float height01, out RaycastHit best)
        {
            best = default;
            Transform t = parent.transform;
            // Attached parts are transform children of their parent, so keep only the colliders of this part itself.
            // In the editor part colliders are triggers; Collider.Raycast still hits them.
            List<Collider> colliders = parent.GetComponentsInChildren<Collider>()
                .Where(c => c.enabled && c.gameObject.activeInHierarchy && c.GetComponentInParent<Part>() == parent).ToList();
            if (colliders.Count == 0)
            {
                return false;
            }

            Bounds local = new Bounds(t.InverseTransformPoint(colliders[0].bounds.center), Vector3.zero);
            foreach (Collider c in colliders)
            {
                Bounds w = c.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = new Vector3((i & 1) == 0 ? w.min.x : w.max.x, (i & 2) == 0 ? w.min.y : w.max.y, (i & 4) == 0 ? w.min.z : w.max.z);
                    local.Encapsulate(t.InverseTransformPoint(corner));
                }
            }

            float y = Mathf.Lerp(local.min.y, local.max.y, height01);
            Vector3 dir = new Vector3(Mathf.Sin(azimuthDeg * Mathf.Deg2Rad), 0f, Mathf.Cos(azimuthDeg * Mathf.Deg2Rad));
            float reach = local.extents.magnitude * 2f + 1f;
            // Aim at the vertical line through the colliders' centre (not the part origin, which may sit off the body).
            Ray ray = new Ray(t.TransformPoint(new Vector3(local.center.x, y, local.center.z) + dir * reach), -t.TransformDirection(dir));
            float nearest = float.MaxValue;
            foreach (Collider c in colliders)
            {
                if (c.Raycast(ray, out RaycastHit hit, reach * 2f) && hit.distance < nearest)
                {
                    nearest = hit.distance;
                    best = hit;
                }
            }
            return nearest < float.MaxValue;
        }

        /// <summary>EditorLogic.checkAttach surface placement, plus a turn of angle degrees about the surface normal.</summary>
        private static void PlaceOnSurface(Part parent, Part child, RaycastHit hit, float angleDeg)
        {
            AttachNode sn = child.srfAttachNode;
            Quaternion rot = Quaternion.AngleAxis(angleDeg, hit.normal)
                * Quaternion.LookRotation(hit.normal, parent.transform.rotation * Vector3.up)
                * Quaternion.LookRotation(sn.orientation, Vector3.up);
            Vector3 pos = hit.point - rot * child.attRotation * sn.position;
            child.transform.SetPositionAndRotation(pos, rot);
        }

        /// <summary>EditorLogic.UpdateSymmetry radial copy k of count: the first part turned about the parent's up axis.</summary>
        private static void PlaceRadial(Part parent, Part first, Part copy, int k, int count)
        {
            Quaternion q = Quaternion.AngleAxis(k * 360f / count, parent.transform.up);
            copy.transform.SetPositionAndRotation(
                q * (first.transform.position - parent.transform.position) + parent.transform.position, q * first.transform.rotation);
        }

        /// <summary>
        /// EditorLogic.UpdateSymmetry mirror copy: reflected through the plane of the root part's up and forward axes,
        /// with the part modules' OnWillBeMirrored hook and the editor's 180 degree turn for sideways surface nodes.
        /// </summary>
        private static void PlaceMirrored(Part parent, Part first, Part copy)
        {
            Transform root = EditorLogic.RootPart.transform;
            Transform reference = first.GetReferenceTransform();
            Vector3 offset = reference.position - parent.transform.position;
            Vector3 flat = Vector3.ProjectOnPlane(offset, root.up);
            Vector3 position = parent.transform.position + (offset - flat) + Quaternion.AngleAxis(180f, -root.forward) * flat;

            Vector3 up = root.InverseTransformDirection(reference.up);
            Vector3 forward = root.InverseTransformDirection(reference.forward);
            up.x *= -1f;
            forward.x *= -1f;
            up = root.TransformDirection(up);
            forward = root.TransformDirection(forward);
            Quaternion rotation = Quaternion.LookRotation(forward, up);
            bool handled = copy.OnWillBeMirrored(ref rotation, copy.srfAttachNode, parent);
            if (!handled && copy.srfAttachNode.orientation.x != 0f)
            {
                rotation = Quaternion.AngleAxis(180f, up) * rotation;
            }
            copy.transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>What EditorLogic.attachPart does after the nodes are set: attached flag, ship membership, part and transform parents.</summary>
        private static void Link(ShipConstruct ship, Part parent, Part child)
        {
            child.isAttached = true;
            ship.Add(child);
            child.setParent(parent);
            child.transform.parent = parent.transform;
            child.onAttach(parent, true);
            GameEvents.onEditorPartEvent.Fire(ConstructionEventType.PartAttached, child);
        }

        /// <summary>Undoes a half-made placement: parts are removed from the craft if they got that far, then destroyed.</summary>
        private static void DiscardAll(ShipConstruct ship, IEnumerable<Part> parts)
        {
            foreach (Part p in parts.ToList())
            {
                if (p == null)
                {
                    continue;
                }
                if (ship != null && ship.parts.Contains(p))
                {
                    EditorLogic.DeletePart(p);
                }
                else
                {
                    UnityEngine.Object.Destroy(p.gameObject);
                }
            }
        }

        private static string DefaultChildNode(string parentNodeId, List<AttachNode> childNodes)
        {
            if (parentNodeId == "bottom" && childNodes.Any(n => n.id == "top"))
            {
                return "top";
            }
            if (parentNodeId == "top" && childNodes.Any(n => n.id == "bottom"))
            {
                return "bottom";
            }
            return childNodes.FirstOrDefault()?.id;
        }

        private static string NodeIds(IEnumerable<AttachNode> nodes) => string.Join(", ", nodes.Select(n => n.id).ToArray());

        private static ApiException AttachFailed(string message) => new ApiException(409, "attach_failed", message);
    }
}
