using KspBridge.Editor;
using PreFlightTests;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KspBridge.Check
{
    /// <summary>
    /// Stage performance of the editor craft (check_craft.performance). Main thread only.
    /// Stage layout, masses and burn segments come from the game's own VesselDeltaV (the staging app's numbers); thrust, Isp,
    /// TWR and Δv for the asked situation are worked out with the game's formulas on those segments, so the staging app's
    /// own situation setting is left alone.
    /// </summary>
    public static class PerformanceCheck
    {
        public static object Run(ShipConstruct ship, CelestialBody body, double altitude, bool vacuum)
        {
            Dictionary<string, object> situation = new Dictionary<string, object>
            {
                ["body"] = body.bodyName,
                ["altitude"] = altitude,
                ["vacuum"] = vacuum,
            };
            VesselDeltaV dv = ship.vesselDeltaV;
            if (!UpToDate(dv))
            {
                // The game recalculates by itself after an edit (a few frames later); asking again shortly is enough.
                return new Dictionary<string, object> { ["ready"] = false, ["situation"] = situation };
            }

            Atmosphere atm = Atmosphere.At(body, altitude, vacuum);
            double g = body.gravParameter / Math.Pow(body.Radius + altitude, 2);
            List<Dictionary<string, object>> stages = dv.OperatingStageInfo.Where(s => s != null).OrderByDescending(s => s.stage)
                .Select(s => Stage(s, atm, g)).ToList();
            Part root = PartIndex.Root(ship);

            List<object> warnings = new List<object>();
            Dictionary<string, object> liftoff = LiftoffStage(ship, stages);
            if (liftoff != null && (double)liftoff["twr"] < 1)
            {
                warnings.Add("twr_below_1");
            }
            Vector3 com = EditorMarker_CoM.findCenterOfMass(root);
            Ray cot = EditorMarker_CoT.FindCoT(root);
            Ray? col = CenterOfLift(root);
            // The craft's nose is the editor's vessel up (the CoL marker's airflow axis), not the root part's +Y,
            // which points the other way when the root is mounted upside down.
            Vector3 nose = EditorLogic.VesselRotation * Vector3.up;
            if (col.HasValue && Vector3.Dot(col.Value.origin - com, nose) > 0)
            {
                warnings.Add("col_ahead_of_com");
            }
            if (!new NoControlSources().TestCondition())
            {
                warnings.Add("no_control");
            }

            return new Dictionary<string, object>
            {
                ["ready"] = true,
                ["situation"] = situation,
                ["stages"] = stages.Cast<object>().ToList(),
                ["total_delta_v"] = Math.Round(stages.Sum(s => (double)s["delta_v"]), 1),
                ["com"] = Format.Vec(PartIndex.Local(ship, com)),
                ["cot"] = cot.direction == Vector3.zero ? null : Format.Vec(PartIndex.Local(ship, cot.origin)),
                ["col"] = col.HasValue ? Format.Vec(PartIndex.Local(ship, col.Value.origin)) : null,
                ["warnings"] = warnings,
            };
        }

        private static readonly System.Reflection.FieldInfo CalcsDirty =
            typeof(VesselDeltaV).GetField("calcsDirty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        /// <summary>
        /// The stage results describe the craft as it is now. IsReady stays true after the first calculation; an edit only
        /// marks calcsDirty (onShipModified), the recalculation starts a few frames later and swaps the stage lists when it
        /// ends, so the old lists are served until then.
        /// </summary>
        private static bool UpToDate(VesselDeltaV dv) =>
            dv != null && dv.IsReady && !dv.SimulationRunning && !(CalcsDirty != null && (bool)CalcsDirty.GetValue(dv));

        /// <summary>The body by name (case-insensitive), or the home body when name is null; 400 for an unknown name.</summary>
        public static CelestialBody Body(string name)
        {
            if (name == null)
            {
                return FlightGlobals.GetHomeBody();
            }
            return FlightGlobals.Bodies.FirstOrDefault(b => string.Equals(b.bodyName, name, StringComparison.OrdinalIgnoreCase))
                ?? throw ApiException.BadRequest("unknown body '" + name + "' (" + string.Join(", ", FlightGlobals.Bodies.Select(b => b.bodyName)) + ")");
        }

        /// <summary>
        /// The stage the craft leaves the ground in: where its launch clamps let go (engines lit earlier while clamped
        /// count in that stage too), or without clamps the first stage that makes thrust. Null when nothing makes thrust.
        /// </summary>
        private static Dictionary<string, object> LiftoffStage(ShipConstruct ship, List<Dictionary<string, object>> stages)
        {
            List<int> clampStages = ship.parts.Where(p => p != null && p.isLaunchClamp()).Select(p => p.inverseStage).ToList();
            if (clampStages.Count > 0)
            {
                // Stages fire from the highest number down, so the craft is free once the last clamp lets go.
                int release = clampStages.Min();
                return stages.FirstOrDefault(s => (int)s["stage"] == release)
                    ?? stages.Where(s => (int)s["stage"] < release).FirstOrDefault(s => (double)s["thrust"] > 0);
            }
            return stages.FirstOrDefault(s => (double)s["thrust"] > 0);
        }

        private static Dictionary<string, object> Stage(DeltaVStageInfo s, Atmosphere atm, double g)
        {
            // Stage thrust and Isp: the engines active in the stage (DeltaVStageInfo.CalculateISP).
            Burn stageBurn = Burn.Of(s.enginesActiveInStage, atm);
            // Stage Δv: the sum of the game's burn segments (engines may burn out at different times within a stage).
            double deltaV = 0;
            foreach (DeltaVCalc c in s.deltaVCalcs ?? new List<DeltaVCalc>())
            {
                Burn b = Burn.Of(c.activeEngines, atm);
                if (b.Thrust > 0 && c.endMass > 0 && c.startMass > c.endMass)
                {
                    deltaV += b.VectoredThrust * Math.Log(c.startMass / c.endMass) * b.Isp * PhysicsGlobals.GravitationalAcceleration / b.Thrust;
                }
            }
            return new Dictionary<string, object>
            {
                ["stage"] = s.stage,
                ["mass_start"] = Math.Round(s.startMass, 3),
                ["mass_end"] = Math.Round(s.endMass, 3),
                ["thrust"] = Math.Round(stageBurn.Thrust, 2),
                ["isp"] = Math.Round(stageBurn.Isp, 1),
                ["twr"] = s.startMass > 0 ? Math.Round(stageBurn.Thrust / (s.startMass * g), 2) : 0.0,
                ["delta_v"] = Math.Round(deltaV, 1),
                ["delta_v_vac"] = Math.Round(s.deltaVinVac, 1),
                ["delta_v_asl"] = Math.Round(s.deltaVatASL, 1),
                ["burn_time"] = Math.Round(s.stageBurnTime, 1),
            };
        }

        /// <summary>
        /// The CoL marker's own reference (EditorMarker_CoL.UpdatePosition in the editor): the overlay's pitch and air speed,
        /// 100 m on the home body. Null when nothing makes lift.
        /// </summary>
        internal static Ray? CenterOfLift(Part root)
        {
            EditorVesselOverlays overlays = EditorVesselOverlays.fetch;
            float pitch = overlays != null ? overlays.referencePitch : 1f;
            float speed = overlays != null && overlays.referenceAirSpeed > 0 ? overlays.referenceAirSpeed : 70f;
            Vector3 refVel = EditorLogic.VesselRotation * (Quaternion.AngleAxis(pitch, root.transform.right) * Vector3.up) * speed;
            CelestialBody home = Planetarium.fetch.Home;
            double refAlt = 100.0;
            double stp = FlightGlobals.getStaticPressure(refAlt, home);
            double temp = FlightGlobals.getExternalTemperature(refAlt, home);
            double dens = FlightGlobals.getAtmDensity(stp, temp, home);
            Ray col = EditorMarker_CoL.FindCoL(root, refVel, refAlt, stp, dens, out bool noLift);
            // Symmetric lift directions cancel (direction zero) while the position is still valid, as the marker shows it.
            return noLift ? (Ray?)null : col;
        }

        /// <summary>Air at a situation: pressure (atm), temperature (K) and density (kg/m³); all zero in vacuum or above the air.</summary>
        private struct Atmosphere
        {
            public float PressureAtm;
            public double Temperature;
            public double Density;

            public static Atmosphere At(CelestialBody body, double altitude, bool vacuum)
            {
                if (vacuum || !body.atmosphere || altitude >= body.atmosphereDepth)
                {
                    return new Atmosphere { PressureAtm = 0f, Temperature = 0, Density = 0 };
                }
                double kPa = body.GetPressure(altitude);
                double t = body.GetTemperature(altitude);
                return new Atmosphere { PressureAtm = (float)(kPa * PhysicsGlobals.KpaToAtmospheres), Temperature = t, Density = body.GetDensity(kPa, t) };
            }
        }

        /// <summary>Thrust, thrust-weighted Isp and vectored thrust of a set of engines in one situation.</summary>
        private struct Burn
        {
            public double Thrust;
            public double VectoredThrust;
            public double Isp;

            public static Burn Of(IEnumerable<DeltaVEngineInfo> engines, Atmosphere atm)
            {
                double thrust = 0, flow = 0;
                Vector3 vectored = Vector3.zero;
                foreach (DeltaVEngineInfo e in engines ?? Enumerable.Empty<DeltaVEngineInfo>())
                {
                    if (e?.engine == null)
                    {
                        continue;
                    }
                    // Same as DeltaVEngineInfo.CalculateThrustVector / CalculateISP for a given air.
                    float t = e.engine.MaxThrustOutputAtm(false, true, atm.PressureAtm, atm.Temperature, atm.Density);
                    double isp = e.engine.atmosphereCurve.Evaluate(atm.PressureAtm);
                    if (e.engine.useAtmCurveIsp)
                    {
                        isp *= e.engine.atmCurveIsp.Evaluate((float)(atm.Density * 0.8163265306122448));
                    }
                    if (t <= 0 || isp <= 0)
                    {
                        continue;
                    }
                    thrust += t;
                    flow += t / isp;
                    // The thrust direction as the game takes it (DeltaVEngineInfo.CalculateThrustVector); thrustVectorVac is
                    // zero for air-breathing engines, whose vacuum thrust is zero.
                    CenterOfThrustQuery q = new CenterOfThrustQuery();
                    q.Reset();
                    e.engine.OnCenterOfThrustQuery(q);
                    vectored += q.dir * t;
                }
                return new Burn { Thrust = thrust, VectoredThrust = vectored.magnitude, Isp = flow > 0 ? thrust / flow : 0 };
            }
        }
    }
}
