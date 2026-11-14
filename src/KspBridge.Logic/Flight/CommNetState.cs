using CommNet;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Flight
{
    /// <summary>get_commnet (design 0041 decisions 7·8): the active vessel's CommNet connection and control path. Main thread only.</summary>
    public static class CommNetState
    {
        private static readonly string[] Connection = { "connected", "connected_home", "control_state", "signal_strength", "signal", "signal_delay", "path" };

        public static object Describe(Vessel v)
        {
            CommNetVessel c = v.connection as CommNetVessel;
            CommNode node = c?.Comm;
            bool enabled = HighLogic.CurrentGame.Parameters.Difficulty.EnableCommNet;
            Dictionary<string, object> d = new Dictionary<string, object>
            {
                ["enabled"] = enabled,
                ["vessel"] = v.vesselName,
                ["id"] = (double)v.persistentId,
                ["antenna"] = node == null ? null : new Dictionary<string, object>
                {
                    ["transmit_power"] = node.antennaTransmit.power,
                    ["relay_power"] = node.antennaRelay.power,
                },
            };
            if (!enabled || c == null)
            {
                foreach (string k in Connection)
                {
                    d[k] = null;
                }
                return d;
            }
            d["connected"] = c.IsConnected;
            d["connected_home"] = c.IsConnectedHome;
            d["control_state"] = c.ControlState.ToString();
            d["signal_strength"] = c.SignalStrength;
            d["signal"] = c.Signal.ToString();
            d["signal_delay"] = c.SignalDelay;
            List<object> path = new List<object>();
            if (c.ControlPath != null)
            {
                foreach (CommLink link in c.ControlPath)   // path links: a = start side, b = next node (CommNetwork.FindPath)
                {
                    path.Add(Hop(link));
                }
            }
            d["path"] = path;
            return d;
        }

        /// <summary>
        /// Path links carry only a, b, cost and the used strength. The antenna roles come from the network link from[to]
        /// (a node is a Dictionary of neighbour → link; bestLink is a pathing scratch value reset by every vessel's path
        /// search, so it is not read here) whose stored strength equals the used one (RR, AR, BR order on ties).
        /// </summary>
        private static Dictionary<string, object> Hop(CommLink link)
        {
            CommNode from = link.a, to = link.b;
            double? range = null;
            string aRole = null, bRole = null;   // antenna used by o.a / o.b
            CommLink o = null;
            if (from != null && to != null && from.TryGetValue(to, out o) && o != null)
            {
                double s = link.signalStrength;
                if (o.strengthRR == s)
                {
                    range = Range(o.a.antennaRelay, o.b.antennaRelay);
                    aRole = bRole = "relay";
                }
                else if (o.strengthAR == s)
                {
                    range = Range(o.a.antennaRelay, o.b.antennaTransmit);
                    aRole = "relay";
                    bRole = "transmit";
                }
                else if (o.strengthBR == s)
                {
                    range = Range(o.b.antennaRelay, o.a.antennaTransmit);
                    aRole = "transmit";
                    bRole = "relay";
                }
            }
            bool fromIsA = o != null && o.a == from;
            return new Dictionary<string, object>
            {
                ["from"] = Node(from),
                ["to"] = Node(to),
                ["distance"] = from != null && to != null ? (object)(from.precisePosition - to.precisePosition).magnitude : null,
                ["max_range"] = range,
                ["signal_strength"] = link.signalStrength,
                ["from_antenna"] = fromIsA ? aRole : bRole,
                ["to_antenna"] = fromIsA ? bRole : aRole,
            };
        }

        private static double Range(CommNode.AntennaInfo a, CommNode.AntennaInfo b) => CommNetScenario.RangeModel.GetMaximumRange(a.power, b.power);

        private static Dictionary<string, object> Node(CommNode n)
        {
            if (n == null)
            {
                return null;
            }
            Vessel owner = FlightGlobals.Vessels.FirstOrDefault(x => x != null && x.connection != null && x.connection.Comm == n);
            return new Dictionary<string, object>
            {
                ["name"] = KSP.Localization.Localizer.Format(n.displayName),   // ground stations keep a #autoLOC key here
                ["vessel_id"] = owner != null ? (object)(double)owner.persistentId : null,
                ["home"] = n.isHome,
            };
        }
    }
}
