using KspBridge.Mod.Modules.Maneuver;
using System;

namespace KspBridge.Mod.Modules.Warp
{
    /// <summary>The vessel's orbit when a warp step starts.</summary>
    internal sealed class OrbitReading
    {
        public bool LandedOrSplashed;
        public double Eccentricity;
        public double ApoapsisRadius;

        /// <summary>Infinite for the Sun.</summary>
        public double SphereOfInfluence;

        public double TimeToApoapsis;

        /// <summary>Negative after the periapsis of an open orbit.</summary>
        public double TimeToPeriapsis;
    }

    /// <summary>The UT a warp step goes to, decided once at its start (D001), or why it cannot.</summary>
    internal sealed class WarpTarget
    {
        private WarpTarget(double ut, string failure)
        {
            Ut = ut;
            Failure = failure;
        }

        public double Ut { get; }

        /// <summary>Null when there is a target.</summary>
        public string Failure { get; }

        /// <param name="orbit">Asked for only when the goal is a point of the orbit.</param>
        /// <param name="node">Asked for only when the goal is the node: the maneuver module's start checks and burn start (D014).</param>
        public static WarpTarget From(WarpArgs args, double ut, Func<OrbitReading> orbit, Func<NodeStartResult> node = null)
        {
            switch (args.Goal)
            {
                case WarpGoal.Seconds:
                    return At(ut + args.Value);
                case WarpGoal.Ut:
                    return At(args.Value);
                case WarpGoal.Node:
                    return ToNode(args, node());
                default:
                    return ToPoint(args, ut, orbit());
            }
        }

        /// <summary>The burn start − lead_s, or the start check's failure as it is.</summary>
        private static WarpTarget ToNode(WarpArgs args, NodeStartResult start)
            => start.Failure != null ? Fail(start.Failure) : At(start.StartUt - args.Lead);

        private static WarpTarget ToPoint(WarpArgs args, double ut, OrbitReading orbit)
        {
            bool apoapsis = args.Goal == WarpGoal.Apoapsis;
            string name = apoapsis ? "apoapsis" : "periapsis";
            if (orbit.LandedOrSplashed)
                return Fail("the vessel is landed, splashed or on the launch pad, so it has no " + name + " to go to");
            bool open = !(orbit.Eccentricity < 1);
            if (apoapsis && open)
                return Fail("the orbit is open and has no apoapsis");
            if (apoapsis && orbit.ApoapsisRadius >= orbit.SphereOfInfluence)
                return Fail("the apoapsis is outside the sphere of influence");
            if (!apoapsis && open && orbit.TimeToPeriapsis < 0)
                return Fail("the periapsis of the open orbit is already behind");
            double time = apoapsis ? orbit.TimeToApoapsis : orbit.TimeToPeriapsis;
            if (double.IsNaN(time) || double.IsInfinity(time))
                return Fail("the game has no time to the " + name);
            return At(ut + time - args.Lead);
        }

        private static WarpTarget At(double ut) => new WarpTarget(ut, null);

        private static WarpTarget Fail(string why) => new WarpTarget(0, why);
    }
}
