using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Flight
{
    /// <summary>One stage from the game's delta-v calculator.</summary>
    public sealed class StageCalc
    {
        public int Stage;
        public double StartMass;
        public double EndMass;
        public double DvActual;
        public double DvAsl;
        public double DvVac;
        public double TwrActual;
        public double TwrAsl;
        public double TwrVac;
        public double BurnTime;
    }

    public sealed class ResourceTotal
    {
        public string Name;
        public double Amount;
        public double Max;
    }

    public sealed class BodyData
    {
        public string Name;
        public double Radius;
        public double Gm;
        public double RotationPeriod;

        /// <summary>Null when the body has no atmosphere.</summary>
        public double? AtmosphereTop;

        /// <summary>Infinite for the Sun.</summary>
        public double SphereOfInfluence;
    }

    /// <summary>The game's time warp and whether the vessel is on rails (out of the physics).</summary>
    public sealed class WarpState
    {
        /// <summary>Current rate, also while the game is changing it; 1 without warp.</summary>
        public double Rate = 1;

        /// <summary>The game's physics warp mode (otherwise rails).</summary>
        public bool Physics;

        public bool OnRails;
    }

    /// <summary>The active vessel's state read from the game (<see cref="FlightSnapshot"/>), with no game types in it.</summary>
    public sealed class FlightData
    {
        public string Vessel;
        public string Situation;
        public string Body;
        public double UT;
        public double MissionTime;

        public double Latitude;
        public double Longitude;
        public double Altitude;

        /// <summary>Terrain height above sea level under the vessel (TerrainAltitude with negatives kept).</summary>
        public double TerrainHeight;

        /// <summary>The body has an ocean, so terrain below sea level is under water (height 0 for the surface).</summary>
        public bool Ocean;

        public double SurfaceSpeed;
        public double OrbitalSpeed;
        public double VerticalSpeed;
        public double HorizontalSurfaceSpeed;

        public double ApA;
        public double PeA;
        public double Inclination;
        public double Eccentricity;
        public double SemiMajorAxis;
        public double Period;
        public double TimeToAp;
        public double TimeToPe;

        public Vec Up;
        public Vec North;
        public Vec East;
        public Vec Nose;
        public Vec Top;

        public WarpState Warp = new WarpState();

        public double Throttle;
        public bool Sas;
        public bool Rcs;

        public double Mass;
        public double Thrust;

        /// <summary>Gravity at the vessel's centre of mass (m/s²).</summary>
        public double Gravity;

        public int CurrentStage;
        public List<StagedPart> StagedParts = new List<StagedPart>();

        public bool DeltaVReady;
        public string DeltaVNotReadyReason;
        public List<StageCalc> Stages = new List<StageCalc>();
        public double TotalDvActual;
        public double TotalDvVac;

        public List<ResourceTotal> Resources = new List<ResourceTotal>();
        public BodyData BodyInfo;
    }
}
