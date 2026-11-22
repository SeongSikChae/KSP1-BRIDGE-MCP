# Controller 기본 제어 계획

## 요약

기본 제어를 새 Mod 모듈과 MCP 서버 모듈 쌍(`control`)으로 더한다. 스로틀·자세·스테이징·액션 그룹의 규칙은 모두 새 모듈에 두고, 코어에는 어느 기능에도 매이지 않는 두 가지만 더한다(D001).

- 계획 범위 유지 입력(hold): 모듈이 계획에 하나를 걸면 코어가 그 계획 동안 매 물리 프레임의 조종 입력 통로에서 부르고, 계획이 어떤 사유로 끝나든 푼다. 자세 유지가 이것을 쓴다.
- 계획 종료 동작: 모듈이 계획에 동작을 등록하면 코어가 계획이 끝날 때 종료 사유와 함께 부른다. 스로틀 내림이 이것을 쓴다.

게임에 닿지 않는 부분(인자 검증, 목표 방향, 제어 법칙, 판정 규칙)은 나눠 단위 테스트로 확인하고, 게임에 닿는 부분은 실제 게임에서 MCP 응답으로 확인한다.

이 문서에서 "디컴파일"은 게임 설치 폴더의 `KSP_x64_Data/Managed/Assembly-CSharp.dll`(KSP 1.12.5)을 `ilspycmd -t <형식> <dll>`로 풀어 읽은 것을 뜻한다.

## 바꿀 파일·구조

새 파일(Mod 모듈):

- src/KspBridge.Mod.Modules.Control/KspBridge.Mod.Modules.Control.csproj: `src/KspBridge.Mod.Modules.Measure/KspBridge.Mod.Modules.Measure.csproj`와 같은 구성(net472, 게임 어셈블리 참조, 세대 표식 이름, flight 모듈 참조, 테스트용 `InternalsVisibleTo`).
- src/KspBridge.Mod.Modules.Control/ControlModule.cs: 모듈 등록(단계 종류 다섯, 요청 처리 없음).
- src/KspBridge.Mod.Modules.Control/ControlArgs.cs: 단계 인자 검증과 액션 그룹 이름을 `KSPActionGroup` 값에 잇는 표(게임 상태에 닿지 않음).
- src/KspBridge.Mod.Modules.Control/ThrottleStep.cs: 단계 `throttle`, 스로틀 쓰기, 스로틀 내림 등록.
- src/KspBridge.Mod.Modules.Control/ThrottleRule.cs: 종료 사유와 기체 일치로 스로틀을 내릴지 정하는 규칙(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Control/AttitudeSteps.cs: 단계 `attitude`·`attitude_off`, 활성 기체·조종 가능 여부를 확인하는 `ControlStep`.
- src/KspBridge.Mod.Modules.Control/AttitudeTarget.cs: 목표(피치·헤딩·롤 또는 기준 방향)와 목표 방향·롤 기준 벡터 계산(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Control/AttitudeLaw.cs: 오차와 조종 명령 계산, 정렬 판정(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Control/AttitudeHold.cs: 코어 hold 구현. 기체에서 값을 읽어 `AttitudeLaw`에 넘기고 조종 입력을 쓴다.
- src/KspBridge.Mod.Modules.Control/VesselReader.cs: 기체의 자세·속도·각속도·관성·회전력 읽기.
- src/KspBridge.Mod.Modules.Control/StageStep.cs: 단계 `stage`.
- src/KspBridge.Mod.Modules.Control/StageOutcome.cs: 스테이징 결과 판정과 실패 이유(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Control/ActionGroupStep.cs: 단계 `action_group`.

새 파일(서버 모듈, 테스트, 기준 spec):

- src/KspBridge.Server.Modules.Control/KspBridge.Server.Modules.Control.csproj: `src/KspBridge.Server.Modules.Wait/KspBridge.Server.Modules.Wait.csproj`와 같은 구성.
- src/KspBridge.Server.Modules.Control/ControlModule.cs: `[assembly: KspBridgeModule("control")]`만 둔다. MCP 도구는 없다.
- tests/KspBridge.Mod.Modules.Control.Tests/KspBridge.Mod.Modules.Control.Tests.csproj: `tests/KspBridge.Mod.Modules.Measure.Tests/KspBridge.Mod.Modules.Measure.Tests.csproj`와 같은 구성에 control 모듈 참조.
- tests/KspBridge.Mod.Modules.Control.Tests/ControlArgsTests.cs, ThrottleRuleTests.cs, AttitudeTargetTests.cs, AttitudeLawTests.cs, StageOutcomeTests.cs: 단위 테스트.
- docs/features/flight-control.md: 새 기능의 기준 spec(마지막 구현 단계에서 만든다).

고치는 기존 파일:

- src/KspBridge.Mod.Core/ModuleApi.cs: `IPlanHold`, `IPlanScope`, `StepContext.Plan`을 더한다.
- src/KspBridge.Mod.Core/Coordination/Plan.cs: 계획의 hold와 종료 동작, 진행 포함 요약의 `hold`·`hold_error`.
- src/KspBridge.Mod.Core/Coordination/Coordinator.cs: hold 적용·해제, 종료 동작 실행, 조종 예외와 `Begin` 중 계획 종료 처리.
- src/KspBridge.Mod.Core/Control/Controller.cs: 주석만 고친다(hold와 실행 중 단계가 입력을 쓴다는 설명).
- tests/KspBridge.Mod.Core.Tests/CoordinatorTests.cs: hold·종료 동작·`Begin` 중 종료 시험을 더한다.
- src/KspBridge.Server.Modules.Core/CoreTools.cs: `get_plan`(`hold`)과 `get_flight_events`(새 이벤트) 설명.
- KspBridge.slnx: 새 프로젝트 셋을 더한다.
- ReadMe.md: 계획 단계 다섯과 `hold`, 모듈이 `context.Plan`을 쓰는 법을 더한다. 이 파일은 다른 기준 spec(bridge-connection·debug-tools·deployment·editor·flight-measurement·server-hosting)의 `파일:` 줄에도 있지만, 설명을 더하기만 해 그 기능들의 요구는 바뀌지 않는다.
- docs/features/flight-plan.md: 요구와 이력 반영(마지막 구현 단계).

배포 구성(`Directory.Build.targets`)은 모듈을 이름 패턴으로 모으므로(`_KspModLogicProject`·`_KspServerModuleProject`) 고치지 않는다. 새 패키지는 없다.

## 결정

- 코어에 계획 범위 hold와 종료 동작을 더하고 자세·스로틀 규칙은 모듈에 둔다: 계획의 시작·끝과 조종 입력 통로를 코어가 갖고 있어 해제가 빠지는 경로가 없다 / 검토한 대안: 모듈이 `OnFlyByWire`에 직접 붙음 — 해제·예외 처리·`get_plan` 표시를 모듈마다 다시 만들게 되어 고르지 않음 (D001)
- hold는 실행 중 단계보다 먼저 입력을 쓴다: 단계가 축을 직접 쓰면 단계가 이긴다. 단계 사이의 프레임(앞 단계가 끝나고 다음 단계가 시작되기 전)에도 hold는 적용된다 / 검토한 대안: 단계 뒤에 hold — 뒤에 추가될 비행 단계가 자세를 바꿀 수 없어 고르지 않음
- 스로틀은 `FlightInputHandler.state.mainThrottle`과 활성 기체의 `ctrlState.mainThrottle`을 함께 쓴다: 앞의 값은 게임이 다음 프레임부터 쓰는 값이고 뒤의 값은 그 프레임에 바로 보이는 값이다 / 검토한 대안: 계획 동안 `OnFlyByWire`로 덮어씀 — 계획이 끝나면 사라져 D001과 맞지 않음
- 자세 제어는 축마다 오차에서 목표 각속도를, 목표 각속도에서 회전력 비율을 정한다: 관성과 가용 회전력을 게임에서 읽어 기체마다 조정할 값이 없다 / 검토한 대안: 게임 SAS에 방향을 맡김 — 피치·헤딩을 나타내기 어렵고 SAS가 없는 기체에서 안 됨(D001 C)
- 기준 방향을 정할 수 없으면 정렬되지 않은 상태로 기다린다: spec C5 문구대로이다 / 검토한 대안: 시작 때 실패 — spec이 바뀌어 고르지 않음 (D014)
- `stage`는 `StageManager.ActivateStage(N)`으로 작동한다: 다른 스테이지가 작동하는 되돌릴 수 없는 실수가 없다 / 검토한 대안: `ActivateNextStage` 뒤 확인 — 어긋나면 작동한 뒤에야 알게 되어 고르지 않음 (D015)
- 정렬 기본값은 허용각 2°, 정지 기준 0.5°/s이다: C11의 3° 안에 여유가 있다 / 검토한 대안: 1°·0.5°/s — 발사대에서 정렬되지 않을 수 있어 고르지 않음 (D016)
- 스테이징을 게임이 거절했는지는 호출 동안 `GameEvents.onStageActivate`가 발생했는지로 판정한다: 거절 조건을 따라 만들지 않고 게임이 낸 결과를 본다 / 검토한 대안: `CanSeparate`·`CurrentStage`를 미리 검사 — 게임 조건을 복제해 어긋날 수 있어 고르지 않음 (D010)
- `stage`는 작동한 뒤 `KSPActionGroup.Stage`도 전환한다: 스페이스 키가 둘을 함께 하므로 그 그룹에 걸린 부품 동작도 같이 실행된다 / 검토한 대안: 스테이지만 작동 — 플레이어 조작과 결과가 달라 고르지 않음
- 다음 스테이지 판정은 flight 모듈의 `StageRef`를 쓰고 스테이징 아이콘 판정 한 줄은 모듈에 같은 식으로 둔다: `get_flight`의 다음 스테이지와 같은 값이다(계측 모듈의 `SampleReader.IsStaged`와 같은 방식) / 검토한 대안: flight 모듈의 `IsStaged` 공개 — flight-observation 파일이 바뀌어 고르지 않음
- 서버 모듈은 도구 없이 이름만 둔다: spec이 별도 MCP 도구를 두지 않는다고 정했고 서버는 도구가 없는 모듈을 읽을 수 있다(아래 출처) / 검토한 대안: 서버 모듈을 두지 않음 — C1의 모듈 쌍과 어긋남
- SAS를 쓸 수 없는 시험 기체는 부품이 `probeCoreSphere.v2` 하나인 craft(`C9 No SAS`)로 하고 에디터 도구로 만들어 저장한다: 이 부품 설정에는 `ModuleSAS`가 없다 / 검토한 대안: stock craft — SAS가 없는 것을 찾지 못함 (D009)
- 조종 불가는 `throttle`·`attitude`의 `Begin` 첫 줄과 `attitude`의 `Judge`에서 `Vessel.IsControllable`을 읽어 판정한다: 스로틀·종료 동작·SAS·hold를 건드리기 전에 예외를 던지므로 spec C2·C5의 "바꾸지 않고"가 그대로 성립하고 `AtEnd`도 등록되지 않는다(C3) / 검토한 대안: 게임 이벤트 `onVesselControlStateChange`를 구독 — 상태를 따로 들고 있어야 하고 시작 때의 판정도 따로 필요해 고르지 않음 (D022·D023)

## 인터페이스·데이터

### 게임 동작의 출처

- 스로틀: `FlightInputHandler.FixedUpdate`는 매번 `throttle = state.mainThrottle * 2 − 1`로 시작해 플레이어 입력을 더한 뒤 `state.mainThrottle`에 되돌려 쓰고 `(x + 1) × 0.5`로 바꾼다. 전체 스로틀·차단 키도 `state.mainThrottle`에 1·0을 쓴다. 그 뒤 활성 기체에 `SetControlState(state)`를 부르고 `ctrlState`는 `setControlStates[0]`이다(디컴파일 `FlightInputHandler` 480~540·1212·1988·2433행, `Vessel` 3297·4534행). 그래서 `state.mainThrottle`에 쓴 값은 플레이어가 바꾼 것과 같이 남는다. 조이스틱 스로틀 축을 움직이면 그 값이 이긴다(1259~1306행).
- 엔진은 `vessel.ctrlState.mainThrottle`을 읽는다(디컴파일 `ModuleEngines` 3078행).
- 조종 입력 순서: `Vessel.FeedInputFeed`가 `OnPreAutopilotUpdate`·`OnAutopilotUpdate`(SAS)·`OnPostAutopilotUpdate`·`OnFlyByWire`를 차례로 부른다(디컴파일 `Vessel` 4575~4580행). SAS를 끈 뒤 게임이 실제로 떼는 것은 렌더 프레임의 `VesselAutopilot.Update`지만, 그 전에도 뒤에 불리는 `OnFlyByWire`의 값이 이긴다.
- 기준 방향: 프로그레이드는 `obt_velocity`, 노멀은 `orbit.h.xzy`이다(디컴파일 `VesselAutopilot.SetAutopilotOrbit`). 래디얼 아웃은 내브볼과 같이 천체 중심에서 기체로 가는 방향에서 궤도 속도 방향 성분을 뺀 방향이고 래디얼 인은 그 반대이다(디컴파일 `KSP.UI.Screens.Flight.NavBall` `DrawOrbitalCues`의 `ProjectOnPlane(wCoM − cbPos, obtVel)`과 `radialOutVector`). `AutopilotMode.RadialOut`·`RadialIn`이라는 이름은 실제 방향과 반대이므로(디컴파일 `VesselAutopilot.SetAutopilotSurface`는 `RadialIn`에 `upAxis`, `RadialOut`에 `−upAxis`를 쓴다) 그 식을 쓰지 않는다(D017). 지표 기준은 `srf_velocity`를 쓴다.
- 회전력: `ITorqueProvider.GetPotentialTorque`는 `pos`·`neg` 두 값을 내고 x·y·z는 피치·롤·요 축이다. 게임 SAS는 모든 부품의 `pos`와 `neg`를 따로 더한 뒤 축마다 큰 값을 쓰고, 같은 축의 `Vessel.MOI`로 나눈 값을 최대 각가속도로 쓴다(하한 0.0001)(디컴파일 `VesselAutopilot.VesselSAS.GetTotalVesselTorque`·`UpdateMaximumAcceleration`). 조종면의 `pos`·`neg`는 음수가 될 수 있다(디컴파일 `ModuleControlSurface.GetPotentialTorque`). 반작용 휠은 피치·롤·요 입력을 x·y·z로 놓고 `ReferenceTransform.rotation × −(입력 × 회전력)`을 건다(디컴파일 `ModuleReactionWheel` 289~291·411행). 그래서 기준 변환의 로컬 축에서 회전력 = −명령 × 가용 회전력이다. C11 발사에서 짐벌 엔진의 피치가 발산하지 않았다(2026-11-22, 피치 최소 89.88°). 조종면·RCS의 부호가 같다는 것은 미확인이다.
- 각속도와 관성: `Vessel.angularVelocity`는 기준 변환의 로컬 각속도(rad/s)의 질량 가중 평균이고 `Vessel.MOI`는 같은 축의 관성이다. 각속도가 0이고 기체가 packed이거나, 질량 있는 리지드바디가 없으면 `MOI`를 0으로 둔다(디컴파일 `VesselPrecalculate.CalculatePhysicsStats` 1198~1313행). 로컬 축은 기수 +Y, 위 −Z, 오른쪽 +X이다(`src/KspBridge.Mod.Modules.Flight/FlightSnapshot.cs`의 `Nose`·`Top`).
- 스테이징: `StageManager.ActivateStage(stage)`는 거절할 때(`CanSeparate` 거짓, `_currentStage` 0, 아이콘을 끌고 있음) 소리만 내고 돌아오고, 받아들이면 `GameEvents.onStageActivate`를 발생시킨 뒤 `inverseStage`가 같은 부품을 그 자리에서 `force_activate`한다(디컴파일 `StageManager` 3226~3425행, `Part` 5634행). 작동 뒤 쿨다운은 0.5625초이다(게임 `Physics.cfg`의 `stagingCooldownTimer`). 스페이스 키는 `ActivateNextStage` 뒤 `ToggleGroup(KSPActionGroup.Stage)`를 부른다(디컴파일 `FlightInputHandler` 569행 부근).
- 액션 그룹: `ActionGroupList.ToggleGroup`은 값을 뒤집고 그룹의 부품 동작을 실행하며 `SetGroup`은 값이 다를 때만 `ToggleGroup`을 부른다(디컴파일 `ActionGroupList` 83~233행).
- SAS를 쓸 수 있는지: `VesselAutopilot.VesselSAS.CanEngageSAS()`는 `APSkillExtensions.AvailableAtLevel`을 부른다. 전체 SAS 설정이 꺼진 샌드박스에서 승무원이 없으면 SAS 부품의 수준으로 정한다(디컴파일). `probeCoreSphere.v2`(부품 설정의 이름은 `probeCoreSphere_v2`)의 부품 설정에는 `ModuleSAS`가 없다(게임 폴더 `GameData/Squad/Parts/Command/probeStackSphere_v2/probeStackSphere_v2.cfg`). 이 기체에서 판정이 거짓이 되는 것은 게임에서 확인했다(2026-11-22, 계획 34).
- 조종 불가: `Vessel.IsControllable`은 `Vessel.LateUpdate`가 `FlightGlobals.ready`일 때 부르는 `CheckControllable`이 정하고 `CurrentControlLevel`이 `NONE`보다 클 때 참이다. 조종 수준은 CommNet이 켜져 있고 기체에 통신망 연결(`Vessel.connection`, `CommNetVessel`)이 있으면 `CommNetVessel.GetControlLevel`이 정하고, 조종 부품(`ModuleCommand`)의 상태가 `Full`이나 `Partial`이면 `NONE`이 아니다. 연결이 없으면 부품의 `isControlSource` 가운데 가장 큰 값이다. 잔해로 보는 기체에는 `CommNetVessel`이 없다(`CommNetVessel.OnStart`가 종류가 `Unknown`보다 클 때만 남는다)(디컴파일 `Vessel` 2769~2917·8057행, `CommNet.CommNetVessel`, `ModuleCommand.UpdateControlSourceState`). 전기가 모자라거나, 승무원이 모자라거나, 승무원 없는 탐사선이 신호를 요구하는 설정에서 통신이 끊기면 `NONE`이 된다(모두 디컴파일).
- 계획 단계: `Begin`과 `Judge`는 같은 `FixedUpdate` 호출에서 불리고 예외는 단계 실패가 된다(`src/KspBridge.Mod.Core/Coordination/Coordinator.cs` `FixedUpdate`). 다시 읽기는 `LogicEntry.Stop`이 `Abort(reload)`를 먼저 부르고 구독을 나중에 푼다(`src/KspBridge.Mod.Core/LogicEntry.cs`).
- 서버 모듈: 세대 로더는 어셈블리마다 `KspBridgeModule` 표식을 요구하고 도구 형식은 있는 만큼만 더한다(`src/KspBridge.Server/Modules/ModuleHost.cs` `TryLoad`).

### 코어(`IPlanHold`, `IPlanScope`)

- `IPlanHold`: `Control(FlightCtrlState state, Vessel vessel)`, `Describe()`(요약용 사전).
- `IPlanScope`(`StepContext.Plan`): 지금 `Hold`, `SetHold(hold, reason)`(있던 hold를 reason으로 풀고 건다), `ReleaseHold(reason)`(없으면 거짓), `AtEnd(key, action)`(같은 key는 처음 것만). 끝난 계획의 범위를 쓰면 예외이다.
- hold가 풀릴 때마다 이벤트 `hold_released`(`reason`, `hold`)를 남긴다.
- 끝낼 때 순서: 실행 중 단계의 `End` → hold 해제(사유는 계획 종료 사유) → 종료 동작 → `plan_completed`·`plan_aborted` 이벤트 → `PlanEnded`. 종료 동작의 예외는 잡아 이벤트 `plan_end_error`(`key`, `message`)로 남긴다.
- `Control`: 계획이 있고 조종 예외가 없으면 hold를 부르고, 실행 중 단계가 시작됐으면 단계를 부른다. 어느 쪽이든 예외는 다음 물리 프레임에 지금 단계의 실패가 되고, 아직 시작되지 않은 단계는 시작하지 않고 실패한다.
- `FixedUpdate`: `Begin`이나 `Judge`가 게임 이벤트를 일으켜 그 계획이 끝났으면(예: 활성 기체 변경) 더 진행하지 않는다.
- 진행 포함 요약(`get_plan`의 `current`, 제출 응답)에 `hold`(없으면 `null`)와 `hold_error`(`Describe` 예외 설명)를 더한다. `last`와 `get_status`에는 더하지 않는다.

### 단계 인자

| 단계 | `params` |
|---|---|
| `throttle` | `value`: 0~1, 필수 |
| `attitude` | `pitch`(−90~90)와 `heading`(0 이상 360 미만), 선택 `roll`(−180~180), 또는 `direction` 하나. `tolerance_deg`(> 0, 기본 2), `settle_rate_dps`(> 0, 기본 0.5) |
| `attitude_off` | 없음 |
| `stage` | 없음 |
| `action_group` | `group`(문자열 `"1"`~`"10"` 또는 정수 1~10, `gear`·`light`·`brakes`·`abort`·`sas`·`rcs`), `state`(`on`·`off`·`toggle`) |

수가 아닌 값·범위 밖·빠진 인자는 `Create`에서 `bad_request`이다.

### 단계 동작

- `throttle`: `Begin`에서 활성 기체가 없거나 조종 불가이면 실패한다(이유 `there is no active vessel`·`the vessel is not controllable`. 종료 동작은 등록하지 않는다). 스로틀을 쓰고 `AtEnd("throttle_cut")`을 등록한다(그 시점의 기체를 기억). 종료 동작은 사유가 `planner_abort`·`step_failed`·`timeout`·`reload`이고 지금 활성 기체가 기억한 기체와 같으면 스로틀을 0으로 쓰고 이벤트 `throttle_cut`(`reason`, `from`)을 남긴다.
- `attitude`: `Begin`에서 활성 기체가 없거나 조종 불가이면 SAS와 hold를 바꾸지 않고 실패하고, `Judge`에서도 조종 불가이면 실패한다(이유 `the vessel is not controllable`). SAS가 켜져 있으면 `SetGroup(SAS, false)`를 부르고 `SetHold(hold, "attitude")`를 부른다. `Judge`는 정렬이면 완료한다. 진행은 `target`, `angle_error_deg`, `roll_error_deg`(롤 지정 때만, 아니면 `null`), `rate_dps`, `tolerance_deg`, `settle_rate_dps`이다. 목표 방향을 정할 수 없으면 `angle_error_deg`와 `roll_error_deg`는 `null`이다(D014).
- `attitude_off`: `ReleaseHold("attitude_off")`를 부르고 완료한다.
- hold 요약: `type`(`attitude`), `target`(`pitch_deg`·`heading_deg`·`roll_deg` 또는 `direction`).
- `stage`: 기체의 스테이징 부품으로 N을 정하고(없으면 실패 `no stages left`) N의 부품 목록을 잡아 둔 뒤, `onStageActivate`를 구독한 채 `ActivateStage(N)`을 부르고 바로 푼다. 발생하지 않았으면 실패 `the game refused staging`, N의 부품 가운데 하나도 작동 상태(`ACTIVE`·`DEACTIVATED`)가 아니면 실패 `no part of stage N was activated`, 아니면 `Stage` 액션 그룹을 전환하고 이벤트 `stage_activated`(`stage`)를 남긴다.
- `action_group`: SAS를 켜는 경우 `CanEngageSAS`가 거짓이면 그룹을 바꾸지 않고 실패하고, 참이면 `ReleaseHold("sas")`를 부른다. `on`·`off`는 `SetGroup`, `toggle`은 `ToggleGroup`을 부르고 이벤트 `action_group_changed`(`group`, `state`)에 바뀐 뒤 값을 남긴다.

### 자세 계산

- 목표 방향: 피치·헤딩은 기체의 `up`·`north`·`east`로 만든다. 롤의 목표 위 방향은 `level × cos 롤 + right × sin 롤`이고 `level`은 피치를 올리는 방향, `right`는 헤딩의 오른쪽이라 피치 ±90°에서도 정해진다. 기준 방향은 위 출처의 식이다. 궤도 기준은 궤도 속도, 지표 기준은 지표 속도가 1 m/s 미만이면 방향이 없다. 노멀은 `orbit.h`의 크기가, 래디얼은 `up`에서 속도 방향 성분을 뺀 벡터의 크기가 0에 가까워도 방향이 없다(D014).
- 오차(로컬 축): 기수 (0, 1, 0)에서 목표 t로 가는 최소 회전 벡터이다. 축 (t_z, 0, −t_x), 각 atan2(축의 길이, t_y). 정반대이면 x축으로 돈다. 롤 오차는 최소 회전 뒤의 위 방향과 목표 위 방향 사이의 부호 있는 각을 y 성분으로 둔다. 롤을 지정하지 않았으면 y 성분은 0(롤 속도만 줄임), 방향이 없으면 모두 0이다(제어 계산에만 쓰는 값이며 진행의 오차는 `null`이다).
- 가용 회전력은 축마다 max(모든 부품 `pos`의 합, 모든 부품 `neg`의 합)이고 0 이하이면 0으로 본다(D018). 축마다 최대 각가속도 a = 가용 회전력 ÷ 관성, 목표 각속도 ω_d = sign(e) × min(|e| ÷ 2 s, √(a × |e|), 20°/s), 필요한 회전력 = 관성 × (ω_d − ω) ÷ 0.5 s, 명령 = −(필요한 회전력 ÷ 가용 회전력)을 −1~1로 자른 값이다. 가용 회전력이나 관성이 0이면 그 축의 명령은 0이다(D019).
- 명령의 x·y·z를 `state.pitch`·`state.roll`·`state.yaw`에 쓴다.
- 정렬: 방향이 있고 방향 오차 ≤ 허용각, 롤 지정 때 |롤 오차| ≤ 허용각, 각속도 크기 ≤ 정지 기준.

## 오류·위험

1. 조종면·RCS의 입력 부호가 반작용 휠과 같다는 것은 미확인이다. 다르면 엔진을 끈 대기 비행이나 RCS를 켠 채의 자세가 발산한다. 짐벌은 C11 발사에서 발산하지 않았고, 조종면은 짐벌이 지배하는 그 시험으로 구분되지 않는다(D025). 기준 방향 시험은 반작용 휠이 주로 돌린다.
2. 각속도 부호(v = ω × r)는 계측 계획에서도 미확인이다(docs/specs/계측/plan.md "오류·위험" 3). 제어 법칙은 같은 전제를 쓴다. 기준 방향·피치·헤딩·롤 시험이 모두 정렬되어(2026-11-22) 제어 법칙 안에서는 맞는 것을 확인했다.
3. 제어 상수(2 s, 0.5 s, 20°/s)는 `Kerbal X`(궤도, 130 t)와 작은 기체(0.4 t)에서만 확인했다(2026-11-22). 유연한 기체에서 흔들리면 정지 기준을 채우지 못해 `timeout`이 된다. 알아채는 방법: 단위 테스트의 강체 모의와 C11 시험의 완료 시간·표본 각도.
4. `ActivateStage(N)`에서 N이 게임의 현재 스테이지 번호 − 1과 다를 때의 동작은 미확인이다(D015). 게임이 `_currentStage` 0으로 거절하는 경우도 남는다. 어느 쪽이든 N의 부품이 작동하지 않으면 단계가 실패로 끝나 조용히 넘어가지 않는다. C11의 발사는 두 번호가 같은 경우이다.
5. `stage`와 `abort` 그룹은 기체 분리로 활성 기체를 바꿀 수 있다. 그때 계획은 `vessel_change`로 끝나고 `FixedUpdate`는 더 진행하지 않는다(단위 테스트로 확인).
6. 액션 그룹의 부품 동작 실행(D013)은 MCP 응답에 나오지 않아 게임에서 확인하지 못한다. 구현 후 검토에서 `SetGroup`·`ToggleGroup`을 부르는지 본다.
7. 조종 불가의 판정은 통신망(`EnableCommNet`)과 신호 요구 설정(`requireSignalForControl`)이 켜진 세이브에서 승무원 없는 탐사선이 신호가 끊기면 참이 된다. 신호 요구가 꺼진 세이브에서는 신호가 없어도 조종 불가가 아니다(디컴파일 `ModuleCommand.SignalRequired`). 두 설정이 켜진 세이브의 탐사선 계획은 신호가 없는 동안 `throttle`·`attitude`가 `step_failed`로 끝나며, 이것은 게임이 그 기체를 조종 불가로 본 것이다(D022·D023). 알아채는 방법: 계획의 실패 이유.
8. 도구가 없는 서버 모듈은 처음이었다. `get_status`의 서버 모듈 목록에 `control`이 나오는 것을 확인했다(2026-11-22).
9. 테스트 프로젝트가 게임 열거형(`KSPActionGroup`)의 값을 쓰는 것은 처음이었다. `ControlArgsTests`가 통과한다(2026-11-22).
10. 발사대의 고정대가 자세 제어 중에도 `Kerbal X`를 잡아 두는 것을 확인했다(2026-11-22, C5의 단계 진행 확인). 기울면 C5의 단계 진행 확인에서 각도 오차와 `get_flight` 자세가 함께 바뀌므로 비교는 그대로 성립하고, 기체가 쓰러지면 `get_flight`의 상황과 자세로 드러난다. 확인 뒤 `debug_revert`로 되돌린다.

## 검증 전략

빌드와 단위 테스트:

- `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과한다.
- C2·C4·C9(인자): `ControlArgsTests` — 경계값, 빠짐, 수가 아님, 모르는 이름, 목표 조합, 기본값, 액션 그룹 이름 16개와 `KSPActionGroup` 값의 대응(D021).
- C3: `ThrottleRuleTests` — 여덟 종료 사유와 기체 일치 여부. `CoordinatorTests` — 종료 동작이 hold 해제 뒤 한 번 불리고 예외가 계획 종료를 막지 않음.
- C5(계산): `AttitudeTargetTests` — 여덟 방향(래디얼 아웃이 천체 바깥쪽), 피치 ±90°의 롤, 방향 없음. `AttitudeLawTests` — 오차(정반대 포함), 명령 부호, 가용 회전력 합산(`pos`·`neg`가 음수이거나 서로 상쇄되는 경우), 관성·회전력 0, 정렬 경계, 로컬 회전력 = −명령 × 회전력으로 푼 강체 모의의 수렴(방향·롤).
- C6·C7·C10: `CoordinatorTests` — hold가 뒤 단계와 단계 사이 프레임에 불림, 교체·해제·종료 사유별 이벤트, `hold` 요약(진행 포함 요약에만, 제출 응답은 `null`), hold 예외와 단계 예외가 `step_failed`, `Begin` 중 계획이 끝남.
- C8: `StageOutcomeTests` — 다음 스테이지 없음, 거절, 부품 작동 없음, 성공.

실제 게임(실제 `mcp__kspbridge__*` 호출, 판정은 응답만으로):

- 배포: `dotnet publish src/KspBridge.Mod.Core -c Release`와 `dotnet publish src/KspBridge.Server.Modules.Control -c Release`로 게시하고 핫리로드로 반영한다. 게임 장면을 바꾸기 전에 `get_status`로 다른 비행이 없는지 본다.
- C1: `get_status`의 서버·Mod 모듈에 `control`이 있고 `list_step_types`에 다섯 단계와 인자·완료·실패 조건 설명이 나온다.
- C11: spec의 항목을 그 순서대로 한다. 자세 단계에는 `timeout_s` 300을 준다.
- C2: 계획 `throttle` 0.4 → `measure_start` → `wait` 2초 → `measure_stop` 뒤 계측 스로틀의 최소·최대가 0.4이다.
- C3: 계획 `throttle` 0.3 → `wait` 600초 중 `debug_revert`를 한 뒤 `get_flight_events`에 그 계획의 `throttle_cut`이 없다.
- C6: 계획 `attitude`(prograde) → `attitude`(retrograde, 정렬을 기다리지 않도록 `tolerance_deg` 180·`settle_rate_dps` 1000) → `attitude_off` → `measure_start` → `wait` 5초 → `measure_stop` → `attitude_off`가 완료되고, `hold_released`의 사유가 `attitude`, `attitude_off` 차례로 하나씩이며, 계측의 조종 입력 세 축의 최소·최대가 0이다.
- C7: 계획 `attitude`(피치 30°·헤딩 45°) → `wait` 600초를 실행하고 `wait` 중 `get_plan`의 `current.hold`에 목표가 있으며 `abort_plan`이 그 계획을 `planner_abort`로 끝내고 `last`에 `hold`가 없다. 제출 응답의 `hold`는 `null`이다.
- C8의 거절: 발사대의 `Kerbal X`에서 계획 `stage` → `stage`가 두 번째 단계의 `step_failed`로 끝나고 이유가 거절이다(쿨다운 0.5625초). 판정 뒤 `debug_revert`.
- C5의 단계 진행(D020): 발사대의 `Kerbal X`(스테이징 전, 스로틀 0)에서 계획 `attitude`(피치 0°·헤딩 90°·롤 0°)를 붙잡아 두고 `get_plan`의 `step_progress`에 여섯 값이 있으며, 각도 오차가 이어서 읽은 `get_flight` 자세로 계산한 값과 3° 안에서 같고 `tolerance_deg` 2·`settle_rate_dps` 0.5인지 본 뒤 `abort_plan`이 그 계획을 `planner_abort`로 끝내는지 본다. 이어서 `attitude`(surface_prograde)를 붙잡아 두고 `angle_error_deg`가 `null`인지 보고 같은 방법으로 끝낸다(D014).
- C9의 나머지 그룹(D021): 발사대의 `Kerbal X`에서 `1`~`10`·`gear`·`light`·`brakes`·`abort`를 `toggle`로 두 번씩 실행하는 계획이 `completed`로 끝나고, 그룹마다 `action_group_changed`의 상태가 첫 번째와 두 번째에 서로 반대인지 본다. 판정 뒤 `debug_revert`.
- 조종 불가(C2·C3·C5, D022): 기체를 직접 읽으므로 단위 테스트를 두지 않고, 게임에서 시험하지 않으며(D027), 코드로 확인한다.
- C10의 예외: 게임에서 일으킬 수단이 없어 단위 테스트로만 확인한다.
- C5의 활성 기체 없음은 비행 장면 준비가 계획 제출의 조건이라 게임에서 일으키지 못한다. 코드로 확인한다.

## 원칙 점검

- 적용 규칙:
  - `workflow/spec-driven.md`와 하위 문서, `workflow/repository-content.md`(저장소 기준 상대 경로, 개인 경로 없음)
  - `git/github-access.md`·`git/github-issue.md`(이슈 #32), `git/commit-message.md`
  - `dotnet/project-creation.md`(`src/`·`tests/` 배치, `{Solution}.{역할}` 이름, MSTest, 솔루션에 포함), `dotnet/nuget-package-selection.md`(새 패키지 없음)
  - `shell/powershell-windows.md`(명령 실행), `workflow/conversation-language.md`(대화 언어)
  - 전역 규칙 MCP 도구 검증(게임 검증은 실제 `mcp__kspbridge__*` 호출로만 판정)
  - 저장소 규칙 파일은 없다. `ReadMe.md` "기능 모듈 추가"는 기능을 코어 코드를 고치지 않고 모듈로 더한다고 한다.
- 충돌:
  - `dotnet/project-creation.md` 1번(`net10.0`)을 Mod 모듈과 그 테스트는 따르지 않고 `net472`를 쓴다. 게임(KSP 1.12.5, Unity Mono)이 읽는 어셈블리여야 하고 기존 Mod 프로젝트가 모두 그렇다.
  - `ReadMe.md`의 "코어 코드는 고치지 않습니다"와 달리 코어 네 파일을 고친다. 계획 범위로 유지하는 입력은 계획의 수명을 가진 코어에만 둘 수 있고 사용자가 D001에서 정했다. 더하는 것은 기능에 매이지 않는 hold와 종료 동작뿐이며 자세·스로틀 규칙은 모듈에 있다.
