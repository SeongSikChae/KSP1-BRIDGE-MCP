using KspBridge.Mod.Core;
using System;
using System.Globalization;

namespace KspBridge.Mod.Modules.Maneuver
{
    /// <summary>How the game ended a conic segment (Orbit.patchEndTransition), as far as the node time rule cares.</summary>
    internal enum SegmentEnd
    {
        /// <summary>FINAL: no transition; the game puts nodes past its EndUT on it too.</summary>
        Final,

        /// <summary>ESCAPE: leaves the sphere of influence at EndUT.</summary>
        Escape,

        /// <summary>ENCOUNTER: enters another body's sphere of influence at EndUT.</summary>
        Encounter,

        /// <summary>Any other value (INITIAL, MANEUVER, IMPACT): not a bound for a new node.</summary>
        Other,
    }

    /// <summary>The reference orbit of a new node (spec terms "기준 궤도").</summary>
    internal sealed class ReferenceOrbit
    {
        public double Eccentricity;

        /// <summary>Orbital period (s); unused for an open orbit.</summary>
        public double Period;

        /// <summary>The game's time since periapsis at a UT (Orbit.getObtAtUT).</summary>
        public Func<double, double> ObtAt;

        public SegmentEnd End;
        public double EndUt;
    }

    /// <summary>The time rule of create_node (spec C2, D013) without the game.</summary>
    internal static class NodeTime
    {
        /// <summary>The UT of the new node; bad_request with the reason when it cannot be.</summary>
        /// <param name="lastNodeUt">The UT of the last node; null without nodes.</param>
        /// <param name="onSurface">The active vessel is landed, splashed or on the launch pad.</param>
        public static double Resolve(CreateNodeArgs args, double now, double? lastNodeUt, bool onSurface, ReferenceOrbit orbit)
        {
            if (onSurface)
                throw BridgeException.BadRequest("the vessel is landed, splashed or on the launch pad; nodes need an orbit");
            double ut;
            switch (args.Time)
            {
                case NodeTimeKind.Ut:
                    ut = args.TimeValue;
                    break;
                case NodeTimeKind.InSeconds:
                    ut = now + args.TimeValue;
                    break;
                default:
                    ut = Point(args.Time == NodeTimeKind.Apoapsis, Math.Max(now, lastNodeUt ?? now), orbit);
                    break;
            }
            if (!(ut > now))
                throw BridgeException.BadRequest("the node time " + Text(ut) + " is not after now (" + Text(now) + ")");
            if (lastNodeUt.HasValue && !(ut > lastNodeUt.Value))
                throw BridgeException.BadRequest("the node time " + Text(ut) + " is not after the last node (" + Text(lastNodeUt.Value) +
                                                 "); nodes are added after the last one");
            if ((orbit.End == SegmentEnd.Escape || orbit.End == SegmentEnd.Encounter) && !(ut < orbit.EndUt))
                throw BridgeException.BadRequest("the node time " + Text(ut) + " is not before the reference orbit ends at " + Text(orbit.EndUt) +
                                                 (orbit.End == SegmentEnd.Escape ? " (it leaves the sphere of influence)" : " (it enters another body's sphere of influence)"));
            return ut;
        }

        private static double Point(bool apoapsis, double after, ReferenceOrbit orbit)
        {
            double obt = orbit.ObtAt(after);
            double? ut = apoapsis
                ? ConicTimes.NextApoapsis(after, orbit.Eccentricity, orbit.Period, obt)
                : ConicTimes.NextPeriapsis(after, orbit.Eccentricity, orbit.Period, obt);
            if (!ut.HasValue)
                throw BridgeException.BadRequest(apoapsis
                    ? "the reference orbit is open and has no apoapsis"
                    : "the reference orbit is open and its periapsis is already behind");
            return ut.Value;
        }

        private static string Text(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
