---
status: done
category: ksp-bridge
feature: SASRCS
phase: 개발
issue: 9
---

# SASRCS 개발

설계: [0014-SASRCS-설계.md](0014-SASRCS-설계.md)

## 단위 1: set_sas와 SAS 상태

- **대상**: `Flight/Control/ManualControl.cs`(신규) `SetSas`·`DescribeSas`·`RequireManual`, `FlightHandler` `POST /api/flight/sas`, `Coordinator.Describe`(`controls`)·`StatusSummary`(`sas`), `FlightTools.SetSas`, 테스트(ExpectedTools, 요청 매핑), `Routes.Version` 0.2.20
- **코드 초안**:

```csharp
private static readonly (string Name, VesselAutopilot.AutopilotMode Mode)[] Modes =
{
    ("stability", VesselAutopilot.AutopilotMode.StabilityAssist), ("prograde", VesselAutopilot.AutopilotMode.Prograde),
    // ... retrograde, normal, antinormal, radial_in(RadialIn), radial_out(RadialOut), target, anti_target(AntiTarget), maneuver
};

public static object SetSas(Vessel v, RequestBody b)
{
    RequireManual(v);                                  // 409 plan_running, eva_not_supported, not_controllable
    bool on = b.GetBool("on");
    string name = b.Has("mode") ? b.GetString("mode") : null;
    string reference = b.Has("reference") ? b.GetString("reference") : null;
    if (!on && (name != null || reference != null)) throw ApiException.BadRequest("mode and reference need on: true");
    VesselAutopilot.AutopilotMode? m = name == null ? (VesselAutopilot.AutopilotMode?)null : ParseMode(name);   // 400 unknown
    FlightGlobals.SpeedDisplayModes? speed = reference == null ? (FlightGlobals.SpeedDisplayModes?)null : ParseReference(v, reference);
    // 400 unknown; target without v.targetObject -> 409 before any change

    VesselAutopilot ap = v.Autopilot;
    SasSnapshot before = SasSnapshot.Take(v);          // display mode, SAS group, ap.Enabled, ap.Mode
    try
    {
        if (!on)
        {
            SetGroupChecked(v, KSPActionGroup.SAS, false);   // 409 group_locked when the group did not change
            return DescribeSas(v);
        }
        bool running = before.Enabled && before.Group;
        VesselAutopilot.AutopilotMode want = m ?? (running ? before.Mode : VesselAutopilot.AutopilotMode.StabilityAssist);
        if (speed.HasValue) FlightGlobals.SetSpeedMode(speed.Value);
        if (!ap.CanSetMode(want)) throw Unavailable(v, want);   // judged once, right after the display mode change
        if (!running)
        {
            // SetMode on a disabled autopilot only records the mode; the next Update's Enable() resets it to stability.
            SetGroupChecked(v, KSPActionGroup.SAS, true);
            if (!ap.Enable(want)) throw Unavailable(v, want);
        }
        else if (want != ap.Mode && !ap.SetMode(want))
        {
            throw Unavailable(v, want);
        }
        return DescribeSas(v);   // {on: ap.Enabled && group, mode, speed_mode, available_modes}
    }
    catch
    {
        before.Restore(v);       // every failure path (any exception): only what differs goes back; Restore never throws
        throw;
    }
}

/// <summary>Error code by the mode actually judged: stability -> sas_unavailable, else sas_mode_unavailable (+ available list).</summary>
private static ApiException Unavailable(Vessel v, VesselAutopilot.AutopilotMode judged) => judged == VesselAutopilot.AutopilotMode.StabilityAssist
    ? new ApiException(409, "sas_unavailable", "SAS is not available on this vessel")
    : new ApiException(409, "sas_mode_unavailable", ModeName(judged) + " is not available now (available: " + string.Join(", ", Available(v)) + ")");

/// <summary>SetGroup goes through ToggleGroup, which leaves the group unchanged during a cooldown or an override group
/// (ActionGroupList.cs 83~150, 212): read it back.</summary>
internal static void SetGroupChecked(Vessel v, KSPActionGroup g, bool on)
{
    if (v.ActionGroups[g] == on) return;
    // an override group toggles only its own list and leaves groups[] (ActionGroupList.cs 106~148): refuse before touching it
    if (Overridden(v, g)) throw new ApiException(409, "group_locked", g + " is held by action group override " + v.GroupOverride);
    v.ActionGroups.SetGroup(g, on);
    if (v.ActionGroups[g] != on) throw new ApiException(409, "group_locked", g + " did not change (cooldown)");   // cooldown changes nothing
}

internal static bool Overridden(Vessel v, KSPActionGroup g)
{
    int n = v.GroupOverride;
    return GameSettings.ADDITIONAL_ACTION_GROUPS && n > 0 && n <= v.OverrideDefault.Length
        && (v.OverrideDefault[n - 1] || (v.OverrideActionControl[n - 1] & g) != 0);
}

private static FlightGlobals.SpeedDisplayModes ParseReference(Vessel v, string reference)
{
    FlightGlobals.SpeedDisplayModes m = reference == "orbit" ? FlightGlobals.SpeedDisplayModes.Orbit
        : reference == "surface" ? FlightGlobals.SpeedDisplayModes.Surface
        : reference == "target" ? FlightGlobals.SpeedDisplayModes.Target
        : throw ApiException.BadRequest("reference must be orbit, surface or target");
    if (m == FlightGlobals.SpeedDisplayModes.Target && v.targetObject == null)   // a destroyed target is not caught (stock TargetLockInvalid alike)
        throw new ApiException(409, "sas_mode_unavailable", "reference target needs a target");
    return m;
}

// SasSnapshot.Restore (never throws, each step on its own): only what differs goes back.
//   display mode != before -> SetSpeedMode(before); group != before -> SetGroup(before) (not when Overridden);
//   enabled before && (!ap.Enabled || ap.Mode != before.Mode) -> ap.Enable(before.Mode);
//   unchanged Enabled/Mode -> no Enable (Enable on a running autopilot re-initializes and re-locks stability to the current attitude)
```

- 사용 가능 여부는 `CanSetMode`만 판정한다. 표시 모드는 기준 프레임만 바꾸고, 판정은 표시 모드를 바꾼 직후에 한다(`CanSetMode`의 normal·radial 벡터 잠금 검사는 orbit 표시에서만 한다. VesselAutopilot.cs 2572~2632). 자체 규칙을 더하지 않는다.
- `RequireManual(v)`: `FlightReset.RequireSceneReady()`·`RequireNoPendingRecovery()`(기존 검사 재사용), `Coordinator.Running`이면 409 `plan_running`, `v.isEVA`이면 409 `eva_not_supported`, `!v.IsControllable`이면 409 `not_controllable`.
- `set_sas`는 시작 상태(`SasSnapshot`)를 기록하고 모든 실패 경로에서 되돌린다. 모드 판정은 표시 모드를 바꾼 직후 한 번만 한다(이전 초안의 `RequireMode` 중복 제거).
- `SetGroup(SAS, true)`는 Autopilot을 직접 부르지 않는다. 그룹이 켜진 채 비활성이면 다음 `Vessel.Update` → `VesselAutopilot.Update`(Vessel.cs 7995, VesselAutopilot.cs 2295~2323)가 `Enable()`=stability로 켠다. 그래서 `Enable(want)`를 같은 호출 안에서 직접 부르면 `enabled`가 true가 되어 Update가 덮어쓰지 않는다. `Enable(mode)`는 이미 켜져 있으면 `Disable()` 뒤 다시 켠다(2443~2462).
- **검증**: 빌드, App 테스트. 실측(reload_mod, 궤도 위 기체):
  - SAS 끈 상태에서 `set_sas on:true mode:prograde reference:orbit` → 응답 mode prograde·speed_mode orbit, 다음 `get_flight`(1프레임 이상 뒤)에서도 `controls.sas.mode` prograde
  - 켜진 상태에서 `mode:normal` → 응답 mode normal, 다음 `get_flight`에서도 normal
  - `reference:surface mode:prograde` → 200, `speed_mode` surface(기준 프레임만 바뀜)
  - `reference:target`(목표 없음) → 409, `speed_mode`·SAS 모드 모두 이전 값 그대로
  - `on:false mode:x` → 400
  - target 없음에서 `mode:target` → 409 `sas_mode_unavailable`(가능 모드 목록 포함), 기동 노드 없음에서 `mode:maneuver` → 409, 둘 다 SAS 모드 불변
  - 계획 실행 중 → 409 `plan_running`
  - `group_locked`·`not_controllable`·EVA는 시험 상황을 MCP만으로 만들 수 없어 코드 검토로 확인한다
  - `get_status.flight.sas` 일치
- **선행 단위**: 없음

## 단위 2: set_rcs와 RCS 상태

- **대상**: `ManualControl.SetRcs`·`DescribeRcs`, `POST /api/flight/rcs`, `controls.rcs`, `StatusSummary`(`rcs`), `FlightTools.SetRcs`, 테스트, 0.2.21
- **코드 초안**:

```csharp
public static object SetRcs(Vessel v, RequestBody b)
{
    RequireManual(v);
    SetGroupChecked(v, KSPActionGroup.RCS, b.GetBool("on"));   // 409 group_locked (the group is unchanged, nothing to restore)
    return DescribeRcs(v);
}

private static Dictionary<string, object> DescribeRcs(Vessel v)
{
    int parts = v.parts.Count(p => p.FindModuleImplementing<ModuleRCS>() != null);
    object mono = null;
    PartResourceDefinition mp = PartResourceLibrary.Instance?.GetDefinition("MonoPropellant");   // a mod may remove it
    if (mp != null)
    {
        v.GetConnectedResourceTotals(mp.id, out double amount, out double max);
        mono = max > 0 ? new Dictionary<string, object> { ["amount"] = amount, ["max"] = max } : null;
    }
    return new Dictionary<string, object> { ["on"] = v.ActionGroups[KSPActionGroup.RCS], ["parts"] = parts, ["monopropellant"] = mono };
}
```

- **검증**: 빌드, App 테스트. 실측: RCS 부품 있는 기체로 `set_rcs on` → `controls.rcs.on` true·parts·monopropellant, `get_status.flight.rcs` true, `off` → false, 계획 중 409.
- **선행 단위**: 단위 1

## 단위 3: rcs_translate 펄스

- **대상**: `ManualControl.Translate`·`Fly`·`Tick`·`StopPulse`·`Stop`·`DescribeTranslate`, `POST /api/flight/translate`, `BridgeLogic`(FixedUpdate에서 `ManualControl.Tick()`, `Stop`에서 `ManualControl.Stop()` 결과를 `released`에 추가), `Coordinator.Submit`(2단계 진입 직후 `ManualControl.StopPulse("plan")`), `FlightScene`(OnScene·OnVesselChange·OnVesselDestroy), `FlightReset`(recover·leave·revert의 Abort 옆), `StatusSummary`(`translating`), `FlightTools.RcsTranslate`, 테스트, 0.2.22
- **코드 초안**:

```csharp
private static bool _active;            // pulse running: never judged by Unity's == null (a destroyed vessel compares equal to null)
private static Vessel _pv;              // hooked pulse vessel (compared with ReferenceEquals)
private static uint _pvId;
private static Vector3 _axes;           // tool frame: x = RT.right, y = -RT.forward, z = RT.up
private static double _endUt, _stoppedUt;
private static Vector3d _dv;            // non-gravitational dv accumulated per tick, tool frame
private static Vector3d _lastVel;       // previous tick's obt_velocity, body and frame (kept by the logic, not acceleration_immediate)
private static CelestialBody _lastBody;
private static bool _lastRotating;      // FlightGlobals.RefFrameIsRotating (CelestialBody.inverseRotation)
private static Vector3d _lastGrav;      // previous tick's graviticAcceleration and dt: paired with the same physics step as Δv
private static double _lastDt;
private static bool _haveLast;
private static readonly HashSet<string> _notes = new HashSet<string>();   // dv_note: frame_changed, landed, atmosphere
private static string _ended;           // null while running or before the first pulse
private static bool _any;               // a pulse was started in this logic

public static object Translate(Vessel v, RequestBody b)
{
    RequireManual(v);
    double s = b.GetDouble("seconds");
    if (!(s > 0 && s <= 30)) throw ApiException.BadRequest("seconds must be in (0, 30]");
    Vector3 a = new Vector3(Axis(b, "x"), Axis(b, "y"), Axis(b, "z"));   // 400 outside [-1, 1], or 0 < |value| < 0.05 (ModuleRCS EPSILON dead zone)
    if (a == Vector3.zero) throw ApiException.BadRequest("x, y or z must be non-zero");
    if (!v.ActionGroups[KSPActionGroup.RCS]) throw new ApiException(409, "rcs_off", "RCS is off (set_rcs on: true first)");
    if (AttitudeController.Suspended(v)) throw new ApiException(409, "on_rails", "rails warp or packed vessel");
    StopPulse("replaced");
    _pv = v; _pvId = v.persistentId; _axes = a; _endUt = Planetarium.GetUniversalTime() + s;
    _dv = Vector3d.zero; _haveLast = false; _notes.Clear(); _ended = null; _any = true; _active = true;
    v.OnFlyByWire += Fly;
    return DescribeTranslate();
}

private static bool Feeding(Vessel v) =>
    ReferenceEquals(v, FlightGlobals.ActiveVessel) && !AttitudeController.Suspended(v) && v.IsControllable
    && v.ActionGroups[KSPActionGroup.RCS] && Planetarium.GetUniversalTime() < _endUt;

private static void Fly(FlightCtrlState st)
{
    try
    {
        if (!_active || !Feeding(_pv)) return;   // FlightInputHandler feeds every loaded vessel; FixedUpdate order is not fixed
        // ModuleRCS: inputLin = RT.rotation * (X, Z, Y). Signs start from the decompile and are fixed by the dv_local measurement.
        st.X = SignX * _axes.x;
        st.Z = SignZ * _axes.z;
        st.Y = SignY * _axes.y;
    }
    catch (Exception)
    {
        StopPulse("error");   // an exception here would break the OnFlyByWire multicast and FlightInputHandler's vessel loop
    }
}

public static void Tick()   // FixedUpdate, after Coordinator.Tick
{
    if (!_active) return;
    try
    {
        Vessel v = _pv;
        if (!HighLogic.LoadedSceneIsFlight) { StopPulse("scene"); return; }
        if ((object)v == null || !v || v.state == Vessel.State.DEAD) { StopPulse("vessel_lost"); return; }
        if (!ReferenceEquals(FlightGlobals.ActiveVessel, v)) { StopPulse("vessel_changed"); return; }
        if (AttitudeController.Suspended(v)) { StopPulse("on_rails"); return; }
        if (!v.ActionGroups[KSPActionGroup.RCS]) { StopPulse("rcs_off"); return; }
        if (!v.IsControllable) { StopPulse("not_controllable"); return; }
        Accumulate(v);                                       // this tick's thrust counts before done
        if (Planetarium.GetUniversalTime() >= _endUt) StopPulse("done");
    }
    catch (Exception)
    {
        StopPulse("error");
    }
}

/// <summary>dv_local: (Δobt_velocity − g·dt) projected on this tick's control axes. Gravity is removed (it turns obt_velocity by
/// several m/s per second in orbit). acceleration_immediate is not used: it is left stale on body/frame-change ticks (Vessel.cs 7543).</summary>
private static void Accumulate(Vessel v)
{
    Vector3d vel = v.obt_velocity;
    CelestialBody body = v.mainBody;
    bool rotating = FlightGlobals.RefFrameIsRotating;   // FlightGlobals.cs 295
    double dt = TimeWarp.fixedDeltaTime;
    // VesselPrecalculate writes next step's graviticAcceleration after measuring: pair Δv with the previous tick's g and dt
    // (stock perturbation_immediate pairs them the same way). A dt change (physics warp) is skipped like a frame change.
    if (_haveLast && ReferenceEquals(body, _lastBody) && rotating == _lastRotating && dt == _lastDt)
    {
        Vector3d d = vel - _lastVel - _lastGrav * _lastDt;
        Transform rt = v.ReferenceTransform;
        _dv += new Vector3d(Vector3d.Dot(d, rt.right), -Vector3d.Dot(d, rt.forward), Vector3d.Dot(d, rt.up));
    }
    else if (_haveLast)
    {
        _notes.Add("frame_changed");   // this tick is skipped; the reference restarts
    }
    if (v.LandedOrSplashed) _notes.Add("landed");
    if (v.atmDensity > 0) _notes.Add("atmosphere");
    _lastVel = vel; _lastBody = body; _lastRotating = rotating; _lastGrav = v.graviticAcceleration; _lastDt = dt; _haveLast = true;
}

public static void StopPulse(string reason)   // never throws
{
    if (!_active) return;
    _active = false;                                   // recorded first, without touching Unity objects
    _ended = reason;
    _stoppedUt = Planetarium.GetUniversalTime();
    Vessel v = _pv;
    _pv = null;
    if ((object)v == null) return;
    Safe(() => v.OnFlyByWire -= Fly);
    Safe(() => { if (v) { v.ctrlState.X = 0; v.ctrlState.Y = 0; v.ctrlState.Z = 0; } });   // axis override groups keep it otherwise
}

/// <summary>Logic stop: the pulse is not handed over.</summary>
public static IList<string> Stop()
{
    bool had = _active;
    StopPulse("logic_stop");
    return had ? new List<string> { "rcs pulse stopped" } : new List<string>();
}
```

- `DescribeTranslate`: `_any`가 false면 null. 아니면 `{active, vessel_id: _pvId, x, y, z, remaining_s(진행 중일 때만), dv_local: _dv, dv_note: _notes(비면 null), ended}`.
- 측정은 한 틱 늦다(직전 물리 스텝의 속도 변화). `done` 외의 사유로 끝나면 마지막 분사 틱이 빠질 수 있다(2초 펄스 기준 약 1%).
- `Coordinator.Describe`의 `controls`와 `StatusSummary`의 요약 필드는 try/catch로 격리해, 실패하면 `controls: null`과 `controls_error`를 넣는다(`BridgeLogic.Status`의 `input_locks` 패턴). 단위 1에서 `controls`를 처음 넣을 때부터 적용한다. Unity 객체에 접근하지 않으므로 기체가 파괴된 뒤에도 값이 남는다.
- `FlightScene`: 각 핸들러에서 `Coordinator.Running` 검사와 별개로 `ManualControl.StopPulse("scene"|"vessel_changed")`를 부르고, `OnVesselDestroy`는 `v.persistentId == ManualControl.PulseVesselId`이고 진행 중일 때 `"vessel_lost"`.
- 도킹: `ManualControl.Hook`/`Unhook`(BridgeLogic Start/Stop)에서 `GameEvents.onPartCouple`을 듣고, `from`이나 `to` 부품의 기체가 펄스 기체(`persistentId == _pvId`)면 `StopPulse("docked")`. 도킹 뒤 주 기체로 남으면 기체 변경 이벤트가 나지 않기 때문이다.
- `BridgeLogic.Status`에 `translate` 키를 `reset`처럼 격리해 넣는다(`ManualControl.Summary()` = `{active, vessel_id, ended}` 또는 null, 실패 시 `translate_error`). 비행 씬 밖(leave·recover 뒤)에서도 종료 사유를 볼 수 있다.
- `FlightReset`: recover(활성 기체)·leave·revert에서 `if (Coordinator.Running)` 블록 **밖**, `Save()`와 씬 로드보다 **앞**에서 같은 사유로 `StopPulse`를 부른다. 계획과 펄스는 동시에 있을 수 없어 `Running` 블록 안에서는 한 번도 실행되지 않는다. 사전 검사(`RequireLeave`, `RequireSave` 등)가 통과한 뒤, 계획 중단과 같은 시점에 둔다.
- `BridgeLogic.Stop`: `IList<string> r = Coordinator.Stop(_context);` 뒤 `ManualControl.Stop()` 결과를 더해 반환(try로 감쌈).
- **부호 확정**: 디컴파일에서 유도한 초기값으로 시작하고(힘은 분사 노즐 반대 방향), 실측에서 x·y·z 각각 2초 펄스를 주어 `dv_local`의 같은 축이 양수인지 확인한다. 반대면 그 축의 부호를 뒤집고 다시 실측한다. 결과를 "실측 결과"에 남긴다.
- **검증**: 빌드, App 테스트. 실측(궤도 위 RCS 기체, 축 실측 전 `set_sas on:true mode:stability`로 회전을 막는다):
  - RCS 끔 → 409 `rcs_off`
  - 켬 → `x:1 seconds:2` → 펄스 중 `active` true·`remaining_s` 감소, 끝난 뒤 `ended: done`, `dv_local.x` > 0이고 |y|, |z|는 x의 10% 이하(y, z도 같은 방식). 이 기준은 RCS가 질량 중심 기준으로 대칭 배치된 시험 기체가 전제다(비대칭이면 회전과 함께 다른 축 성분이 생긴다)
  - 400 경계값: `seconds: 0`, `seconds: 31`, x·y·z 모두 0, `x: 1.5`
  - on_rails: 레일 가속 중 요청 → 409 `on_rails`
  - 대조: 측정 전용 모드는 두지 않는다(요구 밖). 대신 축마다 다른 두 축 성분이 0 근처인지로 확인한다. 중력이 섞이면 세 축 모두에 펄스와 무관한 큰 값이 나타나므로 이 조건으로 드러난다
  - 펄스 중 `set_rcs off`(계획 중이 아니므로 허용) → 그다음 `get_flight`에서 `ended: rcs_off`
  - MonoPropellant 감소
  - 펄스 중 새 펄스 → 이전 `replaced`
  - 펄스 중 `submit_flight_plan` → `ended: plan`
  - 펄스 중 `revert_flight launch` → 되돌린 뒤 `get_flight.controls.translate.ended` `revert`(펄스 상태는 정적 필드라 씬을 넘어 남고, `vessel_id`로 어느 기체의 펄스였는지 보인다)
  - 궤도 펄스의 `dv_note`는 null
  - 펄스 중 `leave_flight` → 우주센터에서 `get_status.translate.ended` `leave`(recover도 같은 방식)
  - `x: 0.03` → 400(데드존)
  - `docked`·`not_controllable` 사유는 MCP만으로 상황을 만들 수 없어 코드 검토로 확인한다
  - 펄스 중 `reload_mod` → 응답 `released`에 "rcs pulse stopped"
- **선행 단위**: 단위 2

## 단위 4: 계획 단계 키 `rcs`와 SAS 모드·RCS 복원

- **대상**:
  - `Plan`: `StepKeys`에 `rcs`, `Step.Rcs`(bool?)
  - `Coordinator`: 단계 시작 지점에서 `AttitudeController.ApplyStepRcs(step.Rcs)` 호출(Submit의 2단계 `Attach` 뒤, `NextStep`의 `_step++` 뒤, `ReplaceSteps`의 `includeCurrent` 새 현재 단계 시작 뒤. `BeginStep`은 Submit의 시험 계산에서도 불리므로 거기에는 넣지 않는다. Resume은 이미 시작한 단계라 다시 적용하지 않는다), Release(`RestoreSas` → `RestoreRcs` → `Detach`), `CapturePlan`(`sas_mode_before`, `rcs_before`, `rcs_touched`), Resume의 `Attach`(555줄)·`Adopt`(564, 595줄) 호출
  - `AttitudeController`: `_sasModeBefore`, `_rcsBefore`, `_rcsTouched`, `Attach(v, HandedControl h = null)`·`Adopt(v, HandedControl h)`, `ApplyStepRcs`, `RestoreSas`(모드 포함), `RestoreRcs`. `Fly`는 RCS를 건드리지 않는다
  - `Handover`: `HandedPlan.SasModeBefore`(string), `RcsBefore`(bool?), `RcsTouched`(bool?)와 파싱(94줄 옆)
  - `FlightTools` `submit_flight_plan`·`replace_flight_steps` 설명(`rcs` 단계 키), 0.2.23
- **코드 초안**:

```csharp
// Plan.Step.Parse
if (o.TryGetValue("rcs", out object r) && r != null)
    step.Rcs = r as bool? ?? throw Invalid(path + ".rcs", "must be a boolean");

// AttitudeController: Attach / Adopt take the handed-over values (null = record from the vessel now)
_sasBefore = h?.SasBefore ?? v.ActionGroups[KSPActionGroup.SAS];
_sasModeBefore = ParseModeOrNull(h?.SasModeBefore) ?? v.Autopilot.Mode;
_rcsBefore = h?.RcsBefore ?? v.ActionGroups[KSPActionGroup.RCS];
_rcsTouched = h?.RcsTouched ?? false;

/// <summary>Once at each step start (never every tick: override groups and the toggle cooldown can flip it back and forth).
/// rcs given: set it. Omitted after the plan changed RCS: back to the state before the plan. Omitted otherwise: untouched.</summary>
public static void ApplyStepRcs(bool? rcs)
{
    if (_v == null) return;
    bool? want = rcs ?? (_rcsTouched ? _rcsBefore : (bool?)null);
    if (want.HasValue && _v.ActionGroups[KSPActionGroup.RCS] != want.Value)
    {
        _v.ActionGroups.SetGroup(KSPActionGroup.RCS, want.Value);
        if (_v.ActionGroups[KSPActionGroup.RCS] == want.Value)
        {
            _rcsTouched = true;   // only when it really changed (cooldown / override groups leave it unchanged)
        }
    }
}

public static void RestoreSas()
{
    if (_v == null) return;
    _v.ActionGroups.SetGroup(KSPActionGroup.SAS, _sasBefore);
    // Release also runs for vessel_changed / vessel_lost / scene: Enable only on a live, unpacked vessel
    // CanSetMode / SetAutopilot use the active vessel's display velocity: Enable only on the active vessel
    if (_sasBefore && ReferenceEquals(_v, FlightGlobals.ActiveVessel) && _v.loaded && !_v.packed && _v.state != Vessel.State.DEAD)
    {
        VesselAutopilot ap = _v.Autopilot;   // SetGroup alone comes back as stability on the next Update
        ap.Enable(ap.CanSetMode(_sasModeBefore) ? _sasModeBefore : VesselAutopilot.AutopilotMode.StabilityAssist);
    }
}

public static void RestoreRcs()
{
    if (_rcsTouched) _v?.ActionGroups.SetGroup(KSPActionGroup.RCS, _rcsBefore);
}

// Coordinator.CapturePlan
["sas_mode_before"] = ManualControl.ModeName(AttitudeController.SasModeBefore),   // tool name, e.g. "prograde"; null for an unknown value (never throws); read back with ManualControl.ParseModeOrNull
["rcs_before"] = AttitudeController.RcsBefore,
["rcs_touched"] = AttitudeController.RcsTouched,
```

- `HandedControl`: `HandedPlan`의 SAS·RCS 필드 묶음. 이전 로직의 핸드오버에 새 필드가 없으면 null로 받아 "지금 기체에서 기록·touched false"로 둔다(기존 `sas_before`와 같은 방식).
- 로더(`KspBridge.Mod`)는 바꾸지 않는다. 이중 실패 때 RCS 미복원은 설계 0014의 알려진 제약이다.
- **검증**: 빌드, App 테스트. 실측(`reload_mod`, KSP 재시작 불필요):
  - 계획 RCS 사용(휠 토크가 작은 기체, 진공): 두 실행을 같은 절차로 맞춘다. `set_sas on stability`로 회전을 멈춘 뒤 `set_sas on:false`, 그다음 지금 기수에서 90° 떨어진 같은 목표의 attitude 단계(`until: attitude_error < 2`)를 `rcs` 생략으로 한 번, `rcs: true`로 한 번 실행한다. 시간은 `step_started`·`step_completed` 이벤트의 UT 차이로 잰다. 판정: `rcs: true` 쪽 시간이 더 짧고, 단계 전후 `controls.rcs.monopropellant.amount`가 줄었다. 시간 차이 수치는 "실측 결과"에 기록한다
  - RCS 끈 상태에서 `[rcs: true 단계, rcs 생략 단계]` 계획 → 첫 단계 중 `controls.rcs.on` true, 둘째 단계 시작 뒤 false(계획 전 상태), 계획 완료 뒤 false
  - `rcs` 없는 계획 → RCS 불변
  - `rcs: "yes"` → 400 `invalid_plan`
  - 궤도에서 `set_sas on prograde reference:orbit` 뒤 계획 실행·완료 → `controls.sas.mode` prograde 복원(`speed_mode` orbit 확인)
  - `rcs: true` 단계 실행 중 `abort_flight` → SAS 모드와 RCS 복원
  - `rcs: true` 단계 실행 중 `replace_flight_steps include_current:true`로 `rcs` 생략 단계 → 새 단계 시작 뒤 RCS가 계획 전 상태
  - `rcs: true` 단계 실행 중 `reload_mod` → 이어받은 뒤 완료 시 RCS 복원
  - vessel_changed로 중단될 때 이전 기체의 RCS 복원은 MCP로 기체를 바꿀 수단이 없어 코드 검토로 확인한다
- **선행 단위**: 단위 3

## 단위 5: 문서

- **대상**: `ReadMe.md` 도구 표·"SAS·RCS" 절(좌표계, `reference`·`speed_mode`(스톡이 표시 모드를 자동으로 바꿀 수 있어 유지가 보장되지 않음), 오류, 종료 사유, `useThrottle` RCS 부품, `rcs` 단계 키와 복원 제약, 버전 호환 제약), `get_flight`·`get_status` 도구 설명의 `controls`·요약 필드, `abort_flight` 설명(SAS 모드·RCS 복원), 알려진 제약(KSP 종료 때 RCS 미복원)
- **코드 초안**:

```csharp
[McpServerTool(Name = "rcs_translate", Destructive = true)]
[Description("RCS translation pulse on the active vessel for seconds (0, 30] of game time, then zero. Axes in [-1, 1] relative to the control part: " +
    "x right, y top (dorsal), z nose. rcs_off when the RCS group is off, plan_running during a flight plan, on_rails during rails warp, eva_not_supported on EVA. " +
    "A new pulse replaces the running one; get_flight.controls.translate shows progress, dv_local (non-gravitational velocity change in the same axes; " +
    "dv_note flags landed/atmosphere/frame_changed when it is not RCS alone) and the end reason.")]
public Task<string> RcsTranslate(double seconds, double x = 0, double y = 0, double z = 0, CancellationToken ct = default)
    => client.SendAsync(HttpMethod.Post, "api/flight/translate", new { seconds, x, y, z }, ct);
```

- **검증**: 빌드, App 테스트, 도구 설명과 ReadMe·실측 결과 일치 확인.
- **선행 단위**: 단위 4

## 실측 결과

(구현 단위마다 기록. 문서의 `status: done`은 문서 단계 완료를 뜻한다)

### 단위 1 (2026-11-13, Logic 0.2.20, 발사대 TestB, 샌드박스)

- 빌드 오류 0, App 테스트 통과 38/38.
- `reload_mod` 0.2.19 → 0.2.20, `reload_app`에 `set_sas` 포함.
- SAS 끈 상태에서 `set_sas on mode:prograde reference:orbit` → `{on true, mode prograde, speed_mode orbit}`. 다음 `get_flight.controls.sas`도 prograde(Update가 stability로 덮어쓰지 않음).
- 켜진 상태에서 `mode:normal` → 응답과 다음 `get_flight` 모두 normal.
- `mode:target`(목표 없음), `mode:maneuver`(노드 없음) → 409 `sas_mode_unavailable`(목록: stability, prograde, retrograde, normal, antinormal, radial_in, radial_out). `reference:target` → 409. 이후 `get_flight`에서 mode normal·speed_mode orbit 불변.
- `on:false mode:prograde` → 400, `mode:bogus` → 400.
- 발사대에서 `reference:surface mode:prograde` → 409(지표 속도 0이라 surface 표시에서 prograde 불가, 목록에 prograde 없음). 실패 뒤 `speed_mode`가 orbit로 되돌아감(원자성 확인). 검증 기준의 "surface prograde → 200"은 궤도·비행 중 조건이며, 발사대에서는 이 409가 맞는 스톡 판정이다.
- `reference:surface mode:normal` → 200, speed_mode surface(surface 표시에서도 normal 허용, 3차 검토 H1 정정과 일치).
- 계획 실행 중 `set_sas` → 409 `plan_running`. `get_status.flight.sas`는 계획 중 false(Controller가 끔).
- `abort_flight` 뒤 SAS on, mode stability(계획 전 normal). 계획의 SAS 모드 복원은 단위 4 범위로, 지금은 기존 동작 그대로다.
- `group_locked`·`not_controllable`·EVA는 MCP만으로 상황을 만들 수 없어 코드 검토로 확인한다(완료 전 독립 검토).
