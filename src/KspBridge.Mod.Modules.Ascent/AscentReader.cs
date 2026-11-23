using KspBridge.Mod.Modules.Control;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Ascent
{
    /// <summary>Reads <see cref="AscentReading"/> from the active vessel (KSP side).</summary>
    public static class AscentReader
    {
        public static AscentReading Read(Vessel v)
        {
            CelestialBody body = v.mainBody;
            Orbit orbit = v.orbit;
            VesselAttitude attitude = VesselReader.Read(v);
            AscentReading r = new AscentReading
            {
                UT = Planetarium.GetUniversalTime(),
                Altitude = v.altitude,
                AtmosphereTop = body.atmosphere ? body.atmosphereDepth : (double?)null,
                BodyRadius = body.Radius,
                Gm = body.gravParameter,
                SoiRadius = body.sphereOfInfluence,
                Eccentricity = orbit.eccentricity,
                ApA = orbit.eccentricity >= 1 ? double.PositiveInfinity : orbit.ApA,
                PeA = orbit.PeA,
                SemiMajorAxis = orbit.semiMajorAxis,
                Inclination = orbit.inclination,
                TimeToAp = orbit.timeToAp,
                VerticalSpeed = v.verticalSpeed,
                Latitude = v.latitude,
                OnSurface = v.LandedOrSplashed || v.situation == Vessel.Situations.PRELAUNCH,
                Up = attitude.Frame.Up,
                North = attitude.Frame.North,
                East = attitude.Frame.East,
                OrbitVelocity = attitude.Frame.OrbitVelocity,
                SurfaceVelocity = attitude.Frame.SurfaceVelocity,
                Nose = attitude.Nose,
                DynamicPressureKpa = v.dynamicPressurekPa,
                Mass = v.GetTotalMass(),
                NextStage = StageActivator.NextStage(v),
                Stages = Stages(v),
            };
            ReadEngines(v, r);
            return r;
        }

        private static void ReadEngines(Vessel v, AscentReading r)
        {
            List<int> ignored = DeltaVGlobals.PropellantsToIgnore;
            List<Part> parts = v.parts;
            for (int i = 0; i < parts.Count; i++)
            {
                Part part = parts[i];
                if (part == null)
                    continue;
                PartModuleList modules = part.Modules;
                for (int j = 0; j < modules.Count; j++)
                {
                    if (!(modules[j] is ModuleEngines engine))
                        continue;
                    r.Thrust += engine.finalThrust;
                    if (engine.isOperational)
                        r.MaxThrustVac += engine.MaxThrustOutputVac(true);
                    r.Engines.Add(new EngineInfo
                    {
                        Operational = engine.isOperational,
                        FlameOut = engine.flameout,
                        Ignited = engine.EngineIgnited,
                        Stage = part.inverseStage,
                        Separation = part.separationIndex,
                        StagingOn = part.stagingOn,
                        Propellant = PropellantReaches(part, engine, ignored),
                    });
                }
            }
        }

        /// <summary>Every propellant the delta-v calculator counts reaches the engine with the game's own flow rule (D009, D014).</summary>
        private static bool PropellantReaches(Part part, ModuleEngines engine, List<int> ignored)
        {
            List<Propellant> propellants = engine.propellants;
            if (propellants == null || propellants.Count == 0)
                return false;
            bool counted = false;
            for (int i = 0; i < propellants.Count; i++)
            {
                Propellant p = propellants[i];
                if (ignored != null && ignored.Contains(p.id))
                    continue;
                counted = true;
                part.GetConnectedResourceTotals(p.id, p.GetFlowMode(), out double amount, out double _);
                if (!(amount > 0))
                    return false;
            }
            return counted;
        }

        /// <summary>The delta-v calculator's stages, highest first; null while it is not ready.</summary>
        private static List<StageBurn> Stages(Vessel v)
        {
            VesselDeltaV dv = v.VesselDeltaV;
            if (dv == null || !dv.IsReady || dv.OperatingStageInfo == null)
                return null;
            List<StageBurn> stages = new List<StageBurn>();
            foreach (DeltaVStageInfo s in dv.OperatingStageInfo)
            {
                if (s == null)
                    continue;
                stages.Add(new StageBurn
                {
                    Stage = s.stage,
                    StartMass = s.startMass,
                    EndMass = s.endMass,
                    DvVac = s.deltaVinVac,
                    BurnTime = s.stageBurnTime,
                });
            }
            stages.Sort((a, b) => b.Stage.CompareTo(a.Stage));
            return stages;
        }
    }
}
