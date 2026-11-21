using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace KspBridge.Mod.Modules.Debug
{
    /// <summary>Game values of a celestial body that the orbit request checks against (read by DebugModule).</summary>
    public sealed class OrbitBody
    {
        public string Name;

        /// <summary>Index in FlightGlobals.Bodies (SetShipOrbit's selBodyIndex).</summary>
        public int Index;

        public double Radius;

        /// <summary>Atmosphere top above sea level; null when the body has no atmosphere.</summary>
        public double? AtmosphereTop;

        /// <summary>Highest terrain above sea level (PQS radiusMax − radius); null when the game cannot tell (no PQS built).</summary>
        public double? TerrainMax;

        /// <summary>Sphere of influence radius from the body's centre; infinite for the Sun.</summary>
        public double SphereOfInfluence;
    }

    /// <summary>
    /// debug_set_orbit arguments, checked and turned into the orbit SetShipOrbit takes (pure). Ap and Pe are altitudes above sea
    /// level; the orbit starts at periapsis (mean anomaly 0) with LAN and argument of periapsis 0.
    /// </summary>
    public sealed class OrbitRequest
    {
        /// <summary>SetShipOrbit clamps the semi-major axis to this share of the sphere of influence.</summary>
        public const double SoiClamp = 0.99;

        /// <summary>Applied Ap/Pe must match within this many metres.</summary>
        public const double MatchTolerance = 1000;

        public OrbitBody Body { get; private set; }
        public double Apoapsis { get; private set; }
        public double Periapsis { get; private set; }
        public double Inclination { get; private set; }
        public double SemiMajorAxis { get; private set; }
        public double Eccentricity { get; private set; }

        /// <summary>Checks the arguments; <paramref name="currentBody"/> is used when no body is given. Bad input is bad_request.</summary>
        public static OrbitRequest Parse(Dictionary<string, object> args, IList<OrbitBody> bodies, string currentBody)
        {
            object rawBody = args.TryGetValue("body", out object b) ? b : null;
            if (rawBody != null && !(rawBody is string))
                throw BridgeException.BadRequest("'body' must be a celestial body's internal name (text), e.g. Kerbin or Mun");
            string name = (string)rawBody ?? currentBody;
            OrbitBody body = null;
            foreach (OrbitBody candidate in bodies)
            {
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    body = candidate;
                    break;
                }
            }
            if (body == null)
                throw BridgeException.BadRequest("no celestial body named '" + name + "'");

            double ap = Finite(args.Num("apoapsis_m", required: true).Value, "apoapsis_m");
            double pe = Finite(args.Num("periapsis_m", required: true).Value, "periapsis_m");
            double inc = Finite(args.Num("inclination_deg") ?? 0, "inclination_deg");
            if (ap < pe)
                throw BridgeException.BadRequest("apoapsis_m (" + F(ap) + ") must not be below periapsis_m (" + F(pe) + ")");

            if (body.AtmosphereTop.HasValue)
            {
                if (pe <= body.AtmosphereTop.Value)
                    throw BridgeException.BadRequest("periapsis_m " + F(pe) + " is inside " + body.Name + "'s atmosphere (top " +
                                                     F(body.AtmosphereTop.Value) + " m)");
            }
            else if (!body.TerrainMax.HasValue)
            {
                throw BridgeException.BadRequest("the highest terrain of " + body.Name + " is not known to the game now, so the " +
                                                 "periapsis cannot be checked against it");
            }
            else if (pe <= body.TerrainMax.Value)
            {
                throw BridgeException.BadRequest("periapsis_m " + F(pe) + " is not above " + body.Name + "'s highest terrain (" +
                                                 F(body.TerrainMax.Value) + " m)");
            }

            double rA = body.Radius + ap;
            double rP = body.Radius + pe;
            double sma = (rA + rP) / 2;
            bool finiteSoi = !double.IsInfinity(body.SphereOfInfluence) && !double.IsNaN(body.SphereOfInfluence);
            if (finiteSoi && rA >= body.SphereOfInfluence)
                throw BridgeException.BadRequest("apoapsis_m " + F(ap) + " is outside " + body.Name + "'s sphere of influence (" +
                                                 F(body.SphereOfInfluence - body.Radius) + " m above sea level)");
            if (finiteSoi && sma > SoiClamp * body.SphereOfInfluence)
                throw BridgeException.BadRequest("the semi-major axis " + F(sma) + " m is above " + SoiClamp.ToString(CultureInfo.InvariantCulture) +
                                                 " of " + body.Name + "'s sphere of influence; the game would cut the orbit");

            return new OrbitRequest
            {
                Body = body,
                Apoapsis = ap,
                Periapsis = pe,
                Inclination = inc,
                SemiMajorAxis = sma,
                Eccentricity = (rA - rP) / (rA + rP),
            };
        }

        /// <summary>The vessel shows the requested orbit: same body, ORBITING, Ap and Pe within <see cref="MatchTolerance"/>.</summary>
        public bool IsApplied(string bodyName, string situation, double apA, double peA)
            => string.Equals(bodyName, Body.Name, StringComparison.Ordinal)
               && situation == "ORBITING"
               && Math.Abs(apA - Apoapsis) <= MatchTolerance
               && Math.Abs(peA - Periapsis) <= MatchTolerance;

        private static double Finite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw BridgeException.BadRequest("'" + name + "' must be a finite number");
            return value;
        }

        private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
