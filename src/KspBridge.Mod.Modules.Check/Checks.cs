using System;
using System.Collections.Generic;
using System.Linq;

namespace KspBridge.Mod.Modules.Check
{
    /// <summary>
    /// Pure judgement of a <see cref="ShipData"/>: the stage table (C2), the per-stage centre-of-mass offset (C6) and the warnings
    /// (C5-C7), built into the check_ship answer (C4). Stage numbers fire from the highest to 0.
    /// </summary>
    public static class Checks
    {
        /// <summary>Masses are judged at the answer's precision (t), so a value the answer shows as 0 never makes a warning (D076).</summary>
        internal const int MassDigits = 3;

        /// <summary>Offsets are judged at the answer's precision (m, D076).</summary>
        internal const int OffsetDigits = 3;

        /// <summary>The answer for a ship without parts (C9): no stages, no warnings.</summary>
        public static Dictionary<string, object> EmptyResult(string body, string facility)
            => Answer(body, facility, new List<object>(), 0, 0, 0, new List<object>());

        public static Dictionary<string, object> Run(ShipData ship)
        {
            Analysis a = new Analysis(ship);
            List<StageRow> rows = a.StageTable();
            List<object> warnings = a.Warnings(rows);
            List<object> stages = rows.Select(r => (object)r.ToAnswer()).ToList();
            // Totals are the sums of the answer's rounded stage values, so adding up the stages gives the total (D078).
            return Answer(ship.Body, ship.Facility, stages,
                SumRounded(rows, r => r.DvAsl), SumRounded(rows, r => r.DvVac), SumRounded(rows, r => r.Burn), warnings);
        }

        private static double SumRounded(List<StageRow> rows, Func<StageRow, double> value)
            => Round(rows.Sum(r => Round(value(r), 1)), 1);

        private static Dictionary<string, object> Answer(string body, string facility, List<object> stages,
            double dvAsl, double dvVac, double burn, List<object> warnings)
            => new Dictionary<string, object>
            {
                ["body"] = body,
                ["facility"] = facility,
                ["stages"] = stages,
                ["total"] = new Dictionary<string, object> { ["dv_asl_ms"] = dvAsl, ["dv_vac_ms"] = dvVac, ["burn_s"] = burn },
                ["warnings"] = warnings,
            };

        internal static double Round(double value, int digits)
            => double.IsNaN(value) || double.IsInfinity(value) ? 0 : Math.Round(value, digits, MidpointRounding.AwayFromZero);
    }

    /// <summary>One row of the stage table before it is written as the answer.</summary>
    internal sealed class StageRow
    {
        public int Stage;
        public bool EngineStage;
        public double StartMass;
        public double EndMass;
        public double FuelMass;
        public double DvAsl;
        public double DvVac;
        public double TwrAsl;
        public double TwrVac;
        public double Burn;
        public double? OffsetM;
        public double? OffsetDeg;

        public Dictionary<string, object> ToAnswer()
        {
            double start = Checks.Round(StartMass, 3);
            double fuel = Checks.Round(FuelMass, 3);
            Dictionary<string, object> d = new Dictionary<string, object>
            {
                ["stage"] = Stage,
                ["start_mass_t"] = start,
                ["end_mass_t"] = Checks.Round(EndMass, 3),
                ["dry_mass_t"] = Checks.Round(start - fuel, 3),
                ["fuel_mass_t"] = fuel,
                ["dv_asl_ms"] = Checks.Round(DvAsl, 1),
                ["dv_vac_ms"] = Checks.Round(DvVac, 1),
                ["twr_asl"] = Checks.Round(TwrAsl, 2),
                ["twr_vac"] = Checks.Round(TwrVac, 2),
                ["burn_s"] = Checks.Round(Burn, 1),
            };
            if (OffsetM.HasValue)
            {
                d["com_offset_m"] = Checks.Round(OffsetM.Value, 3);
                d["com_offset_deg"] = Checks.Round(OffsetDeg.Value, 2);
            }
            return d;
        }
    }

    /// <summary>Where a part falls off: the stage and the separator that drops it (null: stays to the end).</summary>
    internal struct Fall
    {
        public int Stage;
        public uint Cause;
        public int CauseDepth;
    }

    internal sealed class Analysis
    {
        private readonly ShipData ship;
        private readonly Dictionary<uint, PartData> byId;
        private readonly Dictionary<uint, List<PartData>> children = new Dictionary<uint, List<PartData>>();
        private readonly Dictionary<uint, Fall> falls = new Dictionary<uint, Fall>();
        private readonly HashSet<string> fuel;
        private readonly List<int> stageNumbers;
        private readonly HashSet<int> engineStages;

        public Analysis(ShipData ship)
        {
            this.ship = ship;
            byId = ship.Parts.ToDictionary(p => p.Id);
            foreach (PartData p in ship.Parts)
            {
                if (p.Parent.HasValue)
                {
                    if (!children.TryGetValue(p.Parent.Value, out List<PartData> list))
                        children[p.Parent.Value] = list = new List<PartData>();
                    list.Add(p);
                }
            }
            fuel = new HashSet<string>(ship.Engines.SelectMany(e => e.Propellants).Where(n => !ship.IgnoredPropellants.Contains(n)));
            stageNumbers = ship.StagedNumbers.Distinct().OrderByDescending(s => s).ToList();
            engineStages = new HashSet<int>(ship.Engines.SelectMany(e => e.BurnStages));
            ComputeFalls();
        }

        // ---- separation units (D022, D026, D037, D046, D047) ----

        private int Depth(uint id)
        {
            int depth = 0;
            PartData p = byId[id];
            while (p.Parent.HasValue && byId.TryGetValue(p.Parent.Value, out PartData parent))
            {
                depth++;
                p = parent;
            }
            return depth;
        }

        /// <summary>
        /// Each part falls at the highest stage among the staged separators that detach it (the first to fire drops it). On a tie
        /// the separator nearest the part is the cause, so the separation units are the same for every run.
        /// </summary>
        private void ComputeFalls()
        {
            foreach (PartData sep in ship.Parts.Where(p => p.Separator != null && !p.LaunchClamp))
            {
                int depth = Depth(sep.Id);
                foreach (uint root in sep.Separator.DetachedRoots)
                {
                    if (!byId.ContainsKey(root))
                        continue;
                    foreach (PartData q in Subtree(root))
                    {
                        Fall candidate = new Fall { Stage = sep.Separator.Stage, Cause = sep.Id, CauseDepth = depth };
                        if (!falls.TryGetValue(q.Id, out Fall current) || candidate.Stage > current.Stage
                            || (candidate.Stage == current.Stage && candidate.CauseDepth > current.CauseDepth))
                            falls[q.Id] = candidate;
                    }
                }
            }
        }

        private IEnumerable<PartData> Subtree(uint root)
        {
            Stack<PartData> stack = new Stack<PartData>();
            stack.Push(byId[root]);
            while (stack.Count > 0)
            {
                PartData p = stack.Pop();
                yield return p;
                if (children.TryGetValue(p.Id, out List<PartData> kids))
                {
                    foreach (PartData k in kids)
                        stack.Push(k);
                }
            }
        }

        /// <summary>The part is on the ship while stage <paramref name="stage"/> runs (tree rule, launch clamps never).</summary>
        internal bool Present(PartData p, int stage) => !p.LaunchClamp && (!falls.TryGetValue(p.Id, out Fall f) || f.Stage < stage);

        internal bool Remains(PartData p) => !p.LaunchClamp && !falls.ContainsKey(p.Id);

        private string Unit(PartData p) => falls.TryGetValue(p.Id, out Fall f) ? f.Stage + ":" + f.Cause : "remain";

        private IEnumerable<PartData> Ship => ship.Parts.Where(p => !p.LaunchClamp);

        // ---- fuel (D027, D028, D048) ----

        private bool IsFuel(ResourceData r) => fuel.Contains(r.Name);

        private double NonFuelMass(PartData p) => p.Resources.Where(r => !IsFuel(r)).Sum(r => r.Mass);

        private double CurrentFuelMass(PartData p) => p.Resources.Where(IsFuel).Sum(r => r.Mass);

        private IEnumerable<string> CarriedFuel(PartData p) => p.Resources.Where(r => IsFuel(r) && r.Amount > 0).Select(r => r.Name);

        /// <summary>
        /// All resource mass at the start or end of a stage. A stage the calculator did not record for this part takes the end of
        /// the nearest earlier (higher-numbered) stage it did record; with no record at all the current mass (D048).
        /// </summary>
        internal double ResourceMassAt(PartData p, int stage, bool start)
        {
            if (p.StageResources.TryGetValue(stage, out StageMass m))
                return start ? m.Start : m.End;
            int? nearest = null;
            foreach (int key in p.StageResources.Keys)
            {
                if (key > stage && (!nearest.HasValue || key < nearest.Value))
                    nearest = key;
            }
            return nearest.HasValue ? p.StageResources[nearest.Value].End : p.Resources.Sum(r => r.Mass);
        }

        internal double FuelMassAt(PartData p, int stage, bool start) => Math.Max(0, ResourceMassAt(p, stage, start) - NonFuelMass(p));

        private bool HasFuel(PartData p) => p.Resources.Any(r => IsFuel(r) && r.Amount > 0);

        /// <summary>
        /// Fuel <paramref name="propellant"/> is carried while <paramref name="stage"/> runs (D075, D080, D082): a part on the ship
        /// in that stage holds it now and still has fuel at the stage's start at the answer's precision. A tank an earlier stage
        /// emptied, or a part whose only fuel is another engine's, does not count. The calculator's mass is per part, not per
        /// resource (limit D081).
        /// </summary>
        private bool CarriedIn(string propellant, int stage)
            => Ship.Any(p => Present(p, stage)
                && p.Resources.Any(r => r.Name == propellant && r.Amount > 0)
                && Checks.Round(FuelMassAt(p, stage, true), Checks.MassDigits) > 0);

        // ---- stage table (C2, D031, D032, D033, D036, D041, D045) ----

        /// <summary>Calculator set of parts on the ship at the start of a stage (matches its startMass, D032, D034).</summary>
        private IEnumerable<PartData> CalcSet(int stage) => ship.Parts.Where(p => p.HasCalc && p.DecoupleStage < stage);

        public List<StageRow> StageTable()
        {
            bool calculated = ship.Engines.Count > 0;
            List<StageRow> rows = new List<StageRow>();
            double? previousEnd = null;
            foreach (int s in stageNumbers)
            {
                StageRow row = new StageRow { Stage = s, EngineStage = engineStages.Contains(s) };
                if (calculated)
                {
                    if (ship.Stages.TryGetValue(s, out StageCalc c))
                    {
                        row.StartMass = c.StartMass;
                        row.EndMass = c.EndMass;
                        row.DvAsl = c.DvAsl;
                        row.DvVac = c.DvVac;
                        row.TwrAsl = c.TwrAsl;
                        row.TwrVac = c.TwrVac;
                        row.Burn = c.BurnTime;
                    }
                    else
                    {
                        row.StartMass = row.EndMass = previousEnd ?? DirectMass(s);
                    }
                    row.FuelMass = CalcSet(s).Sum(p => FuelMassAt(p, s, true));
                }
                else
                {
                    row.StartMass = row.EndMass = DirectMass(s);
                    row.FuelMass = Ship.Where(p => Present(p, s)).Sum(CurrentFuelMass);
                }
                if (!row.EngineStage)
                {
                    row.DvAsl = row.DvVac = row.TwrAsl = row.TwrVac = row.Burn = 0;
                }
                else
                {
                    ComOffset(row);
                }
                previousEnd = row.EndMass;
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>Mass of the parts on the ship during a stage when the calculator has no stage values (D033, D036).</summary>
        private double DirectMass(int stage)
            => Ship.Where(p => Present(p, stage)).Sum(p => DryMassInStage(p, stage) + p.Resources.Sum(r => r.Mass));

        private static double DryMassInStage(PartData p, int stage)
            => p.Mass - (p.HasCalc && p.IsJettison && p.ActivationStage >= stage ? p.JettisonMass : 0);

        // ---- centre of mass and thrust line (C6, D029, D033, D034) ----

        private static double EngineThrust(EngineData e) => e.ThrustVac > 0 ? e.ThrustVac : e.ThrustAsl;

        internal List<EngineData> ThrustEngines(int stage) => ship.Engines.Where(e => e.BurnStages.Contains(stage)).ToList();

        internal Vec CenterOfMass(int stage)
        {
            Vec sum = Vec.Zero;
            double mass = 0;
            foreach (PartData p in CalcSet(stage))
            {
                double m = p.Mass - (p.JettisonInStage(stage) ? p.JettisonMass : 0) + ResourceMassAt(p, stage, true);
                sum += p.Com * m;
                mass += m;
            }
            return mass > 0 ? sum * (1.0 / mass) : Vec.Zero;
        }

        /// <summary>Thrust line of a stage and its offset from the centre of mass; null when the engine thrust sums to 0.</summary>
        internal ThrustLine Line(int stage)
        {
            List<EngineData> engines = ThrustEngines(stage);
            double total = engines.Sum(EngineThrust);
            if (total <= 0)
                return null;
            Vec point = Vec.Zero;
            Vec direction = Vec.Zero;
            foreach (EngineData e in engines)
            {
                double f = EngineThrust(e);
                point += e.ThrustPoint * (f / total);
                direction += (-e.ExhaustDir).Normalized() * f;
            }
            Vec u = direction.Normalized();
            if (u.Length == 0)
                return null;
            Vec com = CenterOfMass(stage);
            Vec toCom = com - point;
            Vec perpendicular = toCom - u * Vec.Dot(toCom, u);
            double offset = perpendicular.Length;
            double angle = toCom.Length == 0 ? 0 : Math.Acos(Math.Max(-1, Math.Min(1, Vec.Dot(toCom.Normalized(), u)))) * 180 / Math.PI;
            double gimbalTorque = 0;
            foreach (EngineData e in engines)
            {
                double along = Math.Abs(Vec.Dot(com - e.ThrustPoint, u));
                gimbalTorque += EngineThrust(e) * Math.Sin(e.GimbalDeg * Math.PI / 180) * along;
            }
            return new ThrustLine
            {
                Engines = engines,
                Thrust = total,
                OffsetM = offset,
                OffsetDeg = angle,
                GimbalTorque = gimbalTorque,
            };
        }

        private void ComOffset(StageRow row)
        {
            ThrustLine line = Line(row.Stage);
            if (line == null)
                return;
            row.OffsetM = line.OffsetM;
            row.OffsetDeg = line.OffsetDeg;
        }

        // ---- warnings (C4-C7) ----

        private static Dictionary<string, object> Warning(string code, int? stage, IEnumerable<uint> parts, string message,
            Dictionary<string, object> values)
        {
            Dictionary<string, object> w = new Dictionary<string, object> { ["code"] = code };
            if (stage.HasValue)
                w["stage"] = stage.Value;
            w["parts"] = parts.Distinct().Select(id => (object)id).ToList();
            w["message"] = message;
            w["values"] = values ?? new Dictionary<string, object>();
            return w;
        }

        public List<object> Warnings(List<StageRow> rows)
        {
            List<object> warnings = new List<object>();
            List<int> engineStagesDesc = engineStages.OrderByDescending(s => s).ToList();

            // no_separation: after an engine's last burning stage, it is still on the ship in the next engine stage.
            foreach (EngineData e in ship.Engines.Where(e => e.BurnStages.Count > 0 && byId.ContainsKey(e.PartId)))
            {
                int last = e.BurnStages.Min();
                int? next = engineStagesDesc.Where(s => s < last).Cast<int?>().FirstOrDefault();
                if (next.HasValue && Present(byId[e.PartId], next.Value))
                {
                    warnings.Add(Warning("no_separation", next.Value, new[] { e.PartId },
                        "engine " + e.PartId + " finishes burning in stage " + last + " but is not separated before stage " + next.Value,
                        new Dictionary<string, object> { ["last_burn_stage"] = last }));
                }
            }

            // fuel_blocked: a staged engine never burns although its fuel is on the ship in its stage (D075, D080, D082).
            Dictionary<uint, Dictionary<string, object>> blocked = new Dictionary<uint, Dictionary<string, object>>();
            foreach (EngineData e in ship.Engines.Where(e => e.Staged && e.BurnStages.Count == 0 && byId.ContainsKey(e.PartId)))
            {
                List<string> present = e.Propellants.Where(n => fuel.Contains(n) && CarriedIn(n, e.Stage)).Distinct().ToList();
                if (present.Count == 0)
                    continue;
                Dictionary<string, object> w = Warning("fuel_blocked", e.Stage, new[] { e.PartId },
                    "engine " + e.PartId + " cannot draw " + string.Join(", ", present) + " although the ship carries it",
                    new Dictionary<string, object> { ["propellants"] = present.Cast<object>().ToList() });
                blocked[e.PartId] = w;
                warnings.Add(w);
            }

            // no_engine_fuel: a tank whose fuel never goes down while it is on the ship (D021); a tank in the same
            // separation unit as a fuel_blocked engine that burns its fuel is reported with that engine instead (D018).
            HashSet<uint> reportedTanks = new HashSet<uint>();
            foreach (PartData t in Ship.Where(HasFuel))
            {
                List<int> present = stageNumbers.Where(s => Present(t, s)).ToList();
                double startFuel, endFuel;
                if (stageNumbers.Count == 0 || present.Count == 0)
                {
                    // No stages, or the tank drops off as the first stage fires: no engine ever draws from it.
                    startFuel = endFuel = CurrentFuelMass(t);
                }
                else
                {
                    startFuel = FuelMassAt(t, present.Max(), true);
                    endFuel = FuelMassAt(t, present.Min(), false);
                }
                // Judged at the answer's precision (D076): float noise of a tank that burns to empty is not a defect, and a tank
                // whose fuel the answer shows as 0 is not reported.
                if (Checks.Round(startFuel - endFuel, Checks.MassDigits) > 0 || Checks.Round(startFuel, Checks.MassDigits) <= 0)
                    continue;
                List<string> tankFuel = CarriedFuel(t).ToList();
                EngineData owner = ship.Engines.FirstOrDefault(e => blocked.ContainsKey(e.PartId)
                    && Unit(byId[e.PartId]) == Unit(t) && e.Propellants.Any(tankFuel.Contains));
                reportedTanks.Add(t.Id);
                if (owner != null)
                {
                    List<object> parts = (List<object>)blocked[owner.PartId]["parts"];
                    if (!parts.Contains(t.Id))
                        parts.Add(t.Id);
                    continue;
                }
                int? stage = falls.TryGetValue(t.Id, out Fall f) ? f.Stage : (int?)null;
                warnings.Add(Warning("no_engine_fuel", stage, new[] { t.Id },
                    "tank " + t.Id + " feeds no working engine before it " + (stage.HasValue ? "falls off in stage " + stage.Value : "reaches the end"),
                    new Dictionary<string, object>
                    {
                        ["fuel_mass_t"] = Checks.Round(startFuel, 3),
                        ["resources"] = tankFuel.Cast<object>().ToList(),
                    }));
            }
            foreach (Dictionary<string, object> w in blocked.Values)
            {
                foreach (object id in (List<object>)w["parts"])
                    reportedTanks.Add((uint)id);
            }

            // com_offset: thrust × offset exceeds what the gimbals can turn (C6).
            foreach (StageRow row in rows.Where(r => r.EngineStage))
            {
                ThrustLine line = Line(row.Stage);
                if (line == null)
                    continue;
                // Judged at the answer's precision (D076): below a millimetre it is float noise of a symmetric ship, not a defect.
                double offset = Checks.Round(line.OffsetM, Checks.OffsetDigits);
                if (line.Thrust * offset <= line.GimbalTorque)
                    continue;
                warnings.Add(Warning("com_offset", row.Stage, line.Engines.Select(e => e.PartId),
                    "stage " + row.Stage + ": the thrust line misses the centre of mass by " + offset + " m and the gimbals cannot cancel it",
                    new Dictionary<string, object>
                    {
                        ["offset_m"] = offset,
                        ["offset_deg"] = Checks.Round(line.OffsetDeg, 2),
                        ["thrust_kn"] = Checks.Round(line.Thrust, 3),
                        ["torque_knm"] = Checks.Round(line.Thrust * offset, 3),
                        ["gimbal_torque_knm"] = Checks.Round(line.GimbalTorque, 3),
                    }));
            }

            // no_control: no command part, or none left on the root side after every separation.
            List<PartData> commands = Ship.Where(p => p.Command).ToList();
            if (commands.Count == 0)
            {
                warnings.Add(Warning("no_control", null, Enumerable.Empty<uint>(), "the ship has no command part (pod or probe core)", null));
            }
            else if (!commands.Any(Remains))
            {
                warnings.Add(Warning("no_control", null, commands.Select(p => p.Id),
                    "no command part stays on the ship after the last separation", null));
            }

            // low_launch_twr: VAB ship whose first engine stage has sea-level TWR below 1.
            if (string.Equals(ship.Facility, "VAB", StringComparison.OrdinalIgnoreCase) && engineStagesDesc.Count > 0)
            {
                int first = engineStagesDesc[0];
                // Judged on the unrounded TWR: 0.995-0.999 cannot lift off either (D096). The warning shows three decimals so it
                // never reads "1 (below 1)"; the stage table keeps two.
                double twr = ship.Stages.TryGetValue(first, out StageCalc c) ? c.TwrAsl : 0;
                if (twr < 1)
                {
                    double shown = Checks.Round(twr, 3);
                    warnings.Add(Warning("low_launch_twr", first, ThrustEngines(first).Select(e => e.PartId),
                        "stage " + first + " lifts off with sea-level TWR " + shown + " (below 1) at " + ship.Body,
                        new Dictionary<string, object> { ["twr_asl"] = shown }));
                }
            }

            // leftover_fuel: fuel left in the tanks that stay to the end (D025), except tanks already reported.
            int? lastStage = stageNumbers.Count > 0 ? stageNumbers.Min() : (int?)null;
            foreach (PartData t in Ship.Where(p => HasFuel(p) && Remains(p) && !reportedTanks.Contains(p.Id)))
            {
                double left = lastStage.HasValue ? FuelMassAt(t, lastStage.Value, false) : CurrentFuelMass(t);
                if (Checks.Round(left, Checks.MassDigits) <= 0)
                    continue;
                warnings.Add(Warning("leftover_fuel", null, new[] { t.Id },
                    "tank " + t.Id + " still holds " + Checks.Round(left, 3) + " t of fuel after the last stage",
                    new Dictionary<string, object>
                    {
                        ["fuel_mass_t"] = Checks.Round(left, 3),
                        ["resources"] = CarriedFuel(t).Cast<object>().ToList(),
                    }));
            }

            // no_parachute: crew can ride but there is no parachute.
            List<PartData> crewed = Ship.Where(p => p.CrewCapacity > 0).ToList();
            if (crewed.Count > 0 && !Ship.Any(p => p.Parachute))
            {
                warnings.Add(Warning("no_parachute", null, crewed.Select(p => p.Id), "the ship can carry crew but has no parachute", null));
            }

            return warnings;
        }
    }

    internal sealed class ThrustLine
    {
        public List<EngineData> Engines;
        public double Thrust;
        public double OffsetM;
        public double OffsetDeg;
        public double GimbalTorque;
    }
}
