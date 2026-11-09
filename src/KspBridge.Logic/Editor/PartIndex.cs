using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KspBridge.Editor
{
    /// <summary>Finds editor parts by persistentId and describes them in root-part space. Main thread only.</summary>
    public static class PartIndex
    {
        /// <summary>The editor craft's root part; null for an empty craft.</summary>
        public static Part Root(ShipConstruct ship) => ship.parts.Count == 0 ? null : EditorLogic.RootPart;

        public static Part Find(ShipConstruct ship, uint id) =>
            ship.parts.FirstOrDefault(p => p.persistentId == id)
            ?? throw new ApiException(404, "part_not_found", "no part with id " + id + " in the editor craft (use get_craft for ids)");

        /// <summary>World point in root-part space (+Y = rocket up / plane nose).</summary>
        public static Vector3 Local(ShipConstruct ship, Vector3 world) => Root(ship).transform.InverseTransformPoint(world);

        public static Dictionary<string, object> Summary(ShipConstruct ship, Part p)
        {
            Part root = Root(ship);
            return new Dictionary<string, object>
            {
                ["id"] = p.persistentId,
                ["part"] = p.partInfo.name,
                ["title"] = p.partInfo.title,
                ["parent"] = p.parent != null ? (object)p.parent.persistentId : null,
                ["attach"] = Attach(p),
                ["symmetry"] = Symmetry(p),
                ["stage"] = p.hasStagingIcon ? (object)p.inverseStage : null,
                ["position"] = Format.Vec(Local(ship, p.transform.position)),
                ["rotation"] = Format.Vec((Quaternion.Inverse(root.transform.rotation) * p.transform.rotation).eulerAngles),
            };
        }

        private static object Attach(Part p)
        {
            if (p.parent == null)
            {
                return new Dictionary<string, object> { ["kind"] = "root" };
            }
            AttachNode toParent = p.FindAttachNodeByPart(p.parent);
            if (p.attachMode == AttachModes.SRF_ATTACH || toParent == null)
            {
                return new Dictionary<string, object> { ["kind"] = "surface" };
            }
            return new Dictionary<string, object>
            {
                ["kind"] = "node",
                ["parent_node"] = p.parent.FindAttachNodeByPart(p)?.id,
                ["child_node"] = toParent.id,
            };
        }

        private static object Symmetry(Part p)
        {
            if (p.symmetryCounterparts == null || p.symmetryCounterparts.Count == 0)
            {
                return null;
            }
            return new Dictionary<string, object>
            {
                ["mode"] = p.symMethod == SymmetryMethod.Mirror ? "mirror" : "radial",
                ["group"] = new[] { p }.Concat(p.symmetryCounterparts).Select(c => (object)c.persistentId).ToList(),
            };
        }
    }
}
