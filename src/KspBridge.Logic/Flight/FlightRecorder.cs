using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KSP.UI.Screens;
using KspBridge.Attitude;
using UnityEngine;

namespace KspBridge.Flight
{
    /// <summary>
    /// Flight recorder (docs 0025): one JSONL file per flight under saves/&lt;save&gt;/KspBridge/flights,
    /// lines start / s (sample) / e (event) / end (summary + prediction checks). Observes only; a write failure
    /// turns recording off without touching flight control. Main thread only.
    /// </summary>
    public static class FlightRecorder
    {
        private const double BaseInterval = 0.5;
        private const double FastInterval = 0.1;
        private const double RailsInterval = 10;
        private const double FastAgl = 1000;
        private const float FlushSeconds = 2f;
        private const double BrakeMaxSeconds = 60;
        private const int SteadyTicksNeeded = 5;
        private static readonly HashSet<string> FinalReasons = new HashSet<string> { "recovered", "destroyed", "reverted" };
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private static Recording _rec;
        private static string _pendingReason;
        private static bool _disabled;
        private static readonly List<Action> Unhooks = new List<Action>();

        public static string CurrentId => _rec?.Id;

        public static string Directory =>
            Path.Combine(Path.Combine(Path.Combine(Path.Combine(KSPUtil.ApplicationRootPath, "saves"), HighLogic.SaveFolder), "KspBridge"), "flights");

        private sealed class Brake
        {
            public double StartUt, StartMet, PredDist, StartAgl, StartVz, StartThr;
            public double TiltSum, FRatioSum, ThrSum;
            public int N, FRatioN;
        }

        private sealed class Recording
        {
            public string Id, Path;
            public StreamWriter Writer;
            public Vessel Vessel;
            public uint Pid;
            public double LastSampleUt = double.NegativeInfinity;
            public float LastFlush;
            public float LastThrottle = -1;
            public bool BrakeArmed;
            public bool LastSas, LastAttitude;
            public string LastSasMode;
            public float LastWarp = 1;
            public float PrevThrottle;
            public int SteadyTicks;
            public readonly Dictionary<string, bool> LastConscious = new Dictionary<string, bool>();
            public double PrevVz, PrevVh, PrevTilt;
            public bool HasPrev;
            public double MaxAlt, MaxG, MaxVs;
            public int PartsLost;
            public readonly List<object> CrewLost = new List<object>();
            public object Touchdown;
            public List<object> Stages;
            public readonly Dictionary<int, double> DvUsed = new Dictionary<int, double>();
            public readonly Dictionary<uint, int> EngineStage = new Dictionary<uint, int>();
            public readonly List<object> Checks = new List<object>();
            public double FrSum, FrMin = double.MaxValue, FrMax = double.MinValue;
            public int FrN;
            public Brake Brake;
            public bool ImpactThisTick;
            public bool WasPacked;
        }

        // ---- hooks ----

        public static void Hook()
        {
            if (Unhooks.Count > 0)
            {
                return;
            }
            // KSP EventData reads the delegate Target: lambdas only, no static method groups.
            On(GameEvents.onStageActivate, s => OnStage(s));
            On(GameEvents.onVesselSituationChange, a => OnSituation(a.host, a.from, a.to));
            On(GameEvents.onCrash, r => OnImpact(r, "crash"));
            On(GameEvents.onCrashSplashdown, r => OnImpact(r, "crash_splashdown"));
            On(GameEvents.onPartDie, p => OnPartDie(p));
            On(GameEvents.onCrewKilled, r => OnCrewKilled(r));
            On(GameEvents.onVesselWillDestroy, v => OnVesselDestroy(v));
            On(GameEvents.OnVesselRecoveryRequested, v => OnRecoveryRequested(v));
            On(GameEvents.onVesselRecovered, (pv, quick) => OnRecovered(pv));
            On(GameEvents.OnRevertToLaunchFlightState, s => Close("reverted"));
            On(GameEvents.OnRevertToPrelaunchFlightState, s => Close("reverted"));
            On(GameEvents.onGameSceneLoadRequested, s => Close(_pendingReason ?? "scene_exit"));
        }

        public static void Unhook()
        {
            foreach (Action a in Unhooks)
            {
                a();
            }
            Unhooks.Clear();
        }

        private static void On<T>(EventData<T> ev, EventData<T>.OnEvent h)
        {
            ev.Add(h);
            Unhooks.Add(() => ev.Remove(h));
        }

        private static void On<T1, T2>(EventData<T1, T2> ev, EventData<T1, T2>.OnEvent h)
        {
            ev.Add(h);
            Unhooks.Add(() => ev.Remove(h));
        }

        /// <summary>Logic Stop (reload): close the segment; the next logic resumes the same file.</summary>
        public static void Stop()
        {
            Close(_pendingReason ?? "reload");
            Unhook();
        }

        // ---- per tick ----

        public static void FixedUpdate()
        {
            if (_disabled || HighLogic.LoadedScene != GameScenes.FLIGHT || !FlightGlobals.ready)
            {
                return;
            }
            Vessel v = FlightGlobals.ActiveVessel;
            if (_rec != null && _rec.Vessel != v)
            {
                Close("switched");
            }
            if (v == null || v.state == Vessel.State.DEAD || v.isEVA || v.vesselType == VesselType.Debris)
            {
                return;
            }
            if (_rec == null)
            {
                Open(v);
                if (_rec == null)
                {
                    return;
                }
            }
            try
            {
                Tick(_rec, v);
            }
            catch (Exception e)
            {
                Fail(e); // any failure turns recording off instead of erroring every tick
            }
        }

        private static void Tick(Recording r, Vessel v)
        {
            double ut = Planetarium.GetUniversalTime();
            float thr = FlightInputHandler.state.mainThrottle;
            double agl = v.altitude - Prediction.GroundAltitude(v);
            double vz = v.verticalSpeed;
            double vh = Math.Sqrt(Math.Max(0, v.srfSpeed * v.srfSpeed - vz * vz));
            double tilt = Vector3d.Angle(v.GetTransform().up, v.upAxis);
            double mass = v.GetTotalMass();
            double thrust = 0;
            bool integrate = !v.packed && mass > 0;

            // Rails warp freezes the attitude in inertial space: record how far off the held target the vessel is when it ends.
            if (r.WasPacked && !v.packed && AttitudeController.Active(v))
            {
                double? err = AttitudeController.ErrorDeg(v);
                Event(r, v, "attitude_after_warp", new Dictionary<string, object>
                {
                    ["frame"] = AttitudeController.Frame,
                    ["error_deg"] = err.HasValue ? (object)R(err.Value, 1) : null,
                    ["over_limit"] = err.HasValue && err.Value > 15, // record-only flag: far off the target after warp
                });
            }
            r.WasPacked = v.packed;
            foreach (Part p in v.parts)
            {
                foreach (ModuleEngines e in p.FindModulesImplementing<ModuleEngines>())
                {
                    if (e.EngineIgnited)
                    {
                        thrust += e.finalThrust;
                        if (integrate && e.finalThrust > 0)
                        {
                            // Delta-v by the engine's own stage as numbered when first seen (the pad numbering of get_stages):
                            // it may keep burning after later stages fire, and the game renumbers inverseStage after decoupling.
                            if (!r.EngineStage.TryGetValue(p.flightID, out int engineStage))
                            {
                                engineStage = p.inverseStage;
                                r.EngineStage[p.flightID] = engineStage;
                            }
                            r.DvUsed.TryGetValue(engineStage, out double used);
                            r.DvUsed[engineStage] = used + e.finalThrust / mass * TimeWarp.fixedDeltaTime;
                        }
                    }
                }
            }
            // Thrust follows a throttle change a tick later: compare thrust only once the throttle is steady.
            r.SteadyTicks = Math.Abs(thr - r.PrevThrottle) < 0.001f ? r.SteadyTicks + 1 : 0;
            r.PrevThrottle = thr;

            if (r.Stages == null)
            {
                r.Stages = StagesSnapshot(v);
                if (r.Stages != null)
                {
                    // The game may renumber stages between flight start and its first delta-v result (an empty stage is
                    // dropped): before any burn, take the engine stages from this moment so they match the snapshot.
                    if (r.DvUsed.Count == 0)
                    {
                        SeedEngineStages(r, v);
                    }
                    Event(r, v, "stages", new Dictionary<string, object> { ["stages"] = r.Stages });
                }
            }

            r.MaxAlt = Math.Max(r.MaxAlt, v.altitude);
            r.MaxVs = Math.Max(r.MaxVs, v.srfSpeed);
            if (!v.packed)
            {
                r.MaxG = Math.Max(r.MaxG, v.geeForce);
            }

            Controls(r, v, thr);
            double interval = v.packed ? RailsInterval : (thr > 0 && agl < FastAgl ? FastInterval : BaseInterval);
            bool due = ut - r.LastSampleUt >= interval - 1e-6;
            if (thr < 0.05f)
            {
                r.BrakeArmed = true;
            }
            // Prediction only when a sample or the brake check needs it (it also refreshes the drag estimate).
            bool brakeNeeds = r.Brake != null || (r.BrakeArmed && vz < -1 && thr >= 0.5f);
            Prediction pred = !v.packed && (due || brakeNeeds) ? Prediction.Get(v) : null;
            BrakeTick(r, v, ut, thr, agl, vz, tilt, thrust, pred);

            if (due)
            {
                r.LastSampleUt = ut;
                Sample(r, v, ut, thr, agl, vz, vh, tilt, mass, thrust, pred);
            }

            r.PrevVz = vz;
            r.PrevVh = vh;
            r.PrevTilt = tilt;
            r.HasPrev = true;
            r.ImpactThisTick = false;
            if (Time.realtimeSinceStartup - r.LastFlush >= FlushSeconds)
            {
                r.Writer.Flush();
                r.LastFlush = Time.realtimeSinceStartup;
            }
        }

        private static void Sample(Recording r, Vessel v, double ut, float thr, double agl, double vz, double vh, double tilt,
            double mass, double thrust, Prediction pred)
        {
            SurfaceFrame.GetAttitude(v, out double pitch, out double heading, out double roll);
            Dictionary<string, object> s = new Dictionary<string, object>
            {
                ["t"] = "s",
                ["ut"] = R(ut, 2),
                ["met"] = R(v.missionTime, 2),
                ["alt"] = R(v.altitude, 1),
                ["agl"] = R(agl, 1),
                ["vz"] = R(vz, 2),
                ["vh"] = R(vh, 2),
                ["vs"] = R(v.srfSpeed, 2),
                ["pitch"] = R(pitch, 1),
                ["hdg"] = R(heading, 1),
                ["roll"] = R(roll, 1),
                ["tilt"] = R(tilt, 1),
                ["m"] = R(mass, 3),
                ["thr"] = R(thr, 3),
                ["sas"] = v.ActionGroups[KSPActionGroup.SAS] ? Handlers.SasModes.ToName(v.Autopilot.Mode) : null,
                ["stage"] = StageManager.CurrentStage,
            };
            if (pred != null)
            {
                double fPred = pred.ThrustKn * thr;
                s["f"] = R(thrust, 2);
                s["f_pred"] = R(fPred, 2);
                s["acc"] = R(v.perturbation.magnitude, 2);
                s["stop"] = pred.StopDistance.HasValue ? (object)R(pred.StopDistance.Value, 1) : null;
                s["margin"] = pred.BurnMargin.HasValue ? (object)R(pred.BurnMargin.Value, 1) : null;
                s["steer"] = SasMonitor.Steering(v);
                s["g"] = R(v.geeForce, 2);
                if (thr > 0.05 && fPred > 1 && r.SteadyTicks >= SteadyTicksNeeded)
                {
                    double ratio = thrust / fPred;
                    r.FrSum += ratio;
                    r.FrN++;
                    r.FrMin = Math.Min(r.FrMin, ratio);
                    r.FrMax = Math.Max(r.FrMax, ratio);
                }
            }
            Write(r, s, false);
        }

        private static void Controls(Recording r, Vessel v, float thr)
        {
            if (r.LastThrottle < 0 || Math.Abs(thr - r.LastThrottle) >= 0.05f || (thr == 0) != (r.LastThrottle == 0))
            {
                if (r.LastThrottle >= 0)
                {
                    Event(r, v, "control", new Dictionary<string, object> { ["throttle"] = R(thr, 3) });
                }
                r.LastThrottle = thr;
            }
            bool sas = v.ActionGroups[KSPActionGroup.SAS];
            string mode = Handlers.SasModes.ToName(v.Autopilot.Mode);
            if (sas != r.LastSas || (sas && mode != r.LastSasMode))
            {
                Event(r, v, "control", new Dictionary<string, object> { ["sas"] = sas ? mode : "off" });
                r.LastSas = sas;
                r.LastSasMode = mode;
            }
            bool attitude = AttitudeController.IsActive;
            if (attitude != r.LastAttitude)
            {
                Event(r, v, "control", new Dictionary<string, object> { ["attitude"] = AttitudeController.Describe(v) });
                r.LastAttitude = attitude;
            }
            float warp = TimeWarp.CurrentRate;
            if (Math.Abs(warp - r.LastWarp) > 0.01f)
            {
                Event(r, v, "warp", new Dictionary<string, object> { ["rate"] = R(warp, 2), ["mode"] = TimeWarp.WarpMode.ToString() });
                r.LastWarp = warp;
            }
            foreach (ProtoCrewMember c in v.GetVesselCrew())
            {
                bool conscious = !c.inactive;
                if (r.LastConscious.TryGetValue(c.name, out bool was) && was != conscious)
                {
                    Event(r, v, "crew", new Dictionary<string, object> { ["name"] = c.name, ["conscious"] = conscious, ["g_load"] = R(c.GExperiencedNormalized, 2) });
                }
                r.LastConscious[c.name] = conscious;
            }
        }

        // ---- prediction checks ----

        private static void BrakeTick(Recording r, Vessel v, double ut, float thr, double agl, double vz, double tilt, double thrust, Prediction pred)
        {
            if (r.Brake == null)
            {
                // First reach of 0.5 after the throttle was off (gradual throttle-up counts too).
                if (pred != null && r.BrakeArmed && vz < -1 && thr >= 0.5f && pred.StopDistance.HasValue)
                {
                    r.BrakeArmed = false;
                    r.Brake = new Brake
                    {
                        StartUt = ut,
                        StartMet = v.missionTime,
                        PredDist = pred.StopDistance.Value,
                        StartAgl = agl,
                        StartVz = vz,
                        StartThr = thr,
                    };
                }
                return;
            }
            Brake b = r.Brake;
            b.N++;
            b.TiltSum += tilt;
            b.ThrSum += thr;
            if (pred != null && pred.ThrustKn * thr > 1 && r.SteadyTicks >= SteadyTicksNeeded)
            {
                b.FRatioSum += thrust / (pred.ThrustKn * thr);
                b.FRatioN++;
            }
            string end = null;
            if (vz >= 0)
            {
                end = "stopped";
            }
            else if (thr < 0.05f)
            {
                end = "throttle_off";
            }
            else if (v.Landed || v.Splashed || r.ImpactThisTick)
            {
                end = "ground";
            }
            else if (ut - b.StartUt > BrakeMaxSeconds)
            {
                end = "timeout";
            }
            if (end != null)
            {
                EndBrake(r, v, agl, vz, end);
            }
        }

        private static void EndBrake(Recording r, Vessel v, double agl, double vz, string reason)
        {
            Brake b = r.Brake;
            r.Brake = null;
            double actual = b.StartAgl - agl;
            Dictionary<string, object> c = new Dictionary<string, object>
            {
                ["type"] = "brake",
                ["start_met"] = R(b.StartMet, 2),
                ["start_agl"] = R(b.StartAgl, 1),
                ["start_vz"] = R(b.StartVz, 2),
                ["start_thr"] = R(b.StartThr, 3), // pred_dist assumes full thrust, vertical
                ["pred_dist"] = R(b.PredDist, 1),
                ["actual_dist"] = R(actual, 1),
                ["ratio"] = b.PredDist > 1 ? (object)R(actual / b.PredDist, 3) : null,
                ["end_met"] = R(v.missionTime, 2),
                ["end_agl"] = R(agl, 1),
                ["end_vz"] = R(vz, 2),
                ["end"] = reason,
                ["mean_tilt"] = b.N > 0 ? (object)R(b.TiltSum / b.N, 1) : null,
                ["mean_f_ratio"] = b.FRatioN > 0 ? (object)R(b.FRatioSum / b.FRatioN, 3) : null,
                ["mean_thr"] = b.N > 0 ? (object)R(b.ThrSum / b.N, 3) : null,
            };
            r.Checks.Add(c);
            Event(r, v, "check", c);
        }

        /// <summary>At the end of a segment: delta-v burned by each stage's engines (whole flight, seeded on resume) vs the game's pad value.</summary>
        private static void CheckDv(Recording r, int stage, double used)
        {
            double? game = null;
            if (r.Stages != null)
            {
                foreach (Dictionary<string, object> s in r.Stages.OfType<Dictionary<string, object>>())
                {
                    if (Convert.ToInt32(s["stage"]) == stage)
                    {
                        game = Convert.ToDouble(s["dv_actual"]);
                    }
                }
            }
            r.Checks.Add(new Dictionary<string, object>
            {
                ["type"] = "dv",
                ["stage"] = stage,
                ["dv_used"] = R(used, 1),
                ["dv_actual"] = game.HasValue ? (object)R(game.Value, 1) : null,
                ["ratio"] = game.HasValue && game.Value > 1 ? (object)R(used / game.Value, 3) : null,
            });
        }

        // ---- events ----

        private static bool Mine(Vessel v) => _rec != null && v != null && v == _rec.Vessel;

        private static void OnStage(int stage)
        {
            if (_rec != null)
            {
                Event(_rec, _rec.Vessel, "stage", new Dictionary<string, object> { ["stage"] = stage });
            }
        }

        private static void OnSituation(Vessel v, Vessel.Situations from, Vessel.Situations to)
        {
            if (!Mine(v))
            {
                return;
            }
            Dictionary<string, object> e = new Dictionary<string, object> { ["from"] = from.ToString(), ["to"] = to.ToString() };
            if (to == Vessel.Situations.LANDED || to == Vessel.Situations.SPLASHED)
            {
                Dictionary<string, object> td = Contact(_rec, v, to.ToString());
                e["contact"] = td;
                _rec.Touchdown = td;
            }
            Event(_rec, v, "situation", e);
        }

        private static void OnImpact(EventReport report, string kind)
        {
            Part p = report?.origin;
            if (p == null || !Mine(p.vessel))
            {
                return;
            }
            _rec.ImpactThisTick = true;
            Dictionary<string, object> td = Contact(_rec, p.vessel, kind);
            td["part"] = p.partInfo != null ? p.partInfo.title : p.name;
            _rec.Touchdown = td;
            Event(_rec, p.vessel, "impact", td);
        }

        private static void OnPartDie(Part p)
        {
            if (p == null || !Mine(p.vessel))
            {
                return;
            }
            _rec.PartsLost++;
            _rec.ImpactThisTick = true;
            Event(_rec, p.vessel, "part_lost", new Dictionary<string, object> { ["part"] = p.partInfo != null ? p.partInfo.title : p.name });
        }

        private static void OnCrewKilled(EventReport report)
        {
            if (_rec == null || report == null)
            {
                return;
            }
            string name = report.sender;
            if (!_rec.LastConscious.ContainsKey(name ?? ""))
            {
                return;
            }
            _rec.CrewLost.Add(name);
            Event(_rec, _rec.Vessel, "crew", new Dictionary<string, object> { ["name"] = name, ["killed"] = true });
        }

        private static void OnVesselDestroy(Vessel v)
        {
            if (Mine(v))
            {
                Close("destroyed");
            }
        }

        private static void OnRecoveryRequested(Vessel v)
        {
            if (Mine(v))
            {
                _pendingReason = "recovered";
            }
        }

        private static void OnRecovered(ProtoVessel pv)
        {
            if (pv == null)
            {
                return;
            }
            if (_rec != null && _rec.Pid == pv.persistentId)
            {
                _pendingReason = "recovered";
                return;
            }
            try
            {
                // Recovered after the flight scene closed the segment (e.g. scene_exit): append the final reason.
                string path = Latest(pv.persistentId);
                Dictionary<string, object> end = path != null ? FlightFiles.ReadLastEnd(path) : null;
                if (path != null && (end == null || !FinalReasons.Contains(end["reason"] as string ?? "")))
                {
                    Dictionary<string, object> line = end != null ? new Dictionary<string, object>(end) : new Dictionary<string, object> { ["t"] = "end" };
                    line["ut"] = R(Planetarium.GetUniversalTime(), 2);
                    line["reason"] = "recovered";
                    File.AppendAllText(path, Json.Json.Serialize(line) + "\n", Utf8);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KspBridge] flight recorder: recovery append failed: " + e.Message);
            }
        }

        private static Dictionary<string, object> Contact(Recording r, Vessel v, string what)
        {
            double vz = v.verticalSpeed;
            return new Dictionary<string, object>
            {
                ["what"] = what,
                ["met"] = R(v.missionTime, 2),
                ["vz"] = R(r.HasPrev ? r.PrevVz : vz, 2), // last tick before contact
                ["vh"] = R(r.HasPrev ? r.PrevVh : 0, 2),
                ["tilt"] = R(r.HasPrev ? r.PrevTilt : 0, 1),
            };
        }

        /// <summary>SAS monitor events (mode_lost, steering_lost, reapplied).</summary>
        internal static void SasEvent(Vessel v, Dictionary<string, object> e)
        {
            if (Mine(v))
            {
                Dictionary<string, object> copy = new Dictionary<string, object>(e);
                copy.Remove("ut");
                copy.Remove("met");
                copy["sas_event"] = copy["event"];
                copy.Remove("event");
                Event(_rec, v, "sas", copy);
            }
        }

        /// <summary>An anomaly that ended a wait_until early.</summary>
        internal static void AnomalyEvent(Vessel v, Dictionary<string, object> anomaly)
        {
            if (Mine(v))
            {
                Event(_rec, v, "anomaly", new Dictionary<string, object>(anomaly));
            }
        }

        /// <summary>execute_node state changes.</summary>
        internal static void NodeExecEvent(Vessel v, Dictionary<string, object> e)
        {
            if (Mine(v))
            {
                Event(_rec, v, "node_exec", new Dictionary<string, object>(e));
            }
        }

        internal static void TriggerFired(Vessel v, int id, string name, string field, object value, List<Dictionary<string, object>> actions)
        {
            if (Mine(v))
            {
                Event(_rec, v, "trigger", new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["name"] = name,
                    ["field"] = field,
                    ["value"] = value is double d ? R(d, 3) : value,
                    ["actions"] = actions.Cast<object>().ToList(),
                });
            }
        }

        // ---- files ----

        private static void Open(Vessel v)
        {
            try
            {
                string dir = Directory;
                System.IO.Directory.CreateDirectory(dir);
                uint pid = v.persistentId;
                string latest = Latest(pid);
                Dictionary<string, object> lastEnd = latest != null ? FlightFiles.ReadLastEnd(latest) : null;
                bool resume = latest != null && (lastEnd == null || !FinalReasons.Contains(lastEnd["reason"] as string ?? ""));
                double ut = Planetarium.GetUniversalTime();
                string id = resume ? Path.GetFileNameWithoutExtension(latest) : null;
                if (id == null)
                {
                    // Revert to launch restores the launch UT and persistentId: never append a new flight to an old file.
                    long key = (long)Math.Floor(ut);
                    while (File.Exists(Path.Combine(dir, key + "-" + pid + ".jsonl")))
                    {
                        key++;
                    }
                    id = key + "-" + pid;
                }
                Recording r = new Recording
                {
                    Id = id,
                    Path = Path.Combine(dir, id + ".jsonl"),
                    Vessel = v,
                    Pid = pid,
                    LastFlush = Time.realtimeSinceStartup,
                };
                r.Writer = new StreamWriter(new FileStream(r.Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Utf8);
                if (resume && lastEnd != null)
                {
                    Seed(r, lastEnd);
                }
                foreach (ProtoCrewMember c in v.GetVesselCrew())
                {
                    r.LastConscious[c.name] = !c.inactive;
                }
                r.LastSas = v.ActionGroups[KSPActionGroup.SAS];
                r.LastSasMode = Handlers.SasModes.ToName(v.Autopilot.Mode);
                r.LastAttitude = AttitudeController.IsActive;
                r.LastWarp = TimeWarp.CurrentRate;
                SeedEngineStages(r, v);
                r.Stages = StagesSnapshot(v);
                _rec = r;
                _pendingReason = null;
                string reason = resume ? "resume" : v.situation == Vessel.Situations.PRELAUNCH ? "launch" : "attach";
                Write(r, new Dictionary<string, object>
                {
                    ["t"] = "start",
                    ["reason"] = reason,
                    ["ut"] = R(ut, 2),
                    ["met"] = R(v.missionTime, 2),
                    ["vessel"] = v.vesselName,
                    ["type"] = v.vesselType.ToString(),
                    ["situation"] = v.situation.ToString(),
                    ["site"] = v.launchedFrom,
                    ["crew"] = v.GetVesselCrew().Select(c => (object)new Dictionary<string, object>
                    {
                        ["name"] = c.name,
                        ["trait"] = c.trait,
                        ["level"] = c.experienceLevel,
                    }).ToList(),
                    ["stages"] = r.Stages,
                    ["logic"] = Routes.Version,
                }, true);
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        /// <summary>Resumed segment: carry the previous summary so the last end line covers the whole flight.</summary>
        private static void Seed(Recording r, Dictionary<string, object> end)
        {
            r.MaxAlt = Num(end, "max_alt");
            r.MaxG = Num(end, "max_g");
            r.MaxVs = Num(end, "max_vs");
            r.PartsLost = (int)Num(end, "parts_lost");
            if (end.TryGetValue("crew_lost", out object cl) && cl is List<object> crew)
            {
                r.CrewLost.AddRange(crew);
            }
            end.TryGetValue("touchdown", out r.Touchdown);
            if (end.TryGetValue("dv_used", out object du) && du is List<object> dvs)
            {
                foreach (Dictionary<string, object> d in dvs.OfType<Dictionary<string, object>>())
                {
                    r.DvUsed[Convert.ToInt32(d["stage"])] = Convert.ToDouble(d["dv"]);
                }
            }
            if (end.TryGetValue("checks", out object ch) && ch is List<object> checks)
            {
                // Brake checks carry over; dv checks are recomputed from dv_used; thrust stats are per segment.
                foreach (Dictionary<string, object> c in checks.OfType<Dictionary<string, object>>())
                {
                    string type = c.TryGetValue("type", out object t) ? t as string : null;
                    if (type == "brake" || type == "thrust_segment")
                    {
                        r.Checks.Add(c);
                    }
                    else if (type == "thrust")
                    {
                        Dictionary<string, object> old = new Dictionary<string, object>(c) { ["type"] = "thrust_segment" };
                        r.Checks.Add(old);
                    }
                }
            }
        }

        private static double Num(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out object o) && o is double x ? x : 0;

        private static void Close(string reason)
        {
            Recording r = _rec;
            if (r == null)
            {
                _pendingReason = null;
                return;
            }
            _rec = null;
            _pendingReason = null;
            try
            {
                Vessel v = r.Vessel;
                double met = v != null ? v.missionTime : 0;
                if (r.Brake != null && v != null)
                {
                    EndBrake(r, v, v.altitude - Prediction.GroundAltitude(v), v.verticalSpeed, "flight_end");
                }
                foreach (KeyValuePair<int, double> kv in r.DvUsed.OrderByDescending(kv => kv.Key))
                {
                    CheckDv(r, kv.Key, kv.Value);
                }
                if (r.FrN > 0)
                {
                    r.Checks.Add(new Dictionary<string, object>
                    {
                        ["type"] = "thrust",
                        ["mean_ratio"] = R(r.FrSum / r.FrN, 3),
                        ["min_ratio"] = R(r.FrMin, 3),
                        ["max_ratio"] = R(r.FrMax, 3),
                        ["samples"] = r.FrN,
                    });
                }
                Write(r, new Dictionary<string, object>
                {
                    ["t"] = "end",
                    ["ut"] = R(Planetarium.GetUniversalTime(), 2),
                    ["met"] = R(met, 2),
                    ["reason"] = reason,
                    ["vessel"] = v != null ? v.vesselName : null,
                    ["max_alt"] = R(r.MaxAlt, 1),
                    ["max_g"] = R(r.MaxG, 2),
                    ["max_vs"] = R(r.MaxVs, 1),
                    ["parts_lost"] = r.PartsLost,
                    ["crew_lost"] = r.CrewLost,
                    ["touchdown"] = r.Touchdown,
                    ["dv_used"] = r.DvUsed.OrderByDescending(kv => kv.Key).Select(kv => (object)new Dictionary<string, object>
                    {
                        ["stage"] = kv.Key,
                        ["dv"] = R(kv.Value, 1),
                    }).ToList(),
                    ["checks"] = r.Checks,
                }, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KspBridge] flight recorder: close failed: " + e.Message);
            }
            finally
            {
                try
                {
                    r.Writer.Dispose();
                }
                catch (Exception)
                {
                    // already failed; nothing to release
                }
            }
        }

        /// <summary>Make the current file readable up to now (list/log queries).</summary>
        public static void Flush()
        {
            try
            {
                _rec?.Writer.Flush();
            }
            catch (IOException e)
            {
                Fail(e);
            }
        }

        private static string Latest(uint pid)
        {
            string dir = Directory;
            if (!System.IO.Directory.Exists(dir))
            {
                return null;
            }
            return System.IO.Directory.GetFiles(dir, "*-" + pid + ".jsonl")
                .Select(p => new { p, ut = FlightFiles.StartUt(Path.GetFileNameWithoutExtension(p)) })
                .Where(x => x.ut.HasValue)
                .OrderByDescending(x => x.ut.Value)
                .Select(x => x.p)
                .FirstOrDefault();
        }

        private static void Event(Recording r, Vessel v, string type, Dictionary<string, object> data)
        {
            Dictionary<string, object> e = new Dictionary<string, object>
            {
                ["t"] = "e",
                ["ut"] = R(Planetarium.GetUniversalTime(), 2),
                ["met"] = R(v != null ? v.missionTime : 0, 2),
                ["type"] = type,
            };
            foreach (KeyValuePair<string, object> kv in data)
            {
                e[kv.Key] = kv.Value;
            }
            Write(r, e, true);
        }

        private static void Write(Recording r, Dictionary<string, object> line, bool flush)
        {
            if (_disabled || r.Writer == null)
            {
                return;
            }
            try
            {
                r.Writer.Write(Json.Json.Serialize(line));
                r.Writer.Write('\n');
                if (flush)
                {
                    r.Writer.Flush();
                    r.LastFlush = Time.realtimeSinceStartup;
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private static void Fail(Exception e)
        {
            if (_disabled)
            {
                return;
            }
            _disabled = true;
            Debug.LogError("[KspBridge] flight recorder disabled: " + e);
            try
            {
                _rec?.Writer?.Dispose();
            }
            catch (Exception)
            {
                // ignore
            }
            _rec = null;
        }

        /// <summary>Engine stage numbers as of now, before any decoupling renumbers them.</summary>
        private static void SeedEngineStages(Recording r, Vessel v)
        {
            foreach (Part p in v.parts)
            {
                if (p.FindModuleImplementing<ModuleEngines>() != null)
                {
                    r.EngineStage[p.flightID] = p.inverseStage;
                }
            }
        }

        private static List<object> StagesSnapshot(Vessel v)
        {
            VesselDeltaV dv = v.VesselDeltaV;
            if (dv == null || !dv.IsReady || dv.OperatingStageInfo == null || dv.OperatingStageInfo.Count == 0)
            {
                return null;
            }
            return dv.OperatingStageInfo.Select(s => (object)new Dictionary<string, object>
            {
                ["stage"] = s.stage,
                ["dv_actual"] = R(s.deltaVActual, 1),
                ["dv_vac"] = R(s.deltaVinVac, 1),
                ["twr_actual"] = R(s.TWRActual, 2),
                ["burn_time"] = R(s.stageBurnTime, 1),
            }).ToList();
        }

        private static double R(double x, int digits) => Math.Round(x, digits);
    }

    /// <summary>Reading recorder files (shared by the recorder and the query handler).</summary>
    internal static class FlightFiles
    {
        private const int TailBytes = 256 * 1024;

        public static double? StartUt(string id)
        {
            int dash = id.IndexOf('-');
            return dash > 0 && long.TryParse(id.Substring(0, dash), out long ut) ? ut : (double?)null;
        }

        public static FileStream OpenShared(string path) =>
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        public static Dictionary<string, object> ReadFirst(string path)
        {
            using (StreamReader sr = new StreamReader(OpenShared(path), Encoding.UTF8))
            {
                return TryParse(sr.ReadLine());
            }
        }

        /// <summary>Last {"t":"end"} line within the file tail, or null.</summary>
        public static Dictionary<string, object> ReadLastEnd(string path)
        {
            using (FileStream fs = OpenShared(path))
            {
                long start = Math.Max(0, fs.Length - TailBytes);
                fs.Seek(start, SeekOrigin.Begin);
                using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                {
                    string text = sr.ReadToEnd();
                    string[] lines = text.Split('\n');
                    for (int i = lines.Length - 1; i >= 0; i--)
                    {
                        string l = lines[i].Trim();
                        if (l.StartsWith("{\"t\":\"end\"", StringComparison.Ordinal))
                        {
                            Dictionary<string, object> d = TryParse(l);
                            if (d != null)
                            {
                                return d; // a truncated end line (crash mid-write) is skipped
                            }
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>One JSONL line, or null when empty or malformed (e.g. cut off by a KSP crash).</summary>
        public static Dictionary<string, object> TryParse(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return null;
            }
            try
            {
                return Json.Json.Parse(line) as Dictionary<string, object>;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
