using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Flight
{
    /// <summary>A part that has a staging icon (editor_get_stages rule): its stage number and whether it has fired.</summary>
    public readonly struct StagedPart
    {
        public StagedPart(int stage, bool activated)
        {
            Stage = stage;
            Activated = activated;
        }

        public int Stage { get; }

        /// <summary>Part.State is ACTIVE or DEACTIVATED (fired); false while IDLE.</summary>
        public bool Activated { get; }
    }

    public readonly struct StageRefResult
    {
        public StageRefResult(int? lastActivated, int? next)
        {
            LastActivated = lastActivated;
            Next = next;
        }

        public int? LastActivated { get; }
        public int? Next { get; }

        /// <summary>The stage the default answer shows: the last activated stage, otherwise the next one.</summary>
        public int? Reference => LastActivated ?? Next;
    }

    /// <summary>
    /// Last activated / next / reference stage (D019·D020). The game puts every fired icon part in the top stage group
    /// (number currentStage), and a group is deleted only when it has no icons left, so while a fired icon part remains
    /// currentStage is the last activated stage. After that the group may be deleted and currentStage changed, so it is not
    /// used: the last activated stage is then null. The next stage is the highest stage of the icon parts that have not fired.
    /// </summary>
    public static class StageRef
    {
        public static StageRefResult Judge(int currentStage, IEnumerable<StagedPart> stagedParts)
        {
            bool anyActivated = false;
            int? next = null;
            foreach (StagedPart part in stagedParts)
            {
                if (part.Activated)
                    anyActivated = true;
                else if (!next.HasValue || part.Stage > next.Value)
                    next = part.Stage;
            }
            return new StageRefResult(anyActivated ? currentStage : (int?)null, next);
        }
    }
}
