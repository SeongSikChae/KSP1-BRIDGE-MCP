using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KspBridge.Flight
{
    /// <summary>
    /// start/get/stop_resource_transfer (design 0045): the part action window Transfer Out/In over time, between parts of the active vessel.
    /// One transfer at a time; FixedUpdate moves it (Tick). Main thread only.
    /// </summary>
    public static class ResourceTransfer
    {
        private sealed class Side
        {
            public uint Id;
            public string Name;
            public double Amount;
            public double Max;
            public bool Flow;
        }

        private const double Epsilon = 1e-9;

        private static bool _active;
        private static int _seq;
        private static int _id;                 // 0 = none since the logic loaded
        private static string _resource;
        private static int _n;                  // game transferFraction, fixed at start (design 2)
        private static double? _requested;
        private static double _transferred;
        private static double _lastUT;
        private static double? _startUT;
        private static double? _endUT;
        private static string _reason;
        private static string _error;
        private static uint _vesselId;
        private static List<Side> _sources = new List<Side>();
        private static List<Side> _targets = new List<Side>();

        public static object Start(Vessel v, RequestBody b)
        {
            string name = b.GetString("resource");
            List<uint> src = Ids(b, "source_part_ids");
            List<uint> dst = Ids(b, "target_part_ids");
            double? amount = null;
            if (b.Has("amount"))
            {
                double a = b.GetDouble("amount");
                if (!(a > 0) || double.IsInfinity(a))
                {
                    throw ApiException.BadRequest("amount must be a number > 0");
                }
                amount = a;
            }
            if (src.Concat(dst).Distinct().Count() != src.Count + dst.Count)
            {
                throw ApiException.BadRequest("a part id is listed twice (source and target lists must not overlap)");
            }
            if (_active)
            {
                throw new ApiException(409, "transfer_running", "transfer " + _id + " is running; stop_resource_transfer first");
            }
            List<PartResource> s = src.Select(id => Row(v, id, name)).ToList();
            List<PartResource> t = dst.Select(id => Row(v, id, name)).ToList();
            PartResourceDefinition def = s[0].info;
            if (def.resourceTransferMode == ResourceTransferMode.NONE)
            {
                throw new ApiException(409, "not_transferable", name + " cannot be transferred (resourceTransferMode NONE)");
            }
            if (!GameVariables.Instance.UnlockedFuelTransfer(ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.ResearchAndDevelopment)))
            {
                throw new ApiException(409, "transfer_not_unlocked", "resource transfer needs a higher Research and Development level");
            }
            if (HighLogic.CurrentGame.Parameters.CustomParams<GameParameters.AdvancedParams>().ResourceTransferObeyCrossfeed)
            {
                List<PartResource> all = s.Concat(t).ToList();
                foreach (PartResource a in all)
                {
                    foreach (PartResource c in all)
                    {
                        if (a != c && !a.part.CanCrossfeed(c.part, def.id))
                        {
                            throw new ApiException(409, "no_crossfeed", name + " cannot cross-feed from " + Label(a.part) + " to " + Label(c.part)
                                + " (difficulty setting: resource transfer obeys crossfeed rules)");
                        }
                    }
                }
            }
            PartResource blocked = s.FirstOrDefault(r => r.flowMode != PartResource.FlowMode.Out && r.flowMode != PartResource.FlowMode.Both)
                ?? t.FirstOrDefault(r => r.flowMode != PartResource.FlowMode.In && r.flowMode != PartResource.FlowMode.Both);
            if (blocked != null)
            {
                throw new ApiException(409, "flow_mode_blocked", name + " in " + Label(blocked.part) + " has flow mode " + blocked.flowMode);
            }
            if (!PartStates.Unlocked(ControlTypes.TWEAKABLES_ANYCONTROL))
            {
                throw new ApiException(409, "transfer_locked", "resource transfer is not possible now: input_locked (TWEAKABLES_ANYCONTROL)");
            }
            int full = s.Count(r => r.amount > 0);
            int open = t.Count(r => r.amount < r.maxAmount);
            if (full == 0 || open == 0)
            {
                throw new ApiException(409, "nothing_to_transfer", full == 0 ? "every source is empty" : "every target is full");
            }

            double ut = Planetarium.GetUniversalTime();
            _active = true;
            _id = ++_seq;
            _resource = name;
            _n = s.Count == 1 ? open : full;   // Out button (one source) splits over open targets, In button over non-empty sources
            _requested = amount;
            _transferred = 0;
            _lastUT = ut;
            _startUT = ut;
            _endUT = null;
            _reason = null;
            _error = null;
            _vesselId = v.persistentId;
            _sources = s.Select(NewSide).ToList();
            _targets = t.Select(NewSide).ToList();
            PartStates.Log("resource_transfer_start", new Dictionary<string, object>
            {
                ["id"] = _id,
                ["resource"] = _resource,
                ["requested"] = _requested,
                ["source_part_ids"] = src.Select(x => (object)(double)x).ToList(),
                ["target_part_ids"] = dst.Select(x => (object)(double)x).ToList(),
            }, "resource_transfer " + _id + " start " + _resource + " " + string.Join(",", src.Select(x => x.ToString()).ToArray()) + " -> "
                + string.Join(",", dst.Select(x => x.ToString()).ToArray()) + " amount " + (_requested != null ? Num(_requested.Value) : "all"));
            return Describe(null);
        }

        public static object Status()
        {
            if (_active && HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null)
            {
                List<PartResource> s = Resolve(FlightGlobals.ActiveVessel, _sources);
                List<PartResource> t = Resolve(FlightGlobals.ActiveVessel, _targets);
                if (s != null && t != null)
                {
                    Snapshot(s, t);
                }
            }
            return Describe(null);
        }

        public static object StopRequest()
        {
            bool was = _active;
            Stop("stopped");
            return Describe(was);
        }

        /// <summary>FixedUpdate. Never throws.</summary>
        public static void Tick()
        {
            if (!_active)
            {
                return;
            }
            try
            {
                Vessel v = FlightGlobals.ActiveVessel;
                if (v == null || v.persistentId != _vesselId)
                {
                    Stop("vessel_changed");
                    return;
                }
                List<PartResource> s = Resolve(v, _sources);
                List<PartResource> t = Resolve(v, _targets);
                if (s == null || t == null)
                {
                    Stop("part_lost");
                    return;
                }
                double ut = Planetarium.GetUniversalTime();
                double dt = Math.Round(ut - _lastUT, 2);
                _lastUT = ut;
                if (dt > 0)
                {
                    foreach (PartResource to in t)
                    {
                        double rate = to.maxAmount / 20.0 / _n * dt;
                        foreach (PartResource from in s)
                        {
                            double val = Math.Min(Math.Min(rate, from.amount), to.maxAmount - to.amount);
                            if (_requested != null)
                            {
                                val = Math.Min(val, _requested.Value - _transferred);
                            }
                            if (val <= 0)
                            {
                                continue;
                            }
                            double fromBefore = from.amount;
                            double toBefore = to.amount;
                            from.part.TransferResource(from, -val, to.part);   // take first: nothing is created (design 10)
                            double taken = fromBefore - from.amount;
                            if (taken <= 0)
                            {
                                continue;
                            }
                            try
                            {
                                to.part.TransferResource(to, taken, from.part);
                            }
                            catch
                            {
                                from.amount += taken - Math.Max(0, to.amount - toBefore);   // nothing taken is lost when filling throws
                                throw;
                            }
                            double moved = to.amount - toBefore;
                            if (taken - moved > 0)
                            {
                                from.amount += taken - moved;   // give back directly: TransferResource refuses + on an Out-only source
                            }
                            if (Math.Abs((fromBefore - from.amount) - moved) > Epsilon)
                            {
                                _transferred += moved;
                                Snapshot(s, t);
                                Stop("error", "source and target amounts moved differently");
                                return;
                            }
                            _transferred += moved;
                        }
                    }
                }
                Snapshot(s, t);
                if (_requested != null && _transferred >= _requested.Value - Epsilon)
                {
                    Stop("done");
                }
                else if (s.All(r => r.amount <= 0))
                {
                    Stop("source_empty");
                }
                else if (t.All(r => r.amount >= r.maxAmount))
                {
                    Stop("target_full");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[KspBridge] resource transfer: " + e);
                Stop("error", e.Message);
            }
        }

        /// <summary>Never throws (BridgeLogic.Stop calls it before Coordinator.Stop). Ends a running transfer once.</summary>
        public static void Stop(string reason, string error = null)
        {
            if (!_active)
            {
                return;
            }
            _active = false;   // first: a throw below never leaves the transfer running
            _reason = reason;
            _error = error;
            _endUT = _lastUT;  // replaced by the current UT below when it can be read
            try
            {
                if (HighLogic.LoadedSceneIsFlight)
                {
                    _endUT = Planetarium.GetUniversalTime();
                    Vessel v = FlightGlobals.ActiveVessel;
                    if (v != null)
                    {
                        List<PartResource> s = Resolve(v, _sources);
                        List<PartResource> t = Resolve(v, _targets);
                        if (s != null && t != null)
                        {
                            Snapshot(s, t);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[KspBridge] resource transfer snapshot: " + e);
            }
            try
            {
                PartStates.Log("resource_transfer_end", new Dictionary<string, object>
                {
                    ["id"] = _id,
                    ["resource"] = _resource,
                    ["requested"] = _requested,
                    ["transferred"] = _transferred,
                    ["reason"] = _reason,
                    ["error"] = _error,
                }, "resource_transfer " + _id + " end " + _reason + " " + _resource + " transferred " + Num(_transferred)
                    + (_requested != null ? " of " + Num(_requested.Value) : "") + (_error != null ? ": " + _error : ""));
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[KspBridge] resource transfer end log: " + e);   // the end event is missing only here
            }
        }

        private static List<uint> Ids(RequestBody b, string key)
        {
            List<object> list = b.GetList(key);   // '<key>' must be an array when missing or not a list
            if (list.Count == 0)
            {
                throw ApiException.BadRequest(key + " must hold at least one part id");
            }
            List<uint> ids = new List<uint>();
            foreach (object o in list)
            {
                if (!(o is double d) || !(d >= 0 && d <= uint.MaxValue) || d != Math.Floor(d))
                {
                    throw ApiException.BadRequest(key + " must hold integer part ids (persistentId) 0.." + uint.MaxValue);
                }
                ids.Add((uint)d);
            }
            return ids;
        }

        /// <summary>The flight resource row (design 0043: maxAmount != 0 and isVisible) of a part of the vessel.</summary>
        private static PartResource Row(Vessel v, uint id, string name)
        {
            Part p = v.parts.FirstOrDefault(x => x.persistentId == id) ?? throw PartStates.PartNotFound(id);
            PartResource r = p.Resources[name] ?? throw PartStates.ResourceNotFound(p, name);
            if (r.maxAmount == 0 || !r.isVisible)
            {
                throw new ApiException(404, "resource_not_found", name + " in " + Label(p) + " has no flight resource row (empty or hidden)");
            }
            return r;
        }

        /// <summary>The sides' resources on the vessel, or null when a part died, left it or lost the resource.</summary>
        private static List<PartResource> Resolve(Vessel v, List<Side> sides)
        {
            List<PartResource> list = new List<PartResource>();
            foreach (Side side in sides)
            {
                Part p = v.parts.FirstOrDefault(x => x.persistentId == side.Id);
                PartResource r = p != null && p.State != global::PartStates.DEAD ? p.Resources[_resource] : null;
                if (r == null)
                {
                    return null;
                }
                list.Add(r);
            }
            return list;
        }

        private static Side NewSide(PartResource r) => new Side
        {
            Id = r.part.persistentId,
            Name = r.part.partInfo.name,
            Amount = r.amount,
            Max = r.maxAmount,
            Flow = r.flowState,
        };

        private static void Snapshot(List<PartResource> s, List<PartResource> t)
        {
            Copy(s, _sources);
            Copy(t, _targets);
        }

        private static void Copy(List<PartResource> rows, List<Side> sides)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                sides[i].Amount = rows[i].amount;
                sides[i].Max = rows[i].maxAmount;
                sides[i].Flow = rows[i].flowState;
            }
        }

        private static Dictionary<string, object> Describe(bool? stopped)
        {
            bool any = _id > 0;
            Dictionary<string, object> d = new Dictionary<string, object>
            {
                ["active"] = _active,
                ["id"] = any ? (object)_id : null,
                ["resource"] = any ? _resource : null,
                ["requested"] = any ? _requested : null,
                ["transferred"] = any ? (object)_transferred : null,
                ["ut"] = HighLogic.LoadedSceneIsFlight ? (object)Planetarium.GetUniversalTime() : null,
                ["started_ut"] = any ? _startUT : null,
                ["ended_ut"] = any ? _endUT : null,
                ["end_reason"] = any ? _reason : null,
                ["error"] = any ? _error : null,
                ["sources"] = any ? Rows(_sources) : new List<object>(),
                ["targets"] = any ? Rows(_targets) : new List<object>(),
            };
            if (stopped != null)
            {
                d["stopped"] = stopped.Value;
            }
            return d;
        }

        private static List<object> Rows(List<Side> sides) => sides.Select(x => (object)new Dictionary<string, object>
        {
            ["part_id"] = (double)x.Id,
            ["part"] = x.Name,
            ["amount"] = x.Amount,
            ["max_amount"] = x.Max,
            ["flow_enabled"] = x.Flow,
        }).ToList();

        private static string Label(Part p) => p.partInfo.name + " (" + p.persistentId + ")";

        private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
