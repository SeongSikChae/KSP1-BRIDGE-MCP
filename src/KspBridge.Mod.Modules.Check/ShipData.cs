using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Check
{
    /// <summary>One resource carried by a part: current amount and density (t per unit).</summary>
    public sealed class ResourceData
    {
        public string Name;
        public double Amount;
        public double Density;

        public double Mass => Amount * Density;
    }

    /// <summary>The calculator's resource mass of one part at the start and end of a stage (all simulated resources, t).</summary>
    public struct StageMass
    {
        public StageMass(double start, double end)
        {
            Start = start;
            End = end;
        }

        public double Start;
        public double End;
    }

    /// <summary>A staged separator: when its stage fires, the subtree of each detached root falls away.</summary>
    public sealed class SeparatorData
    {
        public int Stage;

        /// <summary>Roots of the parts that fall away; the separator itself when it detaches from its parent.</summary>
        public List<uint> DetachedRoots = new List<uint>();

        /// <summary>
        /// The roots a separator detaches, as the game separates (D077, D085): an omni decoupler drops every child and, when it
        /// has a parent, itself (ModuleDecouple); otherwise the part on the separating node falls, which is the separator itself
        /// when that part is its parent, and nothing falls when the node is empty (ModuleDecouple, ModuleAnchoredDecoupler,
        /// ModuleDockingNode.Decouple).
        /// </summary>
        public static List<uint> DetachedSide(bool omni, uint self, uint? parent, uint? nodePart, IEnumerable<uint> children)
        {
            List<uint> roots = new List<uint>();
            if (omni)
            {
                roots.AddRange(children);
                if (parent.HasValue)
                    roots.Add(self);
            }
            else if (nodePart.HasValue)
            {
                roots.Add(nodePart == parent ? self : nodePart.Value);
            }
            return roots;
        }
    }

    /// <summary>A part as the checks need it (no KSP types).</summary>
    public sealed class PartData
    {
        public uint Id;
        public uint? Parent;
        public bool LaunchClamp;

        /// <summary>part.mass: dry mass including modules not yet jettisoned.</summary>
        public double Mass;

        /// <summary>Mass centre in world coordinates (non-physical parts use their parent's position, as the game's CoM marker).</summary>
        public Vec Com;

        public List<ResourceData> Resources = new List<ResourceData>();

        /// <summary>The calculator has part info for this part (launch clamps have none).</summary>
        public bool HasCalc;

        public int DecoupleStage;
        public int ActivationStage;
        public bool IsJettison;
        public double JettisonMass;

        /// <summary>Calculator resource mass per stage, only for stages the calculator recorded (decoupleStage &lt;= stage).</summary>
        public Dictionary<int, StageMass> StageResources = new Dictionary<int, StageMass>();

        /// <summary>Staged separator (the part is staged by the editor_get_stages rule and has a separator module), else null.</summary>
        public SeparatorData Separator;

        public bool Command;
        public bool Parachute;
        public int CrewCapacity;

        /// <summary>The calculator's JettisonInStage(stage).</summary>
        public bool JettisonInStage(int stage) => HasCalc && IsJettison && DecoupleStage < stage && ActivationStage >= stage;
    }

    /// <summary>An operating engine from the calculator.</summary>
    public sealed class EngineData
    {
        public uint PartId;

        /// <summary>The part is staged (editor_get_stages rule) and its stage number (inverseStage).</summary>
        public bool Staged;
        public int Stage;

        /// <summary>
        /// Stages where the engine burns fuel: the calculator's stage has a burn segment with this engine whose end mass is
        /// below its start mass (D065, filled by <see cref="BurnSignal"/>).
        /// </summary>
        public HashSet<int> BurnStages = new HashSet<int>();

        public List<string> Propellants = new List<string>();

        /// <summary>Thrust point (world) and exhaust direction (unit); the force acts along -ExhaustDir.</summary>
        public Vec ThrustPoint;
        public Vec ExhaustDir;

        /// <summary>Calculator thrust with the thrust limiter (kN); vacuum is 0 for air-breathing engines.</summary>
        public double ThrustVac;
        public double ThrustAsl;

        /// <summary>Smallest range of the enabled gimbal axes × limiter (degrees); 0 without a usable gimbal.</summary>
        public double GimbalDeg;
    }

    /// <summary>The calculator's values for one stage.</summary>
    public sealed class StageCalc
    {
        public double StartMass;
        public double EndMass;
        public double DvAsl;
        public double DvVac;
        public double TwrAsl;
        public double TwrVac;
        public double BurnTime;

        /// <summary>The calculator's burn segments of this stage (DeltaVStageInfo.deltaVCalcs), in order.</summary>
        public List<BurnSegment> Segments = new List<BurnSegment>();
    }

    /// <summary>One calculator burn segment (DeltaVCalc): the engines active in it and the stage mass before and after it.</summary>
    public sealed class BurnSegment
    {
        public List<uint> Engines = new List<uint>();
        public double StartMass;
        public double EndMass;
    }

    /// <summary>Everything a check reads, taken from the game at the calculator's completed event.</summary>
    public sealed class ShipData
    {
        public string Body;
        public string Facility;
        public List<PartData> Parts = new List<PartData>();
        public List<EngineData> Engines = new List<EngineData>();

        /// <summary>Calculator stage values by stage number (only where the calculator has a stage).</summary>
        public Dictionary<int, StageCalc> Stages = new Dictionary<int, StageCalc>();

        /// <summary>inverseStage of the staged parts (editor_get_stages numbers).</summary>
        public List<int> StagedNumbers = new List<int>();

        /// <summary>Propellants the calculator ignores (DeltaVGlobals.PropellantsToIgnore names).</summary>
        public HashSet<string> IgnoredPropellants = new HashSet<string>();
    }
}
