using KspBridge.Mod.Core;
using KspBridge.Mod.Modules.Flight;
using System;
using System.Globalization;

namespace KspBridge.Mod.Modules.Maneuver
{
    /// <summary>The body and sphere around a node (spec C2).</summary>
    internal struct GoalBody
    {
        public double Gm;
        public double Radius;

        /// <summary>Sphere of influence radius (m); infinity when unbounded.</summary>
        public double SoiRadius;
    }

    /// <summary>
    /// The delta-v of a create_node goal from the state vectors at the node time (spec C2, plan decision): an apoapsis or periapsis
    /// goal changes only the speed along the velocity (prograde) and is searched by bisection; circular is the circular speed along
    /// the horizontal part of the velocity. A goal the reference orbit cannot reach this way is bad_request.
    /// </summary>
    internal static class NodeGoal
    {
        private const int Steps = 200;

        /// <summary>Apsis differences (m) this small count as equal, so rounding noise on a flat stretch does not steer the search.</summary>
        private const double FlatM = 1e-6;

        /// <summary>The goal's delta-v vector (same frame as <paramref name="r"/> and <paramref name="v"/>).</summary>
        public static Vec DeltaV(NodeGoalKind kind, double targetAltitude, Vec r, Vec v, GoalBody body)
        {
            double rr = r.Length;
            double altitude = rr - body.Radius;
            if (kind == NodeGoalKind.Circular)
                return Circular(r, v, body.Gm);

            double target = targetAltitude + body.Radius;
            bool apoapsis = kind == NodeGoalKind.Apoapsis;
            if (apoapsis && targetAltitude < altitude)
                throw BridgeException.BadRequest("'goal.apoapsis_m' " + Text(targetAltitude) + " is below the altitude at the node (" + Text(altitude) + ")");
            if (apoapsis && !(target < body.SoiRadius))
                throw BridgeException.BadRequest("'goal.apoapsis_m' " + Text(targetAltitude) + " is outside the sphere of influence");
            if (!apoapsis && targetAltitude < 0)
                throw BridgeException.BadRequest("'goal.periapsis_m' must be >= 0 (got " + Text(targetAltitude) + ")");
            if (!apoapsis && targetAltitude > altitude)
                throw BridgeException.BadRequest("'goal.periapsis_m' " + Text(targetAltitude) + " is above the altitude at the node (" + Text(altitude) + ")");

            // The apsis grows with the speed along the velocity but can stay flat (on a horizontal velocity the node point itself is
            // the apoapsis below the circular speed and the periapsis above it), so the search keeps to the side of the current
            // speed the target lies on and takes the root nearest to it: the smallest delta-v.
            Vec along = v.Normalized();
            double speed = v.Length;
            double max = MaxClosedSpeed(r, along, body);
            Func<double, double> f = s => (apoapsis ? Apsides(r, along * s, body.Gm).Apoapsis : Apsides(r, along * s, body.Gm).Periapsis) - target;
            double now = f(speed);
            double lo, hi;
            bool up = now < -FlatM;
            if (up)
            {
                if (!(speed < max) || f(max) < 0)
                    throw BridgeException.BadRequest("the reference orbit cannot reach that " + (apoapsis ? "apoapsis" : "periapsis") +
                                                     " with prograde delta-v alone at the node");
                lo = speed;
                hi = max;
            }
            else
            {
                // Speed 0 falls straight down: apoapsis = r ≤ target, periapsis = 0 ≤ target, so the root lies below.
                lo = 0;
                hi = speed;
            }
            for (int i = 0; i < Steps && hi - lo > 1e-12; i++)
            {
                double mid = (lo + hi) / 2;
                double fm = f(mid);
                if (up ? fm < -FlatM : fm <= FlatM)
                    lo = mid;
                else
                    hi = mid;
            }
            return along * ((lo + hi) / 2) - v;
        }

        /// <summary>Apoapsis (infinity when open) and periapsis radii of the orbit through <paramref name="r"/> with velocity <paramref name="v"/>.</summary>
        public static (double Apoapsis, double Periapsis) Apsides(Vec r, Vec v, double gm)
        {
            // The eccentricity vector keeps its precision near a circular orbit (1 + 2εh²/μ² cancels to ~1e-16, e ~ 1e-8).
            double rr = r.Length;
            double v2 = Vec.Dot(v, v);
            Vec ev = (r * (v2 - gm / rr) - v * Vec.Dot(r, v)) * (1 / gm);
            double e = ev.Length;
            double h = NodeFrame.Cross(r, v).Length;
            double peri = h * h / gm / (1 + e);
            double energy = v2 / 2 - gm / rr;
            if (!(energy < 0))
                return (double.PositiveInfinity, peri);
            double a = -gm / (2 * energy);
            return (a * (1 + e), peri);
        }

        /// <summary>The highest speed along <paramref name="along"/> that keeps the orbit closed and its apoapsis inside the sphere.</summary>
        private static double MaxClosedSpeed(Vec r, Vec along, GoalBody body)
        {
            double escape = Math.Sqrt(2 * body.Gm / r.Length) * (1 - 1e-12);
            if (double.IsInfinity(body.SoiRadius) || Apsides(r, along * escape, body.Gm).Apoapsis < body.SoiRadius)
                return escape;
            double lo = 0;
            double hi = escape;
            for (int i = 0; i < Steps && hi - lo > 1e-12; i++)
            {
                double mid = (lo + hi) / 2;
                if (Apsides(r, along * mid, body.Gm).Apoapsis < body.SoiRadius)
                    lo = mid;
                else
                    hi = mid;
            }
            return lo;
        }

        private static Vec Circular(Vec r, Vec v, double gm)
        {
            Vec up = r.Normalized();
            Vec horizontal = v - up * Vec.Dot(v, up);
            if (horizontal.Length < 1e-6)
                throw BridgeException.BadRequest("the velocity at the node has no horizontal part to circularize along");
            return horizontal.Normalized() * Math.Sqrt(gm / r.Length) - v;
        }

        private static string Text(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
