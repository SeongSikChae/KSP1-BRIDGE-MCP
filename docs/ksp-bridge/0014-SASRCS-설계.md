---
status: done
category: ksp-bridge
feature: SASRCS
phase: 설계
issue: 9
---

# SASRCS 설계

방안: [0013-SASRCS-방안.md](0013-SASRCS-방안.md)

## 구조

| 위치 | 변경 |
|---|---|
| `src/KspBridge.Logic/Flight/Control/ManualControl.cs` (신규) | SAS·RCS 설정, 병진 펄스(OnFlyByWire 훅), 상태 출력 |
| `src/KspBridge.Logic/Flight/Control/AttitudeController.cs` | SAS 모드 기록·복원, 계획의 `rcs` 단계 값 적용, RCS 기록·복원 |
| `src/KspBridge.Logic/Flight/Plan.cs` | 단계 공통 키 `rcs`(bool) 파싱 |
| `src/KspBridge.Logic/Flight/Coordinator.cs` | 단계 시작 때 `rcs` 적용, Submit 때 펄스 중단, Release에서 SAS·RCS 복원, 핸드오버, `Describe`·`StatusSummary` |
| `src/KspBridge.Logic/Flight/Handover.cs` | `HandedPlan`에 SAS 모드·RCS 필드와 파싱 |
| `src/KspBridge.Logic/Flight/FlightScene.cs`, `FlightReset.cs` | 계획 중단과 같은 지점에서 펄스 중단 |
| `src/KspBridge.Logic/Handlers/FlightHandler.cs` | `POST /api/flight/sas`, `/api/flight/rcs`, `/api/flight/translate` |
| `src/KspBridge.Logic/BridgeLogic.cs` | FixedUpdate에서 `ManualControl.Tick()`, Stop에서 `ManualControl.Stop()`과 `released` 기록 |
| `src/KspBridge.App.Tools/FlightTools.cs` | `set_sas`, `set_rcs`, `rcs_translate` 도구, `get_flight`·`get_status`·`submit_flight_plan`·`replace_flight_steps`·`abort_flight` 설명 |
| `tests/KspBridge.App.Tests` | ExpectedTools, 요청 매핑 테스트 |
| `ReadMe.md` | 도구 표와 SAS·RCS 절 |

## 인터페이스

### set_sas → `POST /api/flight/sas`

| 인자 | 형식 | 설명 |
|---|---|---|
| `on` | bool, 필수 | SAS 켜기·끄기 |
| `mode` | string, 선택 | stability, prograde, retrograde, normal, antinormal, radial_in, radial_out, target, anti_target, maneuver. `on: true`일 때만 쓴다. 생략하면 켤 때 stability, 이미 켜져 있으면 모드 유지 |
| `reference` | string, 선택 | orbit, surface, target. navball 속도 표시 모드(`FlightGlobals.SetSpeedMode`)를 먼저 맞춘다. `on: true`일 때만 쓴다 |

- navball 속도 표시 모드는 SAS 모드의 기준 프레임을 바꾼다. prograde·retrograde는 표시 속도(지표·궤도·목표 상대) 기준이고, normal·radial도 표시 프레임 기준으로 계산된다(`SetAutopilotOrbit`·`SetAutopilotSurface`·`SetAutopilotTarget`). 사용 가능 여부는 `CanSetMode`만 판정한다. orbit 표시에서만 normal·radial의 벡터 잠금을 검사하고, 다른 표시에서는 검사 없이 허용한다(VesselAutopilot.cs 2572~2632). 스톡은 고도에 따라 표시 모드를 자동으로 바꾼다. 그래서 `reference`로 기준 프레임을 정할 수 있게 하고, 응답과 상태에 `speed_mode`를 표시한다.
- `set_sas`는 원자적으로 처리한다. 시작할 때 표시 모드, SAS 그룹, `Autopilot.Enabled`, `Autopilot.Mode`를 기록하고, 어느 실패 경로(모드 판정, 그룹 변경 미적용, `Enable`·`SetMode` false, 그 밖의 예외)든 이 기록으로 되돌린 뒤 예외를 다시 던진다. 되돌리기는 값이 실제로 달라진 항목만 한다. 특히 `Enabled`·`Mode`가 그대로면 `Enable`을 다시 부르지 않는다(`Enable`은 켜져 있으면 `Disable` 뒤 재초기화해 stability 잠금을 지금 자세로 다시 잡는다). 409면 SAS도 표시 모드도 바뀌지 않는다.
- 판정할 모드는 `mode`, 없으면 SAS가 켜져 있을 때 지금 모드, 꺼져 있으면 stability다. 표시 모드를 바꾼 직후 `CanSetMode`로 한 번만 판정한다. 오류 코드는 판정한 모드로 정한다: stability면 `sas_unavailable`, 그 밖이면 `sas_mode_unavailable`.
- `reference: target`은 `v.targetObject`가 없으면 바꾸기 전에 409 `sas_mode_unavailable`. 파괴된 목표는 이 비교로 걸러지지 않는다(스톡 `TargetLockInvalid`도 같은 방식이라 영향은 작다).
- `SetSpeedMode`는 스톡 UI와 같은 경로지만 `commandedSpeedMode`를 바꾸지 않는다. 씬 전환이나 목표 접근 때 스톡이 표시 모드를 자동으로 바꿀 수 있으므로(FlightGlobals.cs 1934, 4971~5053) `reference`는 유지가 보장되지 않는다. 클라이언트는 `speed_mode`로 확인한다(ReadMe에 적는다).
- 액션 그룹 변경은 막히는 경우를 미리 판정한다. `SetGroup`은 `ToggleGroup`으로 넘어가는데, 추가 액션 그룹(`ADDITIONAL_ACTION_GROUPS`)의 오버라이드(`GroupOverride > 0`이고 `OverrideDefault`나 `OverrideActionControl`에 해당)가 걸려 있으면 오버라이드 목록만 토글하고 실제 그룹 값은 그대로 둔다(ActionGroupList.cs 106~148). 이 토글은 바꾼 뒤에는 되돌릴 수 없으므로 `SetGroup` 전에 같은 조건을 판정해 409 `group_locked`로 막는다. 쿨다운은 아무것도 바꾸지 않으므로 바꾼 뒤 다시 읽어 확인한다. `set_rcs`와 계획의 RCS 적용도 같은 함수를 쓴다(계획은 막히면 그 단계에서 바꾸지 않는다).

- 스톡 `VesselAutopilot`은 SAS가 꺼진 상태에서 `SetMode`를 받으면 모드만 기록하고, 다음 `Update`의 `Enable()`이 `Enable(StabilityAssist)`로 덮어쓴다(VesselAutopilot.cs 2312~2323, 2438~2462). 그래서 켤 때는 `SetMode`가 아니라 `Autopilot.Enable(mode)`를 직접 부른다.
  - 꺼져 있음 → 켜기: 먼저 `CanSetMode(mode ?? StabilityAssist)`를 판정한다. 그다음 `SetGroup(SAS, true)` 뒤 `Enable(mode ?? StabilityAssist)`를 부른다. 반환이 false면 그룹을 끄고 409를 낸다.
  - 켜져 있음 + mode: `SetMode(mode)`의 반환이 false면 409를 낸다.
  - 끄기: `SetGroup(SAS, false)`.
- 응답: 실제 상태 `{on: Autopilot.Enabled && 그룹, mode, speed_mode, available_modes}`.

### set_rcs → `POST /api/flight/rcs`

| 인자 | 형식 | 설명 |
|---|---|---|
| `on` | bool, 필수 | RCS 액션 그룹 켜기·끄기 |

- 응답: `rcs` 객체.

### rcs_translate → `POST /api/flight/translate`

| 인자 | 형식 | 설명 |
|---|---|---|
| `seconds` | number, 필수, 0 초과 30 이하 | 펄스 길이(게임 시간 UT 기준) |
| `x` | number, 기본 0, -1~1 | 조종 부품 `ReferenceTransform.right` 방향(+) |
| `y` | number, 기본 0, -1~1 | 조종 부품 `-ReferenceTransform.forward` 방향(+, 캡슐의 등 쪽) |
| `z` | number, 기본 0, -1~1 | 조종 부품 `ReferenceTransform.up` 방향(+, 기수 쪽) |

- 도구 좌표계(x, y, z)는 위 세 벡터로 정의하고, 응답의 `dv_local`도 같은 좌표계로 보고한다. 그래서 축 방향은 "x만 준 펄스 → `dv_local.x` > 0"처럼 MCP 응답만으로 실측할 수 있다.
- `FlightCtrlState` 매핑: `ModuleRCS`는 `inputLin = ReferenceTransform.rotation * (X, Z, Y)`로 쓴다(ModuleRCS.cs 229~369, 435~437). X는 로컬 x, Z는 로컬 y(up), Y는 로컬 z(forward)에 대응한다. 기체에 가해지는 힘의 부호는 디컴파일만으로 확정하지 않고 `dv_local` 실측으로 고정한다.
- 새 펄스는 진행 중인 펄스를 대신한다(이전 펄스 종료 사유 `replaced`).
- 응답: `translate` 객체.

### 오류

| 코드 | error | 조건 |
|---|---|---|
| 400 | `bad_request` | 인자 형식·범위 오류, `on: false`에 `mode`나 `reference`, x·y·z가 모두 0, 0이 아닌 축 값의 절댓값이 0.05 미만(ModuleRCS 데드존 `EPSILON`, ModuleRCS.cs 117·409~433) (기존 `ApiException.BadRequest`) |
| 409 | `scene_loading` 등 | 기존 `FlightReset.RequireSceneReady`·`RequireNoPendingRecovery` 재사용(세 도구 모두) |
| 409 | `not_in_flight` 등 | 기존 `Guard.ActiveVessel()` 그대로 |
| 409 | `plan_running` | 비행 계획 실행 중(세 도구 모두) |
| 409 | `eva_not_supported` | EVA 기체(세 도구 모두). 제트팩은 입력 경로가 다르다 |
| 409 | `not_controllable` | `v.IsControllable`이 false(세 도구 모두). `OnFlyByWire`는 조종 가능 검사보다 앞에서 불린다(Vessel.cs 4580~4600) |
| 409 | `group_locked` | 액션 그룹 변경이 적용되지 않음(쿨다운·오버라이드). 상태는 되돌린다 |
| 409 | `sas_unavailable` | 모드 없이 켤 때 stability도 쓸 수 없음(SAS 모듈·조종사 없음) |
| 409 | `sas_mode_unavailable` | 요청 모드를 쓸 수 없음. 메시지에 지금 고를 수 있는 모드 목록 |
| 409 | `rcs_off` | 병진 요청 때 RCS 그룹이 꺼져 있음 |
| 409 | `on_rails` | 병진 요청 때 레일 시간 가속 중이거나 기체가 packed |

## 데이터

### get_flight `controls` (신규 그룹)

```json
"controls": {
  "sas": { "on": true, "mode": "prograde", "speed_mode": "orbit", "available_modes": ["stability", "prograde", "retrograde", "..."] },
  "rcs": { "on": true, "parts": 4, "monopropellant": { "amount": 30.0, "max": 30.0 } },
  "translate": { "active": true, "vessel_id": 2192678543, "x": 0, "y": 0, "z": 1, "remaining_s": 1.4, "dv_local": { "x": 0.0, "y": 0.0, "z": 0.21 }, "dv_note": null, "ended": null }
}
```

- `available_modes`: 10개 모드 중 `CanSetMode`가 true인 것.
- `rcs.parts`: `ModuleRCS`(파생 포함)를 가진 부품 수. `monopropellant`: 기체 전체 MonoPropellant 합계. 자원 정의가 없거나 탱크가 없으면 null.
- `translate`: 마지막 펄스 기준. 진행 중이면 `active: true`. 끝나면 `active: false`, `ended`에 종료 사유를 넣고, `dv_local`은 종료 시점 값으로 고정한다. 펄스를 한 번도 하지 않았으면 `translate`는 null. 마지막 펄스는 씬을 넘어 남고(`vessel_id`로 대상 기체 표시), 로직이 바뀌면(reload_mod) 사라진다.
- `dv_local`: 펄스 동안 쌓은 비중력 속도 변화(m/s)를 도구 좌표계(x, y, z)로 나타낸 값. 로직이 직전 틱의 `obt_velocity`, `mainBody`, `mainBody.inverseRotation`을 보관하고, 매 FixedUpdate에서 `(obt_velocity − 직전 obt_velocity) − 직전 graviticAcceleration × 직전 dt`를 그 틱의 조종 부품 축에 투영해 더한다. 중력 항은 Δv와 같은 물리 스텝끼리 짝짓는다(스톡 `perturbation_immediate`와 같은 짝. `VesselPrecalculate`는 가속도를 갱신한 뒤 다음 스텝용 `graviticAcceleration`을 쓴다). 그래서 직전 틱의 `graviticAcceleration`과 dt를 보관한다. `fixedDeltaTime`이 바뀐 틱(물리 가속 배율 변경)은 더하지 않고 `frame_changed`로 건너뛴다. 천체나 회전 프레임이 바뀐 틱은 더하지 않고 기준만 새로 잡는다(`dv_note: "frame_changed"`). 스톡 `acceleration_immediate`는 그런 틱에 갱신되지 않아 묵은 값이 되므로 쓰지 않는다(Vessel.cs 7543). 궤도 속도 차이를 그대로 쓰면 중력으로 1초에 수 m/s 바뀌어 RCS 신호(0.1~0.5 m/s)를 덮으므로 중력 항을 뺀다. 틱마다 그때 자세로 투영하므로 펄스 중 회전해도 축이 섞이지 않는다.
- 비중력 속도 변화에는 RCS 추력 말고도 지면 반력·항력·엔진 추력이 들어간다. 펄스 중 착지 상태(`LandedOrSplashed`)이거나 대기 안(`atmDensity > 0`)이었던 틱이 있으면 `dv_note`에 `"landed"`·`"atmosphere"`를 넣어 값이 RCS만의 결과가 아님을 알린다. 측정은 한 틱 늦으므로(직전 물리 스텝 결과) 펄스가 `done` 외의 사유로 끝나면 마지막 분사 틱이 빠질 수 있다(2초 펄스 기준 약 1%).

- `controls`는 `get_flight`의 다른 그룹과 격리한다. 만드는 중 예외가 나면 `controls`는 null, `controls_error`에 예외 이름과 메시지를 넣는다(`BridgeLogic.Status`의 `input_locks` 패턴). `get_status` 요약 필드도 같은 방식이다.

### get_status `flight` 요약 (추가)

`sas`(bool), `rcs`(bool), `translating`(bool).

### get_status `translate` (추가, 씬 무관)

마지막 펄스 요약 `{active, vessel_id, ended}`(없으면 null). `reset`처럼 격리한다(실패 시 `translate_error`). `flight`는 비행 씬 밖에서 null이라, leave·recover로 우주센터에 간 뒤에도 종료 사유를 MCP로 확인하려고 둔다.

### 계획 단계 공통 키 `rcs`

- 단계가 시작될 때(첫 단계는 Submit, 이후 NextStep, 교체는 ReplaceSteps의 새 현재 단계) 한 번만 적용한다. 단계 중에는 강제하지 않는다(매 틱 `SetGroup`을 다시 부르면 오버라이드 그룹·쿨다운 때문에 그룹이 토글될 수 있다. ActionGroupList.cs 83~150).
  - `true`·`false`: RCS 그룹을 그 값으로 맞춘다. 다시 읽어 실제로 바뀌었을 때만 `rcs_touched`가 된다(바뀌지 않으면 계획은 계속하고 그 단계는 그대로 둔다).
  - 생략: `rcs_touched`면 `rcs_before`로 되돌린다. 아니면 건드리지 않는다(지금 동작). 그래서 자세 단계에서 켠 RCS가 뒤의 추진 단계까지 남지 않는다.
- 계획 시작(Attach) 때 RCS 상태를 `rcs_before`로 기록한다. 계획이 끝날 때(Release) `rcs_touched`면 `rcs_before`로 복원한다. `rcs` 단계에 닿기 전에 끝난 계획은 RCS를 건드리지 않는다.
- 단계 중 사용자가 키로 바꾼 RCS는 그 단계 동안 유지된다. 다음 단계 시작이나 계획 종료 때 위 규칙으로 다시 맞춰진다.
- `useThrottle`이 켜진 RCS 부품은 RCS가 켜져 있으면 추진 중 스로틀에 따라 분사한다(스톡 동작, ModuleRCS.cs 241~253). ReadMe에 알린다.

### 계획의 SAS 모드 복원

- 지금 `Attach`는 SAS 켜짐 여부만 기록하고, `RestoreSas`의 `SetGroup(SAS, true)`는 다음 `Update`에서 stability로 켜진다. `set_sas`로 모드를 맞춘 뒤 계획을 돌리면 끝난 뒤 모드가 바뀐다.
- `Attach`에서 `sas_mode_before`(`Autopilot.Mode`)도 기록한다. 복원 때 SAS를 켜야 하면 `SetGroup(SAS, true)` 뒤 `CanSetMode(sas_mode_before)`이면 `Enable(sas_mode_before)`, 아니면 `Enable(StabilityAssist)`를 부른다.
- `Enable(mode)`는 대상 기체가 활성 기체이고 `loaded && !packed && state != DEAD`일 때만 부른다(`CanSetMode`·`SetAutopilot`은 활성 기체의 표시 속도를 쓴다). Release는 vessel_changed·vessel_lost·scene 사유에서도 이전 기체에 실행되므로, 그 밖에는 기존처럼 `SetGroup`만 한다.
- 복원되는 모드의 의미는 그때의 navball 속도 표시 모드를 따른다(계획은 표시 모드를 기록·복원하지 않는다).

### 핸드오버

- `CapturePlan`에 `sas_mode_before`(도구 모드 이름, 예: "prograde"), `rcs_before`, `rcs_touched`를 넣고, `Handover.HandedPlan`이 읽는다. 없으면(이전 버전 로직) 각각 null·Attach 시점 상태·false로 둔다.
- 로더(`KspBridge.Mod`)는 바꾸지 않는다(0.0.3 그대로). 로더의 `DropHandover`는 지금처럼 `sas_before`만 복원한다.
- 알려진 제약:
  - 새 로직과 이전 로직이 모두 시작하지 못하는 이중 실패 때는 RCS가 복원되지 않는다. 이 경로는 실측할 수 없고 로더 변경에는 KSP 재시작이 필요해, 얻는 것에 비해 비용이 커서 이번 범위에서 뺐다.
  - 로더의 `DropHandover`가 plan의 기체를 확인하지 않고 활성 기체에 `sas_before`를 적용하는 기존 결함은 이번 범위 밖이다(별도 이슈).
  - 계획 중 KSP를 종료하면 받을 로직이 없어 SAS와 마찬가지로 RCS도 복원되지 않는다(기존 SAS와 같은 제약).
  - 새 로직에서 이전 로직으로 되돌리면, 이전 로직의 `Plan.Restore`가 모르는 단계 키 `rcs`를 거부해 계획을 이어받지 못하고(`reload_incompatible`), `rcs_touched`를 몰라 RCS를 복원하지 않는다.

## 병진 펄스 수명

- 시작: 활성 기체의 `OnFlyByWire`에 훅을 걸고 종료 UT를 기록하고 `dv_local`을 0으로 둔다.
- 진행 여부는 별도 플래그(`_active`)와 기체 참조를 `ReferenceEquals`로 판정한다. 파괴된 Unity 객체는 `== null`이 참이 되어 종료 기록을 건너뛰게 되므로 쓰지 않는다.
- 매 입력(`OnFlyByWire`): 대상 기체가 활성 기체이고, 레일 가속·packed가 아니고, RCS 그룹이 켜져 있고, 지금 UT가 종료 UT 전일 때만 X/Y/Z를 쓴다. 판정은 훅 안에서 직접 한다. FlightInputHandler는 로드된 모든 기체의 OnFlyByWire를 부르고, FixedUpdate와 순서가 보장되지 않기 때문이다.
- 훅과 `Tick`은 try/catch로 감싼다. 훅의 예외는 OnFlyByWire 멀티캐스트와 FlightInputHandler의 기체 반복을 끊기 때문이다(Vessel.cs 4577~4580). 예외가 나면 사유 `error`로 끝낸다.
- 매 FixedUpdate(`Tick`): 종료 조건을 판정하고, 진행 중이면 `dv_local`을 쌓는다.
- 종료: Unity 객체에 접근하기 전에 `_active = false`, 사유, 종료 UT를 먼저 기록한다. `dv_local`은 쌓아 둔 값을 그대로 둔다. 그다음 기체가 살아 있으면 훅을 떼고 `ctrlState.X/Y/Z`를 한 번 0으로 만든다(axis override 그룹으로 병진 축이 막힌 기체는 `SetControlState`가 그 축을 덮어쓰지 않아 값이 남는다. Vessel.cs 4534~4564). 각 단계는 따로 예외를 삼킨다.

| 사유 | 조건 | 위치 |
|---|---|---|
| `done` | 종료 UT 도달 | `ManualControl.Tick` |
| `replaced` | 새 펄스 요청 | `Translate` |
| `plan` | `submit_flight_plan` 성공 직전 | `Coordinator.Submit` |
| `scene` | 씬 로드 요청 | `FlightScene` (계획 `scene`과 같은 지점) |
| `vessel_changed` | 활성 기체 변경 | `FlightScene` |
| `vessel_lost` | 펄스 기체 파괴 | `FlightScene`, 안전망으로 `Tick`(`state == DEAD`) |
| `revert` / `leave` / `recover` | 해당 도구 | `FlightReset`. `Coordinator.Running` 검사 밖, `Save()`와 씬 로드보다 앞(계획과 펄스는 동시에 있을 수 없어 `Running` 블록 안에서는 실행되지 않는다) |
| `on_rails` | 레일 가속 시작 또는 packed | `ManualControl.Tick` |
| `rcs_off` | 펄스 중 RCS 그룹이 꺼짐 | `ManualControl.Tick` |
| `not_controllable` | 펄스 중 조종 불가가 됨(`OnFlyByWire`는 조종 가능 검사보다 앞에서 불리고 ModuleRCS는 `ctrlState`를 직접 읽는다) | 훅 판정과 `ManualControl.Tick` |
| `docked` | 펄스 기체에 부품이 결합됨(도킹 뒤 주 기체로 남으면 기체 변경 이벤트가 나지 않는다) | `GameEvents.onPartCouple` |
| `error` | 훅·Tick 예외 | `ManualControl.Fly`·`Tick` |
| `logic_stop` | reload_mod·KSP 종료 | `ManualControl.Stop`. 펄스는 핸드오버하지 않으므로 새 로직에서는 보이지 않는다. 대신 `reload_mod` 응답의 `released`에 "rcs pulse stopped"를 넣는다 |

- `Tick`은 이벤트 훅을 놓친 경우의 안전망으로 씬·활성 기체도 다시 확인한다.

## 결정 목록

1. 방안 1~7절을 그대로 따른다(계획 중 409, 시간 지정 펄스, 단계 공통 키 `rcs`, 도구 3개, 409 + 가능 모드, get_flight 상세·get_status 요약).
2. RCS가 꺼져 있으면 `rcs_translate`는 409 `rcs_off`. 도구가 RCS 상태를 몰래 바꾸지 않는다.
3. 펄스 중 `submit_flight_plan`이 오면 펄스를 멈추고(`plan`) 계획을 시작한다.
4. 로더(`KspBridge.Mod`)는 바꾸지 않는다(4차 검토 뒤 사용자 결정). 이중 실패 때 RCS 미복원은 알려진 제약으로 둔다.
5. 펄스 시간은 게임 시간(UT)으로 잰다. 물리 가속 중에는 게임 시간 기준으로 끝난다.
6. 펄스 최대 30초. 더 긴 분사는 여러 번 요청한다.
7. 펄스는 핸드오버하지 않는다(로직 정지 때 멈춤, `released`에 기록).
8. 펄스 종료 사유는 이벤트가 아니라 `controls.translate.ended`로만 알린다. 비행 이벤트는 계획 단계 기준이라 섞지 않는다.
9. 버전은 단위 커밋마다 Logic `Routes.Version`을 올린다.
10. SAS를 켤 때는 `Autopilot.Enable(mode)`를 직접 부르고 반환값으로 성공을 판정한다. 응답은 실제 상태를 담는다.
11. 병진 축은 조종 부품 기준 도구 좌표계로 정의하고, `dv_local`로 MCP 응답만으로 방향을 실측한다.
12. 펄스 입력은 OnFlyByWire 훅 안에서 매번 조건을 판정한다. 종료는 계획 중단과 같은 이벤트 지점에서 한다.
13. 계획은 SAS 모드도 기록·복원한다.
14. EVA 기체는 세 도구 모두 409 `eva_not_supported`.
15. 400 코드는 기존 `bad_request`를 쓴다.
16. `dv_local`은 비중력 가속을 틱마다 투영·적분한다. 중력이 섞인 궤도 속도 차이는 쓰지 않는다.
17. `set_sas`는 `reference`(orbit, surface, target)로 navball 속도 표시 모드를 정할 수 있고, 응답·상태에 `speed_mode`를 표시한다.
18. 펄스 진행은 플래그로 판정하고, 종료 사유는 Unity 접근 전에 기록한다. 훅·Tick 예외는 격리해 `error`로 끝낸다. 펄스 중 RCS가 꺼지면 `rcs_off`로 끝낸다.
19. 세 도구는 기존 씬 준비·회수 대기 검사를 재사용하고, 병진 축 값은 ModuleRCS 데드존(0.05) 미만이면 400으로 거부한다.
20. SAS 모드 사용 가능 여부는 `CanSetMode`만 판정한다. `reference`는 원자적이다(409면 표시 모드도 되돌린다).
21. 계획 단계 `rcs`는 단계 시작 때 한 번 적용하고, 계획이 RCS를 바꾼 뒤 생략한 단계는 계획 전 상태로 되돌린다.
22. `dv_local`은 로직이 보관한 직전 속도로 `Δv − g·dt`를 계산하고, 프레임이 바뀐 틱은 건너뛴다. 착지·대기 영향은 `dv_note`로 알린다.
23. `controls` 생성 실패는 격리한다(`controls_error`).
24. 액션 그룹 변경은 다시 읽어 확인하고, 적용되지 않으면 409 `group_locked`(도구) 또는 touched로 치지 않음(계획).
25. `set_sas`는 시작 상태를 기록해 모든 실패 경로에서 되돌린다. 오류 코드는 판정한 모드로 정한다.
26. `dv_local`의 중력 항은 직전 틱의 `graviticAcceleration`·dt와 짝짓고, dt가 바뀐 틱은 건너뛴다.
27. 조종 불가 기체는 409 `not_controllable`. SAS 모드 복원의 `Enable`은 활성 기체에만.
28. 그룹 변경이 막히는 오버라이드 조건은 `SetGroup` 전에 판정한다. `set_sas` 되돌리기는 차이 기반이고 모든 예외에 적용한다.
29. 펄스는 도킹(`docked`)과 조종 불가(`not_controllable`)에도 끝낸다. 마지막 펄스 요약을 `get_status.translate`에도 둔다.
30. 핸드오버의 `sas_mode_before` 직렬화는 모르는 값이면 null로 둔다(핸드오버 실패로 번지지 않게). 이전 로직에서 이어받으면 모드는 그 시점의 `Autopilot.Mode`라 stability일 수 있다(알려진 제약).
31. 문서의 `status: done`은 문서 단계 완료를 뜻한다. 개발 문서의 "실측 결과"는 구현 단위마다 채운다.
