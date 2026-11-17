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
        public static object Describe(ShipConstruct ship)
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
            Part parent = part.parent;
            string attach = null, parentNode = null, node = null;
            if (parent != null)
            {
                if (part.attachMode == AttachModes.SRF_ATTACH)
                {
                    attach = "surface";
                    node = part.srfAttachNode?.id;
                }
                else
                {
                    attach = "stack";
                    parentNode = parent.FindAttachNodeByPart(part)?.id;
                    node = part.FindAttachNodeByPart(parent)?.id;
                }
            }
            return new Dictionary<string, object>
            {
                ["id"] = part.craftID,
                ["name"] = part.partInfo?.name,
                ["title"] = part.partInfo?.title,
                ["parent"] = parent != null ? (object)parent.craftID : null,
                ["attach"] = attach,
                ["parent_node"] = parentNode,
                ["node"] = node,
                ["stage"] = part.inverseStage,
                ["symmetry"] = part.symmetryCounterparts.Select(p => (object)p.craftID).ToList(),
            };
        }
    }
}
