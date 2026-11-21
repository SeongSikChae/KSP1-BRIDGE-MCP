using System.Collections.Generic;

namespace KspBridge.Mod.Modules.Check
{
    /// <summary>
    /// Decides in which stages each engine burns (D065, D067). An engine burns in stage s when the calculator's stage s has a
    /// burn segment with that engine whose end mass is below its start mass, i.e. the calculator gave the segment delta-v.
    /// A spent engine stays active in the next stage with a floating-point leftover burn, but that segment's mass does not
    /// drop, so it does not count. The segments always belong to the check's own calculation because the runner clears
    /// them before asking for it (D074).
    /// </summary>
    internal static class BurnSignal
    {
        public static void Fill(ShipData ship)
        {
            foreach (EngineData engine in ship.Engines)
                engine.BurnStages.Clear();
            foreach (KeyValuePair<int, StageCalc> stage in ship.Stages)
            {
                foreach (BurnSegment segment in stage.Value.Segments)
                {
                    if (!(segment.EndMass < segment.StartMass))
                        continue;
                    foreach (EngineData engine in ship.Engines)
                    {
                        if (segment.Engines.Contains(engine.PartId))
                            engine.BurnStages.Add(stage.Key);
                    }
                }
            }
        }
    }
}
