using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KspBridge.Mod.Modules.Check
{
    /// <summary>
    /// Builds the pure <see cref="ShipData"/> from the editor ship and its delta-v calculator (KSP side). Called at the
    /// calculator's completed event, before the app body is switched back, so every value belongs to the requested body.
    /// </summary>
    internal static class ShipSnapshot
    {
        /// <summary>The editor_get_stages rule: the part has a stage icon and staging is on (also decides a staged separator, D047).</summary>
        public static bool IsStaged(Part part)
            => (part.hasStagingIcon || (part.stackIcon != null && part.stackIcon.StageIcon != null)) && part.stagingOn;

        public static ShipData Take(ShipConstruct ship, VesselDeltaV vd, CelestialBody body)
        {
            Dictionary<Part, DeltaVPartInfo> infos = new Dictionary<Part, DeltaVPartInfo>();
            foreach (DeltaVPartInfo info in vd.PartInfo)
            {
                if (info != null && info.part != null)
                    infos[info.part] = info;
            }

            ShipData data = new ShipData
            {
                Body = body.bodyName,
                Facility = ship.shipFacility.ToString(),
            };
            foreach (int id in DeltaVGlobals.PropellantsToIgnore)
            {
                PartResourceDefinition def = PartResourceLibrary.Instance.GetDefinition(id);
                if (def != null)
                    data.IgnoredPropellants.Add(def.name);
            }

            foreach (Part p in ship.parts)
            {
                if (p == null)
                    continue;
                if (IsStaged(p))
                    data.StagedNumbers.Add(p.inverseStage);
                data.Parts.Add(PartOf(p, infos));
            }

            foreach (DeltaVEngineInfo e in vd.OperatingEngineInfo)
            {
                if (e != null && e.engine != null && e.engine.part != null)
                    data.Engines.Add(EngineOf(e));
            }

            foreach (int s in data.StagedNumbers.Distinct())
            {
                DeltaVStageInfo st = vd.GetStage(s);
                if (st == null)
                    continue;
                StageCalc calc = new StageCalc
                {
                    StartMass = st.startMass,
                    EndMass = st.endMass,
                    DvAsl = st.deltaVatASL,
                    DvVac = st.deltaVinVac,
                    TwrAsl = st.TWRASL,
                    TwrVac = st.TWRVac,
                    BurnTime = st.stageBurnTime,
                };
                if (st.deltaVCalcs != null)
                {
                    foreach (DeltaVCalc c in st.deltaVCalcs)
                    {
                        if (c != null)
                            calc.Segments.Add(SegmentOf(c));
                    }
                }
                data.Stages[s] = calc;
            }
            BurnSignal.Fill(data);
            return data;
        }

        private static BurnSegment SegmentOf(DeltaVCalc c)
        {
            BurnSegment segment = new BurnSegment { StartMass = c.startMass, EndMass = c.endMass };
            if (c.activeEngines != null)
            {
                foreach (DeltaVEngineInfo e in c.activeEngines)
                {
                    if (e != null && e.engine != null && e.engine.part != null)
                        segment.Engines.Add(e.engine.part.craftID);
                }
            }
            return segment;
        }

        private static Vec V(Vector3 v) => new Vec(v.x, v.y, v.z);

        private static Vector3 ComPosition(Part p) => p.transform.position + p.transform.rotation * p.CoMOffset;

        private static PartData PartOf(Part p, Dictionary<Part, DeltaVPartInfo> infos)
        {
            // Same rule as the game's editor CoM marker: a non-physical part sits at its parent's mass centre.
            Part at = p.physicalSignificance == Part.PhysicalSignificance.FULL ? p : (p.parent ?? p.potentialParent ?? p);
            PartData d = new PartData
            {
                Id = p.craftID,
                Parent = p.parent != null ? p.parent.craftID : (uint?)null,
                LaunchClamp = p.isLaunchClamp(),
                Mass = p.mass,
                Com = V(ComPosition(at)),
                Command = p.FindModuleImplementing<ModuleCommand>() != null,
                Parachute = p.FindModuleImplementing<ModuleParachute>() != null,
                CrewCapacity = p.CrewCapacity,
            };
            foreach (PartResource r in p.Resources)
            {
                d.Resources.Add(new ResourceData
                {
                    Name = r.resourceName,
                    Amount = r.amount,
                    Density = r.info != null ? r.info.density : 0,
                });
            }
            if (infos.TryGetValue(p, out DeltaVPartInfo info))
            {
                d.HasCalc = true;
                d.DecoupleStage = info.decoupleStage;
                d.ActivationStage = info.activationStage;
                d.IsJettison = info.isJettison;
                d.JettisonMass = info.jettisonMass;
                foreach (int key in info.stageFuelMass.Keys)
                {
                    DeltaVPartInfo.PartStageFuelMass m = info.stageFuelMass[key];
                    d.StageResources[key] = new StageMass(m.startMass, m.endMass);
                }
            }
            if (IsStaged(p))
                d.Separator = SeparatorOf(p);
            return d;
        }

        /// <summary>
        /// The staged separator's detached side: reads the game values (omni, parent, part on the separating node, children) and
        /// lets <see cref="SeparatorData.DetachedSide"/> pick the roots (D077, D085). Null when the part has no separator module.
        /// </summary>
        private static SeparatorData SeparatorOf(Part p)
        {
            AttachNode node = null;
            bool omni = false;
            ModuleDecouplerBase decoupler = p.FindModuleImplementing<ModuleDecouplerBase>();
            if (decoupler != null)
            {
                node = decoupler.ExplosiveNode;
                omni = decoupler.isOmniDecoupler;
            }
            else
            {
                ModuleDockingNode port = p.FindModuleImplementing<ModuleDockingNode>();
                if (port == null)
                    return null;
                if (!string.IsNullOrEmpty(port.referenceAttachNode))
                    node = p.FindAttachNode(port.referenceAttachNode);
            }
            Part nodePart = node != null ? node.attachedPart : null;
            return new SeparatorData
            {
                Stage = p.inverseStage,
                DetachedRoots = SeparatorData.DetachedSide(omni, p.craftID,
                    p.parent != null ? p.parent.craftID : (uint?)null,
                    nodePart != null ? nodePart.craftID : (uint?)null,
                    p.children.Where(c => c != null).Select(c => c.craftID)),
            };
        }

        private static EngineData EngineOf(DeltaVEngineInfo e)
        {
            ModuleEngines engine = e.engine;
            Part part = engine.part;
            EngineData d = new EngineData
            {
                PartId = part.craftID,
                Staged = IsStaged(part),
                Stage = part.inverseStage,
                Propellants = engine.propellants.Select(pr => pr.name).Distinct().ToList(),
                ThrustVac = e.thrustVac,
                ThrustAsl = e.thrustASL,
                GimbalDeg = GimbalDegrees(part),
            };
            CenterOfThrustQuery q = new CenterOfThrustQuery();
            engine.OnCenterOfThrustQuery(q);
            d.ThrustPoint = V(q.pos);
            d.ExhaustDir = V(q.dir).Normalized();
            return d;
        }

        /// <summary>
        /// Smallest range of the enabled gimbal axes × limiter / 100 (degrees). 0 without a gimbal, when locked, or when an
        /// axis is switched off: the lower bound of the turn the gimbal can make whatever the offset's direction.
        /// </summary>
        private static double GimbalDegrees(Part part)
        {
            ModuleGimbal gimbal = part.FindModuleImplementing<ModuleGimbal>();
            if (gimbal == null || gimbal.gimbalLock || !gimbal.enablePitch || !gimbal.enableYaw)
                return 0;
            // Unset (negative) ranges are filled as ModuleGimbal.OnLoad does: XP/YP from gimbalRange, XN from XP, YN from YP.
            float xp = gimbal.gimbalRangeXP < 0 ? gimbal.gimbalRange : gimbal.gimbalRangeXP;
            float yp = gimbal.gimbalRangeYP < 0 ? gimbal.gimbalRange : gimbal.gimbalRangeYP;
            float xn = gimbal.gimbalRangeXN < 0 ? xp : gimbal.gimbalRangeXN;
            float yn = gimbal.gimbalRangeYN < 0 ? yp : gimbal.gimbalRangeYN;
            float range = Mathf.Min(Mathf.Min(xp, xn), Mathf.Min(yp, yn));
            return Mathf.Max(0, range) * gimbal.gimbalLimiter / 100.0;
        }
    }
}
