using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Editor
{
    /// <summary>
    /// The ship being built: part tree, attachment, stages, symmetry. Parts are identified by craftID, which is stored in the
    /// craft file and unique within the ship, so ids survive save/load and can address parts in later assembly requests.
    /// A part held by the mouse and not attached is not part of the ship and is not listed.
    /// </summary>
    internal static class ShipView
    {
        public static Dictionary<string, object> Describe(ShipConstruct ship)
        {
            ship.GetShipMass(out float dry, out float fuel);
            return new Dictionary<string, object>
            {
                ["name"] = ship.shipName,
                ["facility"] = EditorDriver.editorFacility.ToString(),
                ["part_count"] = ship.parts.Count,
                ["mass_t"] = PartCatalog.Round(dry + fuel),
                ["dry_mass_t"] = PartCatalog.Round(dry),
                ["parts"] = ship.parts.Select(p => (object)DescribePart(p)).ToList(),
            };
        }

        private static Dictionary<string, object> DescribePart(Part part)
        {
            PartRelation r = Relation(part);
            return new Dictionary<string, object>
            {
                ["id"] = part.craftID,
                ["name"] = part.partInfo?.name,
                ["title"] = part.partInfo?.title,
                ["parent"] = r.Parent.HasValue ? (object)r.Parent.Value : null,
                ["attach"] = r.Attach,
                ["parent_node"] = r.ParentNode,
                ["node"] = r.Node,
                ["stage"] = part.inverseStage,
                ["symmetry"] = r.Symmetry.Select(id => (object)id).ToList(),
            };
        }

        /// <summary>Attach relations of a live part, in the same terms as CraftDoc.Relations.</summary>
        public static PartRelation Relation(Part part)
        {
            Part parent = part.parent;
            PartRelation r = new PartRelation
            {
                Name = part.partInfo?.name,
                Symmetry = part.symmetryCounterparts.Select(p => p.craftID).ToList(),
            };
            if (parent != null)
            {
                r.Parent = parent.craftID;
                if (part.attachMode == AttachModes.SRF_ATTACH)
                {
                    r.Attach = "surface";
                    r.Node = part.srfAttachNode?.id;
                }
                else
                {
                    r.Attach = "stack";
                    r.ParentNode = parent.FindAttachNodeByPart(part)?.id;
                    r.Node = part.FindAttachNodeByPart(parent)?.id;
                }
            }
            return r;
        }

        public static Dictionary<uint, PartRelation> Relations(ShipConstruct ship) => ship.parts.ToDictionary(p => p.craftID, Relation);

        /// <summary>
        /// Answer of an edit that changed the ship: changed and removed part ids, parts whose symmetry the game cleared,
        /// whether a part held by the mouse was dropped, and the ship as editor_get_ship shows it.
        /// </summary>
        public static object EditResult(ShipConstruct ship, IEnumerable<uint> changed, IEnumerable<uint> removed,
            IEnumerable<uint> symmetryCleared, bool droppedHeldPart)
        {
            Dictionary<string, object> result = new Dictionary<string, object>
            {
                ["changed"] = changed.Select(id => (object)(double)id).ToList(),
            };
            List<uint> gone = removed?.ToList() ?? new List<uint>();
            if (gone.Count > 0)
                result["removed"] = gone.Select(id => (object)(double)id).ToList();
            List<uint> cleared = symmetryCleared?.ToList() ?? new List<uint>();
            if (cleared.Count > 0)
                result["symmetry_cleared"] = cleared.Select(id => (object)(double)id).ToList();
            result["dropped_held_part"] = droppedHeldPart;
            result["ship"] = Describe(ship);
            return result;
        }
    }
}
