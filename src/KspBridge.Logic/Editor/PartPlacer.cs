using KspBridge.Catalog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace KspBridge.Editor
{
    /// <summary>add_part request. Node is the parent node id plus optional child node id.</summary>
    public sealed class AddRequest
    {
        public string Part;
        public uint? Parent;
        public string ParentNode;
        public string ChildNode;
    }

    /// <summary>Adds, removes and moves editor parts. Every public call changes nothing when it fails.</summary>
    public static class PartPlacer
    {
        private static readonly TimeSpan MainThreadTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan PartStartTimeout = TimeSpan.FromSeconds(5);

        private static string TempDir => KSPUtil.ApplicationRootPath + "GameData/KspBridge/PluginData/tmp";

        /// <summary>A spawned part waiting for Part.Start before it is attached.</summary>
        private sealed class PendingStack
        {
            public Part Child;
            public int SpawnFrame;
            public uint ParentId;
            public string ParentNode;
            public string ChildNode;
        }

        /// <summary>
        /// HTTP thread. A part's staging icon is created by Part.Start (stackIcon.CreateIcon, with no yield before it in the
        /// editor), and StageManager only stages icons that exist when the part is picked and placed. Unity runs Start for an
        /// object created during Update on the next frame, before any Update. So a stack attach spawns the part, waits until the
        /// frame count has moved on, then attaches it on the main thread. (Part.started is only set in flight, not in the editor.)
        /// </summary>
        public static object Add(MainThreadDispatcher dispatcher, AddRequest r)
        {
            object begun = dispatcher.Invoke(() => Begin(r), MainThreadTimeout);
            if (!(begun is PendingStack pending))
            {
                return begun;
            }

            DateTime deadline = DateTime.UtcNow + PartStartTimeout;
            while (!(bool)dispatcher.Invoke(() => Time.frameCount > pending.SpawnFrame, MainThreadTimeout))
            {
                if (DateTime.UtcNow > deadline)
                {
                    dispatcher.Invoke(() => { Discard(EditorLogic.fetch?.ship, pending.Child); return null; }, MainThreadTimeout);
                    throw new ApiException(504, "part_start_timeout", pending.Child.partInfo.name + " did not finish starting within " + PartStartTimeout.TotalSeconds + "s");
                }
                System.Threading.Thread.Sleep(20);
            }
            return dispatcher.Invoke(() => Finish(pending), MainThreadTimeout);
        }

        /// <summary>Main thread. Checks the request; places the root at once, or spawns a stack child and returns it pending.</summary>
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
            if (r.ParentNode == null)
            {
                throw ApiException.BadRequest("node.parent is required");
            }

            // Check everything before creating anything.
            Part parent = PartIndex.Find(ship, r.Parent.Value);
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
            return new PendingStack { Child = child, SpawnFrame = Time.frameCount, ParentId = parent.persistentId, ParentNode = pn.id, ChildNode = childNodeId };
        }

        /// <summary>Main thread, after Part.Start. Re-checks the parent node (the craft may have changed meanwhile) and attaches.</summary>
        private static object Finish(PendingStack p)
        {
            ShipConstruct ship = null;
            try
            {
                ship = EditorScene.Require();
                if (p.Child.hasStagingIcon && p.Child.stackIcon.StageIcon == null)
                {
                    throw new ApiException(500, "staging_icon_missing", p.Child.partInfo.name + " has no staging icon after Part.Start");
                }
                Part parent = PartIndex.Find(ship, p.ParentId);
                AttachNode pn = FreeNode(parent, p.ParentNode);
                AttachStack(ship, parent, pn, p.Child, p.Child.FindAttachNode(p.ChildNode));
            }
            catch
            {
                Discard(ship, p.Child);
                throw;
            }
            EditorScene.Commit(ship);
            return EditResult.Added(ship, new[] { p.Child });
        }

        private static AttachNode FreeNode(Part parent, string nodeId)
        {
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

        /// <summary>
        /// Turns the child so its node faces the parent node, puts the two nodes together and links them in the editor's order
        /// (EditorLogic: part picked -> attachPart -> part placed). StageManager holds a part's staging icons when it is picked
        /// and puts the held icons into stages when it is placed, so both events are needed for staged parts.
        /// </summary>
        private static void AttachStack(ShipConstruct ship, Part parent, AttachNode pn, Part child, AttachNode cn)
        {
            GameEvents.onEditorPartPicked.Fire(child);
            PlaceAtNode(parent, pn, child, cn);
            child.attachMode = AttachModes.STACK;
            pn.attachedPart = child;
            cn.attachedPart = parent;
            Link(ship, parent, child);
        }

        /// <summary>Turns the child so its node faces the parent node and puts the two nodes together.</summary>
        private static void PlaceAtNode(Part parent, AttachNode pn, Part child, AttachNode cn)
        {
            Quaternion rot = parent.transform.rotation * Quaternion.FromToRotation(cn.orientation, -pn.orientation);
            Vector3 pos = parent.transform.TransformPoint(pn.position) - rot * cn.position;
            child.transform.SetPositionAndRotation(pos, rot);
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
            GameEvents.onEditorPartPlaced.Fire(child);
        }

        /// <summary>Undoes a half-made placement: the part is removed from the craft if it got that far, then destroyed.</summary>
        private static void Discard(ShipConstruct ship, Part p)
        {
            if (ship != null && ship.parts.Contains(p))
            {
                EditorLogic.DeletePart(p);
            }
            else if (p != null)
            {
                UnityEngine.Object.Destroy(p.gameObject);
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
