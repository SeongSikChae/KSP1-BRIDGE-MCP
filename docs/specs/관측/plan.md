# 관측: 비행 상태 조회와 계획 진행 표시 계획

## 요약

세 부분으로 나눠 만든다.

1. `flight` 모듈 쌍(새 Mod 모듈 `KspBridge.Mod.Modules.Flight`, 새 서버 모듈 `KspBridge.Server.Modules.Flight`)이 `get_flight`를 맡는다. Mod 쪽은 게임 값을 읽는 얇은 층(`FlightSnapshot`)과 게임 없이 시험하는 순수 계산(`FlightReport`·`Attitude`·`StageRef`·`FlightReadiness`)으로 나눈다. Δv·연소 시간·스테이지 TWR은 비행 중 활성 기체의 게임 계산기(`Vessel.VesselDeltaV`) 값을 그대로 쓴다(D003).
2. 코어 계획 실행에 선택적 진행 보고 인터페이스(`IStepProgress`)를 더하고, `Plan`의 요약에 실행 중일 때만 진행 값을 붙인다(D002). `wait` 단계가 이를 구현한다.
3. `debug` 모듈에 `set_orbit.start`·`set_orbit.result`를 더하고, 서버 `debug_set_orbit` 도구가 시작한 뒤 결과를 되묻는다(D008, `check_ship`과 같은 방식). 궤도 적용은 게임의 궤도 설정 치트와 같은 `FlightGlobals.fetch.SetShipOrbit`을 쓴다(D004).

## 바꿀 파일·구조

새 Mod 모듈 `src/KspBridge.Mod.Modules.Flight/`:
- `KspBridge.Mod.Modules.Flight.csproj`: check 모듈 csproj와 같은 틀(net472, Assembly-CSharp·UnityEngine.CoreModule 참조, `InternalsVisibleTo` 시험 프로젝트)
- `FlightModule.cs`: `IBridgeModule`(이름 `flight`). 처리기 `get`(인자 `include`). 준비 판정 → 스냅숏 → 보고서
- `FlightReadiness.cs`: 준비 판정(순수). 입력 구조체와 `Judge`
- `FlightSnapshot.cs`: 게임 값 읽기(활성 기체·궤도·계산기·엔진·자원·천체). 순수 데이터(`FlightData`)로 옮긴다
- `FlightData.cs`: 스냅숏 데이터 형(게임 형식 없음)
- `FlightReport.cs`: `FlightData` → 응답 사전(순수). 열린 궤도 `null`, 현재 TWR, 기준 스테이지·스테이지 표·자원·천체 정보, 계산 준비 표시
- `StageRef.cs`: 마지막으로 작동한·다음·기준 스테이지 판정(순수)
- `Attitude.cs`: 지표 기준 피치·헤딩·롤 계산(순수 벡터)
- `Vec.cs`: 순수 3D 벡터(check 모듈 `Vec`과 같은 모양. 모듈끼리 참조하지 않으려고 따로 둔다)

새 서버 모듈 `src/KspBridge.Server.Modules.Flight/`:
- `KspBridge.Server.Modules.Flight.csproj`: check 서버 모듈 csproj와 같은 틀
- `FlightTools.cs`: `get_flight` 도구(`ReadOnly`, `Idempotent`). 인자 `include`(문자열 배열)

코어(Mod):
- `src/KspBridge.Mod.Core/ModuleApi.cs`: `IStepProgress` 인터페이스 추가
- `src/KspBridge.Mod.Core/Coordination/Plan.cs`: `Plan.ToDict`에 실행 중 진행 값을 붙이는 판(`ToDict(double? now)`)과 진행 계산 `PlanProgress`
- `src/KspBridge.Mod.Core/Coordination/Coordinator.cs`: 진행 값을 붙인 요약을 돌려주는 `CurrentSummaryWithProgress()`를 더하고, `Submit` 응답도 이 판으로. `CurrentSummary`(`get_status`의 `StatusPlan`이 읽음)와 `LastSummary`(끝난 계획)는 진행 값 없이 그대로
- `src/KspBridge.Mod.Core/Status/CoreModule.cs`: `plan.get`의 `current`가 `CurrentSummaryWithProgress()`를 쓰게 함

서버 코어 모듈:
- `src/KspBridge.Server.Modules.Core/CoreTools.cs`: `get_plan`·`submit_plan` 설명에 진행 값 설명 추가(동작 그대로)

wait 모듈:
- `src/KspBridge.Mod.Modules.Wait/WaitModule.cs`: `WaitRun`이 `IStepProgress` 구현(`target_s`, `remaining_s`)

debug 모듈:
- `src/KspBridge.Mod.Modules.Debug/DebugModule.cs`: `set_orbit.start`·`set_orbit.result` 처리기, 장면 전환·모듈 정지 때 대기 취소
- `src/KspBridge.Mod.Modules.Debug/OrbitRequest.cs`: 인자 검증과 Ap·Pe → 장반경·이심률 변환(순수)
- `src/KspBridge.Mod.Modules.Debug/PendingOrbit.cs`: 대기 상태 기계(순수, 10초 시간 초과)
- `src/KspBridge.Mod.Modules.Debug/KspBridge.Mod.Modules.Debug.csproj`: `InternalsVisibleTo` 시험 프로젝트 추가
- `src/KspBridge.Server.Modules.Debug/DebugTools.cs`: `debug_set_orbit` 도구(시작 뒤 결과 되묻기, 15초 상한)

시험:
- `tests/KspBridge.Mod.Modules.Flight.Tests/KspBridge.Mod.Modules.Flight.Tests.csproj`, `AttitudeTests.cs`, `StageRefTests.cs`, `FlightReportTests.cs`, `FlightReadinessTests.cs`
- `tests/KspBridge.Mod.Modules.Debug.Tests/KspBridge.Mod.Modules.Debug.Tests.csproj`, `OrbitRequestTests.cs`, `PendingOrbitTests.cs`
- `tests/KspBridge.Mod.Core.Tests/CoordinatorTests.cs`: 진행 값 시험 추가
- `tests/KspBridge.Mod.Core.Tests/WaitProgressTests.cs`: `wait` 진행 보고 시험(새 파일)
- `tests/KspBridge.Mod.Core.Tests/KspBridge.Mod.Core.Tests.csproj`: wait 모듈 참조 추가

그 밖:
- `KspBridge.slnx`: 새 프로젝트 넷(Mod·서버 flight 모듈, flight·debug 시험) 추가
- `ReadMe.md`: 도구 목록에 `get_flight`·`debug_set_orbit`, `get_plan` 진행 값 설명
- `docs/features/flight-observation.md`(새 기준 spec), `docs/features/flight-plan.md`(새 기준 spec), `docs/features/debug-tools.md`(새 기준 spec)

## 결정

- 모듈 배치: 비행 상태 조회는 새 `flight` 모듈 쌍 (D001). 배포는 프로젝트 이름 규칙(`KspBridge.Mod.Modules.*`, `KspBridge.Server.Modules.*`)으로 `Directory.Build.targets`가 자동으로 넣으므로 배포 파일은 바꾸지 않는다.
- 진행 보고: 단계 실행 객체가 선택적으로 `IStepProgress`를 구현한다 (D002). `IStepRun`을 바꾸지 않아 기존 단계와 다른 모듈은 그대로 컴파일된다. / 검토한 대안: `IStepRun`에 메서드를 더함 — 모든 단계가 구현해야 해 "선택적"이 아니므로 고르지 않음.
- 진행 값 위치: 계획 요약(`current`)의 최상위에 `elapsed_s`, `step_elapsed_s`, `step_timeout_remaining_s`, `step_progress`, `step_progress_error`를 둔다. 단계 목록(`steps[]`) 항목은 바꾸지 않는다. / 검토한 대안: 실행 중 단계 항목 안에 둠 — Planner가 단계 목록에서 실행 중 항목을 찾아야 해 고르지 않음.
- 진행 값 계산 시각: `get_plan`·`submit_plan` 처리 때 코어 시계(UT)로 계산한다. 진행 보고를 부르기 전 그 단계의 `StepContext.UT`를 지금 UT로 맞춘다(다음 물리 프레임에 `FixedUpdate`가 다시 맞추므로 판정에 영향 없음). / 검토한 대안: 마지막 물리 프레임 값 저장 — 요청 시각과 어긋나 고르지 않음.
- 오류 코드 범위(D021·D022·D023): 새 도구 `get_flight`·`debug_set_orbit`만 C10 규칙(`FlightReadiness`와 debug 모듈 준비 판정)으로 `not_ready`·`bad_request`를 낸다. 기존 도구의 오류 처리 코드는 바꾸지 않는다(`get_plan`은 오류 없는 조회 그대로, `submit_plan`·`submit_wait`는 `Coordinator.Submit`의 지금 규칙 그대로).
- Δv 출처: 비행 중 `vessel.VesselDeltaV`의 `IsReady`·`GetStage(n)`·`TotalDeltaVActual`·`TotalDeltaVVac` (D003). 스테이지 값은 `deltaVActual`·`deltaVatASL`·`deltaVinVac`, `TWRActual`·`TWRASL`·`TWRVac`, `stageBurnTime`, `startMass`·`endMass`.
- 스테이지 번호 집합: spec C5대로 계산기 `vessel.VesselDeltaV.OperatingStageInfo`의 스테이지 가운데 번호가 기준 스테이지 이하인 것. 발사대에서 `editor_get_stages`와 집합이 같은지는 게임에서 확인하고(C5), 어긋나면 3절 3번대로 원인을 나눈다 (D014).
- 스테이지 판정(`StageRef`, D006·D018·D019·D020): 게임 `vessel.currentStage`를 `c`라 한다. 판정은 스테이지가 있는 부품(스테이징 아이콘이 있는 부품. check 모듈 `IsStaged`와 같은 판정)의 작동 상태(`Part.State`)로 한다. 분리를 마친 분리기는 스스로 아이콘을 지우므로(`ModuleDecouplerBase`가 `stagingEnabled`를 끄고 `Part.UpdateStageability`가 아이콘을 지움) 세지 않는다. spec 용어 정의와 같다(D020).
  - 마지막으로 작동한 스테이지: 스테이지가 있는 부품 가운데 상태가 `ACTIVE` 또는 `DEACTIVATED`(이미 작동함)인 부품이 하나라도 남아 있으면 `c`, 없으면 `null`. 상황은 보지 않는다.
  - 다음 스테이지: 상태가 `IDLE`(아직 작동하지 않음)인 스테이지 부품의 최대 스테이지 번호. 없으면 `null`.
  - 기준 스테이지: 마지막으로 작동한 스테이지. 그것이 `null`이면 다음 스테이지.
  - 기준 스테이지 값: 계산기 `GetStage(기준)`이 값을 내지 않으면(`null`) Δv·연소 시간·TWR은 0.
  - 디컴파일 근거:
    - 스테이지를 작동하면 게임은 그 스테이지 부품마다 `Part.activate` → `force_activate`를 불러 상태를 `ACTIVE`로 바꾼다. 작동하지 않은 부품은 `IDLE`로 남는다.
    - 비행 중 스테이지 아이콘은 `stages[Min(inverseStage, currentStage)]`에 들어간다. 그래서 아이콘이 있는 작동 부품은 모두 맨 위 그룹(번호 `c`)에 있다. 그룹이 비었는지는 아이콘으로 정해진다(`DeleteEmptyStages`는 `Icons.Count == 0`인 그룹을 지움). 그래서 아이콘이 있는 작동 부품이 하나라도 남아 있으면 그 그룹은 지워지지 않고, `c`는 마지막으로 작동한 스테이지 번호 그대로다.
    - 아이콘이 있는 작동 부품이 모두 없어지면(떨어지거나 분리기가 아이콘을 지움) 빈 맨 위 그룹을 지우며 `currentStage`가 줄어든다. 그 뒤의 `c` 값에는 기대지 않는다. 그때는 아이콘이 있는 작동 부품이 없으므로 마지막으로 작동한 스테이지는 `null`이다.
    - `ActivateStage`는 아이콘 여부와 상관없이 `inverseStage == stage`인 부품을 모두 작동시킨다. 그러나 아이콘이 없는 부품은 그룹을 비지 않게 하지 못하므로 판정에 넣지 않는다(spec 정의와 같음).
    - 다음 스테이지를 `c − 1`로 두지 않고 `IDLE` 부품에서 구하는 것도, 분리 뒤 `c` 값을 확정할 수 없기 때문이다.
    - 발사 전과 스테이징 없이 `SetShipOrbit`으로 옮긴 기체(`currentStage`를 바꾸지 않음)는 모든 부품이 `IDLE`이다. 그래서 마지막으로 작동한 스테이지는 `null`이고 다음 스테이지는 부품 최대 번호다.
- 자세(`Attitude`): 기체의 `up`·`north`·`east`(게임이 매 프레임 계산하는 지표 기준 벡터)와 기준 변환(`ReferenceTransform`, 내비볼의 기준과 같다)의 기수 `f = transform.up`·윗면 `t = −transform.forward`로 계산한다.
  - 피치 = asin(f·up)
  - 헤딩 = atan2(f·east, f·north)를 0~360으로
  - 롤 = 수평 기준 윗면 `u0 = normalize(up − (up·f)f)`와 `t` 사이 각. 부호는 `f` 축 둘레로 오른쪽 날개가 내려가면 +(내비볼 표시와 같은 쪽)
  
  게임 쿼터니언(`Quaternion.LookRotation` 등)은 네이티브 호출이라 단위 시험에서 돌지 않으므로 순수 벡터로 쓴다. / 검토한 대안: 내비볼 식 `Euler(90,0,0)·Inverse(rot)·LookRotation(north, up)`을 그대로 씀 — 시험할 수 없어 고르지 않음.
- 지형 위 고도: 해발 고도 − 지형 높이. 지형 높이 = `body.TerrainAltitude(위도, 경도, allowNegative: true)`(게임 함수, `pqsController.GetSurfaceHeight − radius`. PQS가 없으면 0). 천체에 바다가 있으면(`body.ocean`) 음수 지형 높이를 0(수면)으로 자른다. `allowNegative: false`는 바다 여부와 상관없이 음수를 0으로 자르므로 쓰지 않는다(디컴파일 확인). 고도와 상관없이 계산한다. `vessel.radarAltitude`는 높은 고도에서 해발 고도로 바뀌므로 쓰지 않는다 (D017).
- 현재 추력: 활성 기체 부품의 모든 `ModuleEngines`(`ModuleEnginesFX` 포함) `finalThrust` 합(kN). 현재 TWR = 현재 추력 ÷ (질량 × |`FlightGlobals.getGeeForceAtPosition(CoMD)`|). 질량이나 중력이 0이면 0.
- 질량: `vessel.GetTotalMass()`(t).
- 조종 상태: 스로틀 `vessel.ctrlState.mainThrottle`, SAS·RCS `vessel.ActionGroups[KSPActionGroup.SAS/RCS]`.
- 궤도 요소: `vessel.orbit`의 `ApA`·`PeA`·`inclination`·`eccentricity`·`semiMajorAxis`·`period`·`timeToAp`·`timeToPe`. 이심률 1 이상이면 `period`·`ApA`·`timeToAp`는 `null`.
- 경도는 −180~180으로 맞춘다(게임 값은 범위 밖일 수 있다).
- 천체 정보: `Radius`, `gravParameter`, `rotationPeriod`, `atmosphere ? atmosphereDepth : null`, `sphereOfInfluence`(무한이면 `null`. 코어 `Json.Write`가 무한을 `null`로 쓰지만 보고서에서 명시적으로 `null`로 둔다).
- 자원 목록: 활성 기체 부품의 `PartResource`를 자원 이름별로 `amount`·`maxAmount` 합계. 이름 순.
- `include` 인자: `stages`·`resources`·`body`의 목록. 모르는 값이나 문자열이 아닌 값은 `bad_request`.
- 궤도 설정 변환(`OrbitRequest`): rA = R + Ap, rP = R + Pe, 장반경 = (rA + rP)/2, 이심률 = (rA − rP)/(rA + rP). 승교점 경도·근점 인수·평균 근점 이각은 0, 기준 시각은 지금 UT. 천체 번호는 `FlightGlobals.Bodies`의 순서.
- 궤도 설정 검증:
  - Pe 하한: 대기가 있으면 `atmosphereDepth`. 대기가 없으면 게임이 지형 최고 높이로 쓰는 `pqsController.radiusMax − pqsController.radius`(디컴파일: `Vessel`이 같은 식을 씀). 이 값이 0 이하이거나 PQS가 없으면(빌드되지 않은 천체) 지형 높이를 알 수 없다는 이유로 `bad_request` (D013).
  - Ap 상한: rA < `sphereOfInfluence`(무한이면 검사 안 함).
  - 장반경 상한: `SetShipOrbit`은 장반경을 영향권 × 0.99로 자른다(디컴파일 확인). 그래서 장반경이 0.99 × 영향권을 넘으면 게임이 자르는 범위라는 이유로 `bad_request`(영향권이 무한하면 검사 안 함) (D015).
- 궤도 적용 확인(D008): `set_orbit.result`를 물을 때마다 활성 기체가 지정 천체에 있고, 상황이 `ORBITING`이며, `ApA`·`PeA`가 지정값과 1 km 안이면 끝으로 본다. 시작부터 10초(실시간)가 지나면 시간 초과 `not_ready`. 대기 중인 요청이 있으면 새 `set_orbit.start`는 `bad_request`. 장면 전환·모듈 정지 때 대기를 취소한다.
- 궤도 시험 기체: 궤도 설정 확인(C9·C11 지정 궤도)은 발사 고정대가 없는 stock `Aeris 3A`로 한다. `Kerbal X`(고정대 12개)·`ComSat Lx`(고정대 2개)는 발사 고정대가 붙은 채 궤도로 옮기는 동작을 확인하지 않았기 때문이다.
- 발사대 좌표 기준값: stock 발사대(LaunchPad) 위도 −0.0972°, 경도 −74.5577°. 게임 코드에는 좌표 상수가 없고 장면 데이터에만 있어 디컴파일로는 얻을 수 없다. 그래서 공개된 stock 값을 쓰고, 검증에서 0.01°(약 100 m) 허용치로 대조한다.

## 인터페이스·데이터

`IStepProgress`(코어 `ModuleApi.cs`):

```csharp
/// <summary>Optional on an IStepRun: how far the step is from its goal, shown by get_plan.</summary>
public interface IStepProgress
{
    /// <summary>Called on request (main thread) with the context's UT set to now; return null when there is nothing to show.</summary>
    Dictionary<string, object> Progress(StepContext context);
}
```

계획 요약 추가 필드(실행 중 계획만, `current`와 `submit_plan`·`submit_wait` 응답. `submit_wait`는 wait 모듈이 `Coordinator.Submit`을 그대로 부르므로 코드 변경 없이 같은 요약을 돌려준다):

| 필드 | 값 |
|---|---|
| `elapsed_s` | 지금 UT − `submitted_ut` |
| `step_elapsed_s` | 현재 단계가 시작됐으면 지금 UT − 그 단계 `start_ut`, 아니면 `null` |
| `step_timeout_remaining_s` | 시작됐고 `timeout_s`가 있으면 max(0, `timeout_s` − `step_elapsed_s`), 아니면 `null` |
| `step_progress` | 시작됐고 `IStepProgress`를 구현하면 그 반환값, 아니면 `null` |
| `step_progress_error` | 진행 보고가 예외를 던졌을 때 `"<예외 형식>: <메시지>"`, 아니면 `null` |

`wait` 진행: `{"target_s": <seconds>, "remaining_s": max(0, seconds − elapsed)}`.

Mod 경로 `flight/get`, 인자 `{"include": ["stages","resources","body"]}`(생략 가능). 응답:

```json
{
  "vessel": "Kerbal X", "situation": "PRELAUNCH", "body": "Kerbin", "ut": 0, "met_s": 0,
  "position": {"latitude_deg": 0, "longitude_deg": 0, "altitude_m": 0, "terrain_altitude_m": 0},
  "speed": {"surface_mps": 0, "orbital_mps": 0, "vertical_mps": 0, "horizontal_surface_mps": 0},
  "orbit": {"apoapsis_m": 0, "periapsis_m": 0, "inclination_deg": 0, "eccentricity": 0, "semi_major_axis_m": 0,
            "period_s": 0, "time_to_apoapsis_s": 0, "time_to_periapsis_s": 0},
  "attitude": {"pitch_deg": 0, "heading_deg": 0, "roll_deg": 0},
  "control": {"throttle": 0, "sas": false, "rcs": false},
  "mass_t": 0, "thrust_kn": 0, "twr": 0,
  "stage": {"last_activated": null, "next": 5, "reference": 5,
            "delta_v_mps": 0, "delta_v_vac_mps": 0, "burn_time_s": 0, "twr": 0,
            "total_delta_v_mps": 0, "total_delta_v_vac_mps": 0},
  "delta_v_ready": true, "delta_v_not_ready_reason": null,
  "stages": [{"stage": 5, "start_mass_t": 0, "end_mass_t": 0, "delta_v_mps": 0, "delta_v_asl_mps": 0, "delta_v_vac_mps": 0,
              "twr": 0, "twr_asl": 0, "twr_vac": 0, "burn_time_s": 0}],
  "resources": [{"name": "LiquidFuel", "amount": 0, "max": 0}],
  "body_info": {"name": "Kerbin", "radius_m": 0, "gm_m3ps2": 0, "rotation_period_s": 0, "atmosphere_top_m": 70000, "soi_m": 0}
}
```

`delta_v_ready`가 `false`이면 `stage`의 Δv·연소 시간·TWR 값과 `stages`(요청했으면)는 `null`이다. `stages`·`resources`·`body_info`는 요청했을 때만 키가 생긴다.

Mod 경로 `debug/set_orbit.start`, 인자 `{"body": "Kerbin", "apoapsis_m": 120000, "periapsis_m": 90000, "inclination_deg": 30}` → `{"request": 1, "pending": true}`. `debug/set_orbit.result` 인자 `{"request": 1}` → 대기 중 `{"request": 1, "pending": true}`, 끝 `{"applied": true, "body", "situation", "apoapsis_m", "periapsis_m", "inclination_deg"}`, 시간 초과·취소는 `not_ready`.

## 오류·위험

- 비행 중 계산기가 늦게 준비됨: `IsReady`가 거짓이면 C3대로 표시한다. 계산기 객체가 없을 때(`VesselDeltaV == null`)도 같은 표시로 둔다.
- 궤도 설정 직후 기체가 레일에 묶여 있다(`HoldVesselUnpack(10)`, 디컴파일 확인). 상황 값이 바뀌는 시점은 실측으로 확인한다. 10초 안에 바뀌지 않으면 `not_ready`가 나므로, 검증에서 실패하면 원인을 찾아 고친다(재시도로 넘기지 않는다).
- 스테이징 뒤 스테이지 판정: 이번 작업에서는 스테이징할 MCP 수단이 없어 발사대(스테이징 전)와 스테이징 없이 옮긴 궤도만 게임에서 볼 수 있다. 분리 뒤·점화 뒤 판정은 `StageRefTests`로만 확인하고, 실제 확인은 #32에서 한다.
- 롤 부호: 이번 작업에서는 활주로 롤 크기만 실제로 확인한다(C11). 부호는 단위 시험으로만 확인하고, 실제 확인은 조종 단계(#32)에서 한다.
- 발사대 좌표: 기준값은 디컴파일로 얻을 수 없는 공개 stock 값이다. 실측이 0.01° 밖이면 기준값의 출처를 다시 확인한다(의도 변경 여부를 3절 3번대로 나눈다).
- 진행 보고 예외: 요청 처리 중이므로 잡아서 `step_progress_error`로 보이고 계획에는 손대지 않는다.
- 진행 값 없는 요약을 쓰는 기존 경로는 그대로 둔다. `Finish`의 `LastSummary`와 `get_status`의 `StatusPlan`(`CurrentSummary`를 읽음)이 그렇다. 그래서 `get_status`는 진행 보고를 부르지 않는다. `StatusPlan`이 읽는 키도 그대로 있다.
- 모듈 핫리로드 중 대기 중인 궤도 요청: 모듈 정지 구독에서 취소한다. 이미 호출한 `SetShipOrbit`은 되돌리지 않는다(궤도는 이미 바뀐 상태로 남는다).

## 검증 전략

빌드와 단위 시험은 `dotnet build`·`dotnet test`로 확인한다. 실제 게임 확인은 실행 중인 Claude Code에서 실제 `mcp__kspbridge__*` 호출로만 한다(MCP 검증 규칙). 장면을 바꾸기 전에 `get_status`로 다른 비행이 없는지 본다.

- C1·C2·C6: 실제 `get_flight` 호출로 필드 구성을 확인하고, `include` 유무에 따라 응답 키를 비교한다. `FlightReportTests`로 필드·`null` 규칙을 확인한다.
- C3: `FlightReportTests`(계산기 준비 전 입력 → `null`과 표시). 실제로는 `debug_launch` 직후 호출에서 표시가 나오면 기록한다(나오지 않아도 실패가 아니다).
- C4: `ComSat Lx`를 `editor_load`한 뒤 `check_ship`(Kerbin)을 부른다. 그다음 `debug_launch`(`ComSat Lx`)로 발사대에 올리고 `get_flight include=["stages"]`를 부른다. 스테이지마다 해면·진공 Δv를 1% 안에서 대조한다.
- C5: `FlightReportTests`(표 범위·자원 합계·천체 정보·모르는 항목 `bad_request`). 실제로는 `Kerbal X` 발사대에서 `stages`의 번호 집합과 `editor_get_stages` 집합을 대조한다.
- C7·C8: `CoordinatorTests`(시작 전·단계 사이 `null`, 실행 중 값, 진행 보고 예외, 끝난 계획에 없음)와 `WaitProgressTests`. 실제로는 비행 장면에서 `submit_plan`(wait 30초, timeout 60초) 응답과 몇 초 뒤 `get_plan`으로 값을 확인하고, `submit_wait`(10초) 응답에도 같은 진행 값이 있는지 확인한다.
- C9·C10: `OrbitRequestTests`·`PendingOrbitTests`(검증 규칙·변환·시간 초과)와 `FlightReadinessTests`. 실제로는 다음을 확인한다.
  - `Aeris 3A` 활주로에서 `debug_set_orbit`(Kerbin, 120 km/90 km/30°)을 부른 뒤 첫 `get_flight` 값
  - 잘못된 인자(Ap < Pe, Pe 60 km, 없는 천체)에 `bad_request`
  - 대기가 없는 천체 하한: Kerbin 궤도에서 `debug_set_orbit`(Mun, Ap 50 km, Pe 1 km)에 `bad_request`(지형 높이 하한 또는 알 수 없음)
  - 계획 실행 중 호출에 `bad_request`
  - 우주센터에서 `get_flight`·`debug_set_orbit`에 `not_ready`
- C11: 실제 호출 값으로 계산해 대조한다(발사대 `Kerbal X`, 질량·자원 합계 `ComSat Lx`(부품마다 `editor_get_part_settings`), 두 기체의 발사대 지형 높이 비교, 활주로 `Aeris 3A`, 지정 궤도 `Aeris 3A`). 계산식과 결과는 보고에 남긴다.
- C12: 기준점부터의 `git diff --stat`으로 바뀐 파일이 허용 목록 안인지 본다. 서버 모듈·Mod 세트를 게시해 핫리로드하고, 실행 중인 Claude Code를 다시 연결하지 않고 `get_flight`·`debug_set_orbit`이 도구 목록에 나타나 호출되는지 본다.

## 원칙 점검

- 적용 규칙:
  - Bizadm `workflow/spec-driven.md`, `git/github-issue.md`, `workflow/repository-content.md`(저장소 문서에 개인 경로 없음)
  - 전역 `mcp-verification.md`(실제 `mcp__` 호출로만 검증), `language-preference.md`
  - 저장소 기준 spec `assembly-check`(이번에 바꾸지 않음)
  - 작업 원칙: 기능은 코어 밖 모듈로 더함, 게임 동작은 디컴파일이나 실측으로 확인한 뒤 설계, 장면·상태 전환 직후 실패는 재시도로 넘기지 않고 Mod 결함으로 고침, 입력 잠금(창 포커스)으로 준비를 판정하지 않음, 세션 없는 요청 단위 설계
- 충돌: 없음.
  - 코어 변경(진행 보고)은 spec C12가 허용한 범위이고 `IStepRun`은 그대로다.
  - 준비 판정은 코어 장면 차단 사유와 게임 상태만 보고 입력 잠금은 보지 않는다.
  - 궤도 적용 대기는 한 도구 호출 안에서 Mod에 되묻는 방식이라 MCP 세션 상태에 기대지 않는다.
