using KspBridge.Mod.Modules.Flight;
using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Ascent
{
    /// <summary>One engine module as the staging rule sees it (spec terms).</summary>
    internal sealed class EngineInfo
    {
        /// <summary>A running engine: ModuleEngines.isOperational (ignited and not flamed out).</summary>
        public bool Operational;

        /// <summary>A flamed-out engine: ModuleEngines.flameout.</summary>
        public bool FlameOut;

        /// <summary>ModuleEngines.EngineIgnited.</summary>
        public bool Ignited;

        /// <summary>The stage that ignites the engine (Part.inverseStage).</summary>
        public int Stage;

        /// <summary>The stage that drops the engine (Part.separationIndex); -1 when it is never dropped.</summary>
        public int Separation;

        /// <summary>Every propellant the delta-v calculator counts reaches the engine (spec terms, D009, D014).</summary>
        public bool Propellant;

        /// <summary>Staging is on for the part (Part.stagingOn).</summary>
        public bool StagingOn;
    }

    /// <summary>One stage of the game's delta-v calculator.</summary>
    internal struct StageBurn
    {
        public int Stage;
        public double StartMass;
        public double EndMass;
        public double DvVac;
        public double BurnTime;
    }

    /// <summary>The values of the active vessel the ascent rules read on one physics frame. World vectors in one frame.</summary>
    internal sealed class AscentReading
    {
        public double UT;
        public double Altitude;

        /// <summary>Atmosphere top (sea level, m); null without an atmosphere.</summary>
        public double? AtmosphereTop;

        public double BodyRadius;
        public double Gm;

        /// <summary>Sphere of influence radius (m); infinity when unbounded.</summary>
        public double SoiRadius = double.PositiveInfinity;

        /// <summary>Apoapsis altitude (m); infinity when the orbit is open.</summary>
        public double ApA;

        public double PeA;
        public double Eccentricity;
        public double SemiMajorAxis;
        public double Inclination;
        public double TimeToAp;
        public double VerticalSpeed;
        public double Latitude;

        /// <summary>Landed, splashed or on the launch pad.</summary>
        public bool OnSurface;

        public Vec Up;
        public Vec North;
        public Vec East;
        public Vec OrbitVelocity;
        public Vec SurfaceVelocity;
        public Vec Nose;

        public double DynamicPressureKpa;

        /// <summary>Sum of the engines' current thrust (kN).</summary>
        public double Thrust;

        public double Mass;

        /// <summary>Sum of the running engines' vacuum thrust at full throttle with the thrust limiter (kN).</summary>
        public double MaxThrustVac;

        /// <summary>get_flight's next stage; null when no stage is left.</summary>
        public int? NextStage;

        public List<EngineInfo> Engines = new List<EngineInfo>();

        /// <summary>The delta-v calculator's stages, highest first; null when it is not ready.</summary>
        public List<StageBurn> Stages;

        public bool InAtmosphere => AtmosphereTop.HasValue && Altitude < AtmosphereTop.Value;
    }
}
