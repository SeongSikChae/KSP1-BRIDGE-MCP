using KspBridge.Catalog;
using KspBridge.Editor;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KspBridge.Check
{
    /// <summary>Structure issues of the editor craft (check_craft.structure, edit response warnings). Main thread only.</summary>
    public static class StructureCheck
    {
        /// <summary>
        /// Every issue of the craft: locked_part, orphan, bad_node, overlap, no_root. With focus, the overlap test
        /// only looks at pairs that include a focus part (the other checks are cheap and always cover the whole craft).
        /// </summary>
        public static List<Dictionary<string, object>> Run(ShipConstruct ship, ICollection<Part> focus = null)
        {
            List<Dictionary<string, object>> issues = new List<Dictionary<string, object>>();
            if (ship.parts.Count == 0)
            {
                issues.Add(Issue("error", "no_root", null, "the craft has no parts"));
                return issues;
            }
            Part root = PartIndex.Root(ship);
            foreach (Part p in ship.parts)
            {
                if (!PartCatalog.IsUsable(p.partInfo))
                {
                    issues.Add(Issue("error", "locked_part", new[] { p }, p.partInfo.name + " is not unlocked or purchased in this save"));
                }
                if (p != root && p.parent == null)
                {
                    issues.Add(Issue("error", "orphan", new[] { p }, p.partInfo.name + " is not attached to the craft"));
                }
                foreach (AttachNode n in p.attachNodes)
                {
                    Part other = n.attachedPart;
                    if (other != null && other.FindAttachNodeByPart(p) == null && other.srfAttachNode?.attachedPart != p)
                    {
                        issues.Add(Issue("error", "bad_node", new[] { p, other },
                            "node " + n.id + " of " + p.partInfo.name + " is linked to " + other.partInfo.name + " one way only"));
                    }
                }
            }
            issues.AddRange(Overlaps(ship, focus));
            // unsaveable is not checked: ShipConstruct.AreAllPartsConnected fails any part with neither parent nor children,
            // which is every one-part craft, and a non-root part without a parent is already reported as orphan.
            return issues;
        }

        /// <summary>The issues for an edit response: those about a focus part, and those about the whole craft.</summary>
        public static List<object> Related(ShipConstruct ship, ICollection<Part> focus) =>
            Run(ship, focus).Where(i => !(i["parts"] is List<object> ids) || ids.Count == 0
                    || ids.Any(id => focus.Any(f => f.persistentId == (uint)id)))
                .Cast<object>().ToList();

        /// <summary>
        /// Overlapping parts that will collide. Parts of one vessel do not collide with each other (CollisionManager ignores the
        /// pair unless both parts have sameVesselCollision), so an overlap only matters when a decoupler or docking port lies
        /// between the two parts in the part tree (they become separate vessels when it fires) or both parts collide in-vessel.
        /// </summary>
        private static IEnumerable<Dictionary<string, object>> Overlaps(ShipConstruct ship, ICollection<Part> focus)
        {
            if (focus != null && focus.Count == 0)
            {
                yield break;
            }
            List<Part> parts = ship.parts.Where(p => p != null).ToList();
            Dictionary<Part, Part> cuts = SeparationCuts(parts);
            Dictionary<Part, List<Collider>> colliders = new Dictionary<Part, List<Collider>>();
            Dictionary<Part, Bounds?> bounds = new Dictionary<Part, Bounds?>();
            Bounds? BoundsOf(Part p)
            {
                if (!bounds.TryGetValue(p, out Bounds? b))
                {
                    colliders[p] = OwnColliders(p);
                    bounds[p] = b = Union(colliders[p]);
                }
                return b;
            }
            for (int i = 0; i < parts.Count; i++)
            {
                for (int j = i + 1; j < parts.Count; j++)
                {
                    Part a = parts[i], b = parts[j];
                    if (focus != null && !focus.Contains(a) && !focus.Contains(b))
                    {
                        continue;
                    }
                    Bounds? ba = BoundsOf(a), bb = BoundsOf(b);
                    if (ba == null || bb == null || !ba.Value.Intersects(bb.Value))
                    {
                        continue;   // R3: cheap whole-part bounds test first
                    }
                    Part separator = Separator(a, b, cuts);
                    bool collideInVessel = a.sameVesselCollision && b.sameVesselCollision;
                    if (separator == null && !collideInVessel)
                    {
                        continue;
                    }
                    float depth = MaxPenetration(colliders[a], colliders[b]);
                    if (depth > 0f)
                    {
                        string why = separator != null
                            ? "they become separate vessels when " + separator.partInfo.name + " (" + separator.persistentId + ") separates"
                            : "both collide with parts of their own vessel";
                        Dictionary<string, object> issue = Issue("warning", "overlap", new[] { a, b },
                            a.partInfo.name + " and " + b.partInfo.name + " overlap by " + depth.ToString("0.###") + " m; " + why);
                        issue["depth"] = Format.Round(depth);
                        if (separator != null)
                        {
                            issue["separator"] = separator.persistentId;
                        }
                        yield return issue;
                    }
                }
            }
        }

        /// <summary>
        /// The part-tree links that break when a separator fires, keyed by the child part of the link, with the separator part.
        /// Decouplers break the link at their explosive node ("srf" = their surface attach node), omni decouplers every link,
        /// docking ports the link at their reference node (ModuleDecouplerBase.PartDetaches, ModuleDecouple.OnDecouple,
        /// ModuleDockingNode.Decouple).
        /// </summary>
        private static Dictionary<Part, Part> SeparationCuts(List<Part> parts)
        {
            Dictionary<Part, Part> cuts = new Dictionary<Part, Part>();
            void Cut(Part separator, Part other)
            {
                if (other == null)
                {
                    return;
                }
                if (other.parent == separator)
                {
                    cuts[other] = separator;
                }
                else if (separator.parent == other)
                {
                    cuts[separator] = separator;
                }
            }
            foreach (Part p in parts)
            {
                foreach (PartModule m in p.Modules)
                {
                    if (m is ModuleDecouplerBase d)
                    {
                        if (d.isOmniDecoupler)
                        {
                            Cut(p, p.parent);
                            foreach (Part c in p.children)
                            {
                                Cut(p, c);
                            }
                            continue;
                        }
                        AttachNode node = d.explosiveNodeID == "srf" ? p.srfAttachNode : p.FindAttachNode(d.explosiveNodeID);
                        Cut(p, node?.attachedPart);
                    }
                    else if (m is ModuleDockingNode dock)
                    {
                        // The game sets referenceNode from referenceAttachNode only in OnLoad; a part made from the prefab
                        // carries a copied node, so look the node up again the same way.
                        AttachNode node = string.IsNullOrEmpty(dock.referenceAttachNode) ? dock.referenceNode : p.FindAttachNode(dock.referenceAttachNode);
                        Cut(p, node?.attachedPart);
                    }
                }
            }
            return cuts;
        }

        /// <summary>The separator whose firing splits a and b into different vessels: a broken link on the tree path between them.</summary>
        private static Part Separator(Part a, Part b, Dictionary<Part, Part> cuts)
        {
            HashSet<Part> aUp = new HashSet<Part>();
            for (Part x = a; x != null; x = x.parent)
            {
                aUp.Add(x);
            }
            Part common = b;
            while (common != null && !aUp.Contains(common))
            {
                common = common.parent;
            }
            foreach (Part start in new[] { a, b })
            {
                for (Part x = start; x != null && x != common; x = x.parent)
                {
                    if (cuts.TryGetValue(x, out Part separator))
                    {
                        return separator;
                    }
                }
            }
            return null;
        }

        /// <summary>The deepest penetration between any collider of a and any collider of b whose bounds meet (0 = none).</summary>
        private static float MaxPenetration(List<Collider> a, List<Collider> b)
        {
            float max = 0f;
            foreach (Collider ca in a)
            {
                foreach (Collider cb in b)
                {
                    if (!ca.bounds.Intersects(cb.bounds))
                    {
                        continue;
                    }
                    Transform ta = ca.transform, tb = cb.transform;
                    if (Physics.ComputePenetration(ca, ta.position, ta.rotation, cb, tb.position, tb.rotation, out _, out float d) && d > max)
                    {
                        max = d;
                    }
                }
            }
            return max;
        }

        /// <summary>The part's own enabled colliders (attached parts are transform children, so theirs are left out).</summary>
        private static List<Collider> OwnColliders(Part p) =>
            p.GetComponentsInChildren<Collider>()
                .Where(c => c.enabled && c.gameObject.activeInHierarchy && c.GetComponentInParent<Part>() == p).ToList();

        private static Bounds? Union(List<Collider> colliders)
        {
            if (colliders.Count == 0)
            {
                return null;
            }
            Bounds all = colliders[0].bounds;
            foreach (Collider c in colliders.Skip(1))
            {
                all.Encapsulate(c.bounds);
            }
            return all;
        }

        private static Dictionary<string, object> Issue(string severity, string code, IEnumerable<Part> parts, string message) =>
            new Dictionary<string, object>
            {
                ["severity"] = severity,
                ["code"] = code,
                ["parts"] = (parts ?? Enumerable.Empty<Part>()).Select(p => (object)p.persistentId).ToList(),
                ["message"] = message,
            };
    }
}
