using KspBridge.Mod.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Editor
{
    /// <summary>A staged part: its stage (inverseStage, higher fires first), index in the stage, and symmetry group.</summary>
    public sealed class StageEntry
    {
        public StageEntry(uint id, int stage, int index, uint group)
        {
            Id = id;
            Stage = stage;
            Index = index;
            Group = group;
        }

        public uint Id { get; }
        public int Stage { get; set; }
        public int Index { get; set; }

        /// <summary>Same value for the members of one symmetry group (e.g. the smallest craftID); the id itself when alone.</summary>
        public uint Group { get; }
    }

    /// <summary>
    /// Stage rules on staged parts only (parts with a staging icon that is switched on); other parts' stage numbers are the
    /// game's. Numbers are the game's inverseStage: 0 fires last, the highest first. KSP-independent and unit tested.
    /// </summary>
    public static class Staging
    {
        /// <summary>Stages, highest (first to fire) first, each with its parts in index order.</summary>
        public static List<KeyValuePair<int, List<StageEntry>>> Group(IEnumerable<StageEntry> entries)
            => entries.GroupBy(e => e.Stage)
                .OrderByDescending(g => g.Key)
                .Select(g => new KeyValuePair<int, List<StageEntry>>(g.Key, g.OrderBy(e => e.Index).ThenBy(e => e.Id).ToList()))
                .ToList();

        /// <summary>Number of a new stage that fires first: the highest existing + 1, or 0 when there are no stages.</summary>
        public static int NewStage(IEnumerable<StageEntry> entries)
        {
            List<int> stages = entries.Select(e => e.Stage).ToList();
            return stages.Count == 0 ? 0 : stages.Max() + 1;
        }

        /// <summary>A stage number a part may be put in: an existing stage or the new first one; bad_request otherwise.</summary>
        public static int RequireTarget(IEnumerable<StageEntry> entries, double? requested)
        {
            List<StageEntry> list = entries.ToList();
            int fresh = NewStage(list);
            if (!requested.HasValue)
                return fresh;
            double value = requested.Value;
            if (double.IsNaN(value) || value != Math.Floor(value))
                throw BridgeException.BadRequest("stage must be an integer");
            int stage = (int)value;
            if (stage != fresh && !list.Any(e => e.Stage == stage))
                throw BridgeException.BadRequest("stage " + stage + " does not exist; use an existing stage ("
                    + Describe(list) + ") or " + fresh + " for a new stage that fires first");
            return stage;
        }

        /// <summary>Moves the parts with <paramref name="ids"/> to <paramref name="stage"/> (already checked).</summary>
        public static void Move(IList<StageEntry> entries, ICollection<uint> ids, int stage)
        {
            int index = entries.Where(e => e.Stage == stage && !ids.Contains(e.Id)).Select(e => e.Index).DefaultIfEmpty(-1).Max() + 1;
            foreach (StageEntry e in entries.Where(e => ids.Contains(e.Id)))
            {
                e.Stage = stage;
                e.Index = index;
            }
        }

        /// <summary>Exchanges two existing, different stages; bad_request otherwise.</summary>
        public static void Swap(IList<StageEntry> entries, double a, double b)
        {
            int sa = RequireExisting(entries, a, "a");
            int sb = RequireExisting(entries, b, "b");
            if (sa == sb)
                throw BridgeException.BadRequest("a and b are the same stage");
            foreach (StageEntry e in entries)
            {
                if (e.Stage == sa)
                    e.Stage = sb;
                else if (e.Stage == sb)
                    e.Stage = sa;
            }
        }

        private static int RequireExisting(IEnumerable<StageEntry> entries, double value, string name)
        {
            if (double.IsNaN(value) || value != Math.Floor(value))
                throw BridgeException.BadRequest(name + " must be an integer");
            int stage = (int)value;
            List<StageEntry> list = entries.ToList();
            if (!list.Any(e => e.Stage == stage))
                throw BridgeException.BadRequest("stage " + stage + " does not exist (" + Describe(list) + ")");
            return stage;
        }

        /// <summary>
        /// Renumbers stages 0.. without gaps (order kept) and indexes inside each stage 0.. by the old order; members of one
        /// symmetry group share an index, as the game numbers them.
        /// </summary>
        public static void Normalize(IList<StageEntry> entries)
        {
            List<int> stages = entries.Select(e => e.Stage).Distinct().OrderBy(s => s).ToList();
            foreach (StageEntry e in entries)
                e.Stage = stages.IndexOf(e.Stage);
            foreach (IGrouping<int, StageEntry> stage in entries.GroupBy(e => e.Stage))
            {
                List<uint> groups = stage.OrderBy(e => e.Index).ThenBy(e => e.Id).Select(e => e.Group).Distinct().ToList();
                foreach (StageEntry e in stage)
                    e.Index = groups.IndexOf(e.Group);
            }
        }

        /// <summary>
        /// How the settled ship's stages differ from the edited craft, or null. Only the game knows which new parts are
        /// staged (it decides when it starts them), so new parts it did not stage are dropped from <paramref name="expected"/>
        /// and the remaining stages are compared by order (both renumbered 0..), which also allows the game deleting a stage
        /// that only held such a part. A new part the request put into an explicit stage must be staged.
        /// </summary>
        /// <param name="expected">Stage of every part written as staged (existing staged parts and all new parts).</param>
        /// <param name="live">Stage of every part the game staged.</param>
        /// <param name="added">New parts of the edit.</param>
        /// <param name="requested">New parts with an explicitly requested stage.</param>
        public static string StageMismatch(IDictionary<uint, int> expected, IDictionary<uint, int> live, ICollection<uint> added, ICollection<uint> requested)
        {
            foreach (uint id in requested.OrderBy(x => x))
            {
                if (!live.ContainsKey(id))
                    return "part " + id + " is not staged (the game gave it no stage), so stage cannot be set for it";
            }
            foreach (uint id in live.Keys.OrderBy(x => x))
            {
                if (!expected.ContainsKey(id))
                    return "part " + id + " is staged but was not expected to be";
            }
            Dictionary<uint, int> kept = expected.Where(e => live.ContainsKey(e.Key) || !added.Contains(e.Key)).ToDictionary(e => e.Key, e => e.Value);
            foreach (uint id in kept.Keys.OrderBy(x => x))
            {
                if (!live.ContainsKey(id))
                    return "part " + id + " is no longer staged";
            }
            Dictionary<uint, int> wanted = Rank(kept);
            Dictionary<uint, int> actual = Rank(live);
            foreach (uint id in wanted.Keys.OrderBy(x => x))
            {
                if (wanted[id] != actual[id])
                    return "part " + id + " is in stage " + live[id] + " instead of stage " + wanted[id] + " (counted 0.. over the staged parts)";
            }
            return null;
        }

        /// <summary>Stage numbers replaced by their rank among the distinct stages (0 = lowest).</summary>
        private static Dictionary<uint, int> Rank(IDictionary<uint, int> stages)
        {
            List<int> order = stages.Values.Distinct().OrderBy(s => s).ToList();
            return stages.ToDictionary(s => s.Key, s => order.IndexOf(s.Value));
        }

        /// <summary>Why <paramref name="entries"/> are not normalized, or null when they are.</summary>
        public static string NormalizationError(IEnumerable<StageEntry> entries)
        {
            List<StageEntry> list = entries.ToList();
            List<int> stages = list.Select(e => e.Stage).Distinct().OrderBy(s => s).ToList();
            for (int i = 0; i < stages.Count; i++)
            {
                if (stages[i] != i)
                    return "stages are not numbered 0.." + (stages.Count - 1) + " without gaps";
            }
            foreach (IGrouping<int, StageEntry> stage in list.GroupBy(e => e.Stage))
            {
                if (stage.GroupBy(e => e.Group).Any(g => g.Select(e => e.Index).Distinct().Count() > 1))
                    return "symmetry partners in stage " + stage.Key + " have different indexes";
                List<int> indexes = stage.GroupBy(e => e.Group).Select(g => g.First().Index).OrderBy(x => x).ToList();
                for (int i = 0; i < indexes.Count; i++)
                {
                    if (indexes[i] != i)
                        return "parts in stage " + stage.Key + " are not indexed 0.." + (indexes.Count - 1) + " (one index per symmetry group)";
                }
            }
            return null;
        }

        // ---- live ship (KSP) ----

        /// <summary>The game's own judgement of a staged part (StageManager.setSeparationIndices).</summary>
        public static bool IsStagedLive(Part part)
            => (part.hasStagingIcon || (part.stackIcon != null && part.stackIcon.StageIcon != null)) && part.stagingOn;

        /// <summary>craftID → staged, for every part of the ship.</summary>
        public static Dictionary<uint, bool> StagedMap(ShipConstruct ship)
            => ship.parts.ToDictionary(p => p.craftID, IsStagedLive);

        /// <summary>craftID → stage of the staged parts.</summary>
        public static Dictionary<uint, int> LiveStages(ShipConstruct ship)
            => ship.parts.Where(IsStagedLive).ToDictionary(p => p.craftID, p => p.inverseStage);

        /// <summary>editor_get_stages: stages first to fire first, each with its staged parts.</summary>
        public static object Describe(ShipConstruct ship)
        {
            Dictionary<uint, Part> byId = ship.parts.ToDictionary(p => p.craftID);
            List<StageEntry> entries = ship.parts.Where(IsStagedLive)
                .Select(p => new StageEntry(p.craftID, p.inverseStage, p.inStageIndex, p.craftID))
                .ToList();
            return new Dictionary<string, object>
            {
                ["stages"] = Group(entries).Select(g => (object)new Dictionary<string, object>
                {
                    ["stage"] = (double)g.Key,
                    ["parts"] = g.Value.Select(e => (object)new Dictionary<string, object>
                    {
                        ["id"] = (double)e.Id,
                        ["name"] = byId[e.Id].partInfo?.name,
                        ["title"] = byId[e.Id].partInfo?.title,
                    }).ToList(),
                }).ToList(),
            };
        }

        private static string Describe(IEnumerable<StageEntry> entries)
        {
            List<int> stages = entries.Select(e => e.Stage).Distinct().OrderBy(s => s).ToList();
            return stages.Count == 0 ? "there are no stages" : "stages " + string.Join(", ", stages.Select(s => s.ToString()).ToArray());
        }
    }
}
