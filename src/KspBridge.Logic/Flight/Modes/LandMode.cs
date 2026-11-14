using KspBridge.Flight.Control;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace KspBridge.Flight.Modes
{
    /// <summary>
    /// land: powered landing from a descent (#17, docs 0028~0030). Burns against the stopping distance to the ground
    /// (radar altitude minus the vessel bottom), holds final_speed below final_altitude, tilts to vertical as the
    /// horizontal speed drops, turns on the Gear group at gear_altitude and completes once landed and still.
    /// </summary>
    public sealed class LandMode : IFlightMode
    {
        public static readonly string[] Args = { "margin", "final_altitude", "final_speed", "gear_altitude" };

        private readonly double _margin;
        private readonly double _finalAlt;
        private readonly double _finalSpeed;
        private readonly double _gearAlt;   // NaN = gear off

        private LandMode(double margin, double finalAlt, double finalSpeed, double gearAlt)
        {
            _margin = margin;
            _finalAlt = finalAlt;
            _finalSpeed = finalSpeed;
            _gearAlt = gearAlt;
        }

        /// <summary>Validates the arguments at submit time and returns the step's mode factory.</summary>
        public static Func<IFlightMode> Parse(Dictionary<string, object> o, string path)
        {
            double margin = Plan.OptionalNumber(o, "margin", path, 0.1, 1, 0.8);
            double finalAlt = Plan.OptionalNumber(o, "final_altitude", path, 0, 1000, 20);
            double finalSpeed = Plan.OptionalNumber(o, "final_speed", path, 0.1, 20, 2);
            double gearAlt;
            if (o.TryGetValue("gear_altitude", out object g) && g is bool b)
            {
                if (b)
                {
                    throw Plan.Invalid(path + ".gear_altitude", "must be a number 0..10000 or false");
                }
                gearAlt = double.NaN;
            }
            else
            {
                gearAlt = Plan.OptionalNumber(o, "gear_altitude", path, 0, 10000, 500);
            }
            return () => new LandMode(margin, finalAlt, finalSpeed, gearAlt);
        }

        /// <summary>Re-measure interval (s) while the legs extend after the Gear group turns on.</summary>
        private const double RemeasureS = 0.5;

        /// <summary>How long (s) after turning on the Gear group the bottom keeps being re-measured.</summary>
        private const double GearSettleS = 5;

        private bool _gearDone;
        private double _gearSettleUntil;   // 0 = not settling

        // bottom_offset cache: re-measured when the part count or the Gear group changes, and every RemeasureS while settling
        private double _bottom;
        private int _partCount = -1;
        private bool _gearState;
        private double _measuredUt;

        public bool Done { get; private set; }

        public void Begin(Vessel v, Telemetry t)
        {
        }

        public ModeCommand Update(Vessel v, Telemetry t)
        {
            double h = Height(v);

            // Never in the submit trial: a failed submit must leave the vessel unchanged; the first real Tick turns it on.
            if (!_gearDone && !double.IsNaN(_gearAlt) && h <= _gearAlt && !FlightEvents.InTrial)
            {
                _gearDone = true;
                if (!ManualControl.TrySetGroup(v, KSPActionGroup.Gear, true, out string blocked))
                {
                    Log("gear blocked: " + blocked);
                }
                _gearSettleUntil = Planetarium.GetUniversalTime() + GearSettleS;
                Log("gear on: h=" + F(h, 1));
            }
            return ModeCommand.Idle;
        }

        /// <summary>Ground clearance of the vessel bottom: radarAltitude (CoM) minus the CoM-to-bottom distance.</summary>
        private double Height(Vessel v)
        {
            double ut = Planetarium.GetUniversalTime();
            bool gear = v.ActionGroups[KSPActionGroup.Gear];
            int count = v.parts.Count;
            if (count != _partCount || gear != _gearState || (ut < _gearSettleUntil && ut - _measuredUt >= RemeasureS))
            {
                _bottom = MeasureBottom(v, out int fallback);
                _partCount = count;
                _gearState = gear;
                _measuredUt = ut;
                Log("bottom=" + F(_bottom, 3) + " parts=" + count + " gear=" + gear + " fallback=" + fallback +
                    " radar=" + F(v.radarAltitude, 2));
            }
            return v.radarAltitude - _bottom;
        }

        /// <summary>Distance from the CoM to the lowest collider point along the vessel's −up axis (ReferenceTransform).
        /// fallback = colliders measured by their world AABB (shapes ClosestPoint does not support).</summary>
        private static double MeasureBottom(Vessel v, out int fallback)
        {
            fallback = 0;
            Vector3 com = v.CoMD;
            Vector3 down = -v.ReferenceTransform.up;
            Vector3 far = com + down * 1000f;
            double max = 0;
            List<Part> parts = v.parts;
            for (int i = 0; i < parts.Count; i++)
            {
                Collider[] cs = parts[i].GetPartColliders();
                if (cs == null)
                {
                    continue;
                }
                for (int j = 0; j < cs.Length; j++)
                {
                    Collider c = cs[j];
                    if (c == null || !c.enabled || c.isTrigger)
                    {
                        continue;
                    }
                    max = Math.Max(max, Lowest(c, com, down, far, ref fallback));
                }
            }
            return max;
        }

        /// <summary>ClosestPoint only for the shapes Unity supports (it returns the query point unchanged for others);
        /// otherwise the lowest of the 8 world AABB corners (below the real shape: conservative).</summary>
        private static double Lowest(Collider c, Vector3 com, Vector3 down, Vector3 far, ref int fallback)
        {
            MeshCollider mc = c as MeshCollider;
            if (c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (mc != null && mc.convex))
            {
                return Vector3.Dot(c.ClosestPoint(far) - com, down);
            }
            fallback++;
            Bounds b = c.bounds;
            double max = double.NegativeInfinity;
            for (int k = 0; k < 8; k++)
            {
                Vector3 p = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z);
                max = Math.Max(max, Vector3.Dot(p - com, down));
            }
            return max;
        }

        private static void Log(string message) => UnityEngine.Debug.Log("[KspBridge] land " + message);

        private static string F(double x, int digits) => x.ToString("F" + digits, CultureInfo.InvariantCulture);

        public Dictionary<string, object> Save() => new Dictionary<string, object>();

        public void Load(Vessel v, Dictionary<string, object> state)
        {
        }
    }
}
