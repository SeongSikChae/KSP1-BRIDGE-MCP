using KspBridge.Flight.Control;
using KspBridge.Flight.Maneuver;
using System;
using System.Collections.Generic;

namespace KspBridge.Flight
{
    /// <summary>
    /// list_vessels, set_target and get_flight.target (design 0032 decisions 3, 28, 30). Vessel and port ids are persistentId
    /// (the same as get_flight and get_status); unloaded vessels list their ports from the proto snapshot. Main thread only.
    /// </summary>
    public static class Targeting
    {
        public static object List(Vessel active, bool includeDebris)
        {
            List<Dictionary<string, object>> rows = new List<Dictionary<string, object>>();
            foreach (Vessel x in FlightGlobals.Vessels)
            {
                if (x == null || x.state == Vessel.State.DEAD)
                {
                    continue;
                }
                if (!includeDebris && (x.vesselType == VesselType.Debris || x.vesselType == VesselType.Flag || x.vesselType == VesselType.SpaceObject))
                {
                    continue;
                }
                double dist = active == null ? double.NaN : Vector3d.Distance(x.GetWorldPos3D(), active.GetWorldPos3D());
                rows.Add(new Dictionary<string, object>
                {
                    ["id"] = (double)x.persistentId,
                    ["name"] = x.vesselName,
                    ["type"] = x.vesselType.ToString(),
                    ["situation"] = x.situation.ToString(),
                    ["body"] = x.mainBody != null ? x.mainBody.bodyName : null,
                    ["orbit"] = x.orbit != null ? OrbitMath.OrbitSummary(x.orbit) : null,
                    ["active"] = x == active,
                    ["loaded"] = x.loaded,
                    ["distance"] = OrbitMath.Num(dist),
                    ["docking_ports"] = Ports(x),
                });
            }
            rows.Sort((p, q) => Dist(p).CompareTo(Dist(q)));
            return new Dictionary<string, object> { ["vessels"] = rows };
        }

        private static double Dist(Dictionary<string, object> row) => row["distance"] is double d ? d : double.MaxValue;

        /// <summary>Docking ports {part_id, node_type, state, free}: ModuleDockingNode on a loaded vessel, the proto snapshot's
        /// module values otherwise.</summary>
        private static List<object> Ports(Vessel x)
        {
            List<object> ports = new List<object>();
            if (x.loaded)
            {
                foreach (Part p in x.parts)
                {
                    foreach (ModuleDockingNode n in p.FindModulesImplementing<ModuleDockingNode>())
                    {
                        ports.Add(Port(p.persistentId, n.nodeType, n.state, IsFree(n)));
                    }
                }
                return ports;
            }
            if (x.protoVessel == null)
            {
                return ports;
            }
            foreach (ProtoPartSnapshot pp in x.protoVessel.protoPartSnapshots)
            {
                foreach (ProtoPartModuleSnapshot m in pp.modules)
                {
                    if (m.moduleName != "ModuleDockingNode")
                    {
                        continue;
                    }
                    string state = m.moduleValues?.GetValue("state");
                    string type = m.moduleValues?.GetValue("nodeType") ?? NodeTypeFromPrefab(pp);
                    ports.Add(Port(pp.persistentId, type, state, FreeState(state)));
                }
            }
            return ports;
        }

        private static string NodeTypeFromPrefab(ProtoPartSnapshot pp)
        {
            ModuleDockingNode n = pp.partInfo?.partPrefab?.FindModuleImplementing<ModuleDockingNode>();
            return n?.nodeType;
        }

        private static Dictionary<string, object> Port(uint id, string type, string state, bool free) => new Dictionary<string, object>
        {
            ["part_id"] = (double)id,
            ["node_type"] = type,
            ["state"] = state,
            ["free"] = free,
        };

        /// <summary>Not docked and not pre-attached (a port joined to another part in the editor).</summary>
        public static bool IsFree(ModuleDockingNode n) => n.otherNode == null && FreeState(n.state);

        private static bool FreeState(string state) =>
            state == null || !(state.StartsWith("Docked", StringComparison.Ordinal) || state.StartsWith("PreAttached", StringComparison.Ordinal));

        public static object Set(Vessel v, RequestBody b)
        {
            ManualControl.RequireManual(v);
            if (b.Has("clear") && b.GetBool("clear"))
            {
                FlightGlobals.fetch.SetVesselTarget(null, true);
                return Describe(v);
            }
            ITargetable t;
            if (b.Has("body"))
            {
                string name = b.GetString("body");
                t = FlightGlobals.Bodies.Find(c => string.Equals(c.bodyName, name, StringComparison.OrdinalIgnoreCase))
                    ?? throw ApiException.BadRequest("unknown body " + name);
            }
            else
            {
                double idArg = b.GetDouble("vessel_id");
                Vessel x = FindVessel(idArg) ?? throw ApiException.BadRequest("no vessel with id " + idArg.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (x == v)
                {
                    throw ApiException.BadRequest("cannot target the active vessel");
                }
                if (!b.Has("port"))
                {
                    t = x;
                }
                else
                {
                    if (!x.loaded)
                    {
                        throw new ApiException(409, "target_unloaded", x.vesselName + " is outside physics range: approach it first, then target the port");
                    }
                    double portArg = b.GetDouble("port");
                    ModuleDockingNode port = FindPort(x, portArg) ?? throw ApiException.BadRequest("no docking port with part id " + portArg.ToString(System.Globalization.CultureInfo.InvariantCulture) + " on " + x.vesselName);
                    if (!IsFree(port))
                    {
                        throw new ApiException(409, "port_occupied", "the docking port is already docked (" + port.state + ")");
                    }
                    t = port;
                }
            }
            FlightGlobals.fetch.SetVesselTarget(t, true);
            return Describe(v);
        }

        private static Vessel FindVessel(double id)
        {
            foreach (Vessel x in FlightGlobals.Vessels)
            {
                if (x != null && x.persistentId == id)
                {
                    return x;
                }
            }
            return null;
        }

        private static ModuleDockingNode FindPort(Vessel x, double partId)
        {
            foreach (Part p in x.parts)
            {
                if (p.persistentId == partId)
                {
                    return p.FindModuleImplementing<ModuleDockingNode>();
                }
            }
            return null;
        }

        /// <summary>get_flight.target: null without a target; else {kind vessel|body|port, id, port_id, name, distance,
        /// relative_speed, closest_approach {distance, ut, time_to, relative_speed} (vessel/port target on the same body)}.</summary>
        public static Dictionary<string, object> Describe(Vessel v)
        {
            ITargetable t = v.targetObject;
            if (t == null)
            {
                return new Dictionary<string, object> { ["target"] = null };
            }
            return new Dictionary<string, object> { ["target"] = DescribeTarget(v, t) };
        }

        public static Dictionary<string, object> DescribeTarget(Vessel v, ITargetable t)
        {
            if (t == null)
            {
                return null;
            }
            string kind = t is CelestialBody ? "body" : t is ModuleDockingNode ? "port" : t is Vessel ? "vessel" : "other";
            Vessel tv = t.GetVessel();
            Vector3d pos = t.GetTransform() != null ? (Vector3d)t.GetTransform().position : (tv != null ? tv.GetWorldPos3D() : Vector3d.zero);
            double dist = Vector3d.Distance(v.GetWorldPos3D(), pos);
            double rel = (v.obt_velocity - t.GetObtVelocity()).magnitude;
            Dictionary<string, object> d = new Dictionary<string, object>
            {
                ["kind"] = kind,
                ["name"] = t.GetName(),
                ["id"] = tv != null ? (object)(double)tv.persistentId : null,
                ["port_id"] = t is ModuleDockingNode n && n.part != null ? (object)(double)n.part.persistentId : null,
                ["distance"] = OrbitMath.Num(dist),
                ["relative_speed"] = OrbitMath.Num(rel),
                ["closest_approach"] = null,
            };
            Orbit to = t.GetOrbit();
            if (tv != null && to != null && v.orbit != null && to.referenceBody == v.orbit.referenceBody)
            {
                double now = Planetarium.GetUniversalTime();
                OrbitMath.ClosestApproach(v.orbit, to, now, OrbitMath.SafeSpan(v.orbit, now), out double cut, out double cd);
                if (OrbitMath.Finite(cd))
                {
                    d["closest_approach"] = new Dictionary<string, object>
                    {
                        ["distance"] = OrbitMath.Num(cd),
                        ["ut"] = OrbitMath.Num(cut),
                        ["time_to"] = OrbitMath.Num(cut - now),
                        ["relative_speed"] = OrbitMath.Num(OrbitMath.RelativeSpeed(v.orbit, to, cut)),
                    };
                }
            }
            return d;
        }
    }
}
