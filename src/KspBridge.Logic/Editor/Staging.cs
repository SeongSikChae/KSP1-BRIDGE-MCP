using KSP.UI.Screens;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Editor
{
    /// <summary>The editor staging list (get_craft.stages, set_stage). Main thread only.</summary>
    public static class Staging
    {
        /// <summary>
        /// The staging list as the editor shows it: every stage, the one that fires first (highest inverse stage) first, with the
        /// ids of its parts. A collapsed symmetry group is one icon in the stage; its grouped parts are listed with it.
        /// </summary>
        public static List<object> List()
        {
            StageManager sm = StageManager.Instance;
            if (sm == null)
            {
                return new List<object>();
            }
            return sm.Stages.Where(g => g != null).OrderByDescending(g => g.inverseStageIndex).Select(g => (object)new Dictionary<string, object>
            {
                ["stage"] = g.inverseStageIndex,
                ["parts"] = g.Icons.Where(i => i != null)
                    .SelectMany(i => new[] { i }.Concat(i.groupLead == i ? i.groupedIcons.Where(x => x != null) : Enumerable.Empty<StageIcon>()))
                    .Where(i => i.Part != null).Select(i => (object)i.Part.persistentId).Distinct().ToList(),
            }).ToList();
        }

        /// <summary>
        /// Moves a part's staging icon (its whole symmetry group when grouped) to an existing stage, or to a new stage inserted
        /// at newStageAt, the way the editor does it when an icon is dragged onto another stage (StageIcon.OnEndDrag) and a stage
        /// is added (StageGroup.AddStageAfter).
        /// </summary>
        public static object Move(uint id, int? stage, int? newStageAt)
        {
            ShipConstruct ship = EditorScene.Require();
            Part p = PartIndex.Find(ship, id);
            StageManager sm = StageManager.Instance
                ?? throw new ApiException(409, "staging_unavailable", "the staging list is not ready");
            StageIcon icon = p.stackIcon?.StageIcon;
            if (!p.hasStagingIcon || icon == null)
            {
                throw ApiException.BadRequest(p.partInfo.name + " has no staging icon");
            }
            if (stage.HasValue == newStageAt.HasValue)
            {
                throw ApiException.BadRequest("give either stage (an existing stage) or new_stage_at (insert a new stage there)");
            }
            if (stage.HasValue && (stage.Value < 0 || stage.Value >= sm.Stages.Count))
            {
                throw ApiException.BadRequest("stage must be 0.." + (sm.Stages.Count - 1));
            }
            if (newStageAt.HasValue && (newStageAt.Value < 0 || newStageAt.Value > sm.Stages.Count))
            {
                throw ApiException.BadRequest("new_stage_at must be 0.." + sm.Stages.Count);
            }
            // A collapsed symmetry group is dragged by its lead and moves as a whole.
            if (icon.groupLead != null)
            {
                icon = icon.groupLead;
            }
            StageGroup old = icon.Stage
                ?? throw new ApiException(409, "staging_unavailable", p.partInfo.name + "'s icon is not in a stage");

            if (newStageAt.HasValue)
            {
                int i = newStageAt.Value;
                sm.IncrementCurrentStage();
                sm.AddStageAt(i);
                sm.SetManualStageOffset(i);
                StageManager.SetSeparationIndices();
                GameEvents.StageManager.OnGUIStageAdded.Fire(i);
            }
            StageGroup target = sm.Stages[newStageAt ?? stage.Value];

            if (target == old)
            {
                // Same stage: nothing changes, so no Undo step either.
                return new Dictionary<string, object> { ["stages"] = List() };
            }
            old.RemoveIcon(icon, changeParents: false);
            // Last in the stage on screen too: the stage's UI list also holds other groups' grouped icons.
            target.AddIconAt(icon, target.Icons.Count, target.IconsUIList.childCount);
            icon.SetManualStageOffset(icon.Stage.inverseStageIndex);
            old.UpdateInStageIndexes();
            target.UpdateInStageIndexes();
            if (old.Icons.Count == 0)
            {
                // The editor sorts two frames later (instant: false). Sorting now runs the same DeleteEmptyStages before the
                // Undo step and the returned list, so the response shows the final stages. Like the editor, it removes an
                // emptied stage only if the game created it (defaultStage); added stages stay, empty.
                sm.SortIcons(instant: true);
            }
            else
            {
                StageManager.SetSeparationIndices();
            }
            GameEvents.StageManager.OnGUIStageSequenceModified.Fire();
            EditorScene.Commit(ship);
            return new Dictionary<string, object> { ["stages"] = List() };
        }
    }
}
