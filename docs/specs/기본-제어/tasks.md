# Controller 기본 제어 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 코어의 hold와 계획 종료 동작

- [x] T001 `IPlanHold`(`Control`, `Describe`), `IPlanScope`(`Hold`, `SetHold`, `ReleaseHold`, `AtEnd`), `StepContext.Plan`을 더한다 (src/KspBridge.Mod.Core/ModuleApi.cs) — C3, C6, C7
- [x] T002 계획에 hold와 종료 동작 목록을 두고, 진행 포함 요약에 `hold`(없으면 `null`)와 `hold_error`를 더한다. 진행 없는 요약(`last`, `get_status`)에는 더하지 않는다 (src/KspBridge.Mod.Core/Coordination/Plan.cs) — C7
- [x] T003 Coordinator에 `IPlanScope` 구현과 이벤트 `hold_released`, 끝낼 때의 순서(단계 `End` → hold 해제 → 종료 동작 → 계획 종료 이벤트 → `PlanEnded`), 종료 동작 예외의 이벤트 `plan_end_error`, 끝난 계획의 범위 사용 예외를 넣는다. `Control`은 hold를 먼저, 시작된 단계를 그다음에 부르고 단계 사이 프레임에도 hold를 부른다. 조종 예외는 다음 물리 프레임에 지금 단계의 실패가 되고 시작되지 않은 단계는 시작하지 않는다. `Begin`·`Judge` 중 그 계획이 끝났으면 더 진행하지 않는다 (src/KspBridge.Mod.Core/Coordination/Coordinator.cs) — C3, C6, C7, C10
- [x] T004 [P] hold와 실행 중 단계가 입력을 쓴다는 설명으로 주석을 고친다 (src/KspBridge.Mod.Core/Control/Controller.cs) — C6
- [x] T005 T001~T003의 단위 테스트를 쓴다. hold가 뒤 단계와 단계 사이 프레임에 불림, hold가 단계보다 먼저 불림, 교체·해제·종료 사유별 `hold_released`, 진행 포함 요약에만 `hold`가 있고 제출 응답은 `null`, `Describe` 예외는 `hold_error`, 종료 동작이 hold 해제 뒤 한 번 불리고 같은 key는 처음 것만, 종료 동작 예외가 계획 종료를 막지 않음, hold 예외와 단계 예외가 `step_failed`, 시작되지 않은 단계가 시작되지 않음, `Begin` 중 계획이 끝나면 완료 이벤트가 없음 (tests/KspBridge.Mod.Core.Tests/CoordinatorTests.cs) — C3, C6, C7, C10

체크포인트: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과한다.

## 단계 2: 프로젝트 뼈대

- [x] T006 Mod control 모듈 프로젝트를 만든다. 계측 모듈 프로젝트와 같은 구성에 flight 모듈 참조와 테스트용 `InternalsVisibleTo`를 둔다 (src/KspBridge.Mod.Modules.Control/KspBridge.Mod.Modules.Control.csproj) — C1
- [x] T007 [P] 서버 control 모듈 프로젝트를 만든다. wait 서버 모듈 프로젝트와 같은 구성이다 (src/KspBridge.Server.Modules.Control/KspBridge.Server.Modules.Control.csproj) — C1
- [x] T008 [P] 서버 모듈에 `[assembly: KspBridgeModule("control")]`만 둔다 (src/KspBridge.Server.Modules.Control/ControlModule.cs) — C1
- [x] T009 [P] 테스트 프로젝트를 만든다. 계측 모듈 테스트 프로젝트와 같은 구성에 control 모듈 참조를 둔다 (tests/KspBridge.Mod.Modules.Control.Tests/KspBridge.Mod.Modules.Control.Tests.csproj) — C1
- [x] T010 새 프로젝트 셋을 솔루션에 더한다 (KspBridge.slnx) — C1

체크포인트: `dotnet build KspBridge.slnx`가 통과한다.

## 단계 3: 게임에 닿지 않는 부분과 단위 테스트

- [x] T011 [P] 다섯 단계의 인자 검증을 만든다. 스로틀 값, 자세 목표(피치·헤딩·롤 또는 방향 하나), 허용각(기본 2)·정지 기준(기본 0.5), 액션 그룹 이름(문자열·정수 1~10)과 상태, 이름을 `KSPActionGroup` 값에 잇는 표 (src/KspBridge.Mod.Modules.Control/ControlArgs.cs) — C2, C4, C9
- [x] T012 T011의 단위 테스트를 쓴다. 경계값, 빠짐, 수가 아님, 모르는 이름, 목표 조합(둘 다, 둘 다 없음, 하나만, `direction`과 `roll`), 기본값, 액션 그룹 이름 16개와 `KSPActionGroup` 값의 대응 (tests/KspBridge.Mod.Modules.Control.Tests/ControlArgsTests.cs) — C2, C4, C9
- [x] T013 [P] 종료 사유와 기체 일치로 스로틀을 내릴지 정하는 규칙을 만든다 (src/KspBridge.Mod.Modules.Control/ThrottleRule.cs) — C3
- [x] T014 T013의 단위 테스트를 쓴다. 여덟 종료 사유와 기체 일치 여부 (tests/KspBridge.Mod.Modules.Control.Tests/ThrottleRuleTests.cs) — C3
- [x] T015 [P] 목표와 목표 방향·롤 기준 벡터 계산을 만든다. 피치·헤딩·롤, 기준 방향 여덟(래디얼은 `up`에서 궤도 속도 성분을 뺀 방향), 방향을 정할 수 없는 경우(속도 1 m/s 미만, 벡터 크기가 0에 가까움) (src/KspBridge.Mod.Modules.Control/AttitudeTarget.cs) — C4, C5
- [x] T016 T015의 단위 테스트를 쓴다. 여덟 방향(래디얼 아웃이 천체 바깥쪽), 피치 ±90°의 롤, 방향 없음 (tests/KspBridge.Mod.Modules.Control.Tests/AttitudeTargetTests.cs) — C4, C5
- [x] T017 오차(최소 회전, 정반대, 롤), 가용 회전력 합산(축마다 max(Σpos, Σneg), 0 이하이면 0), 조종 명령(목표 각속도, 관성·회전력 0이면 0), 정렬 판정을 만든다 (src/KspBridge.Mod.Modules.Control/AttitudeLaw.cs) — C5, C6
- [x] T018 T017의 단위 테스트를 쓴다. 오차(정반대 포함), 명령 부호, 가용 회전력의 음수·상쇄, 관성·회전력 0, 정렬 경계(방향 없음 포함), 로컬 회전력 = −명령 × 회전력으로 푼 강체 모의의 방향·롤 수렴 (tests/KspBridge.Mod.Modules.Control.Tests/AttitudeLawTests.cs) — C5, C6
- [x] T019 [P] 스테이징 결과 판정과 실패 이유(`no stages left`, `the game refused staging`, `no part of stage N was activated`)를 만든다 (src/KspBridge.Mod.Modules.Control/StageOutcome.cs) — C8
- [x] T020 T019의 단위 테스트를 쓴다. 다음 스테이지 없음, 거절, 부품 작동 없음, 성공 (tests/KspBridge.Mod.Modules.Control.Tests/StageOutcomeTests.cs) — C8

체크포인트: `dotnet test KspBridge.slnx`가 통과한다.

## 단계 4: 게임에 닿는 부분

- [x] T021 [P] 단계 `throttle`을 만든다. 활성 기체가 없으면 실패, `FlightInputHandler.state.mainThrottle`과 `ctrlState.mainThrottle`을 함께 씀, `AtEnd("throttle_cut")` 등록과 이벤트 `throttle_cut` (src/KspBridge.Mod.Modules.Control/ThrottleStep.cs) — C2, C3, C10
- [x] T022 [P] 기체의 자세·속도·각속도·관성·회전력(`pos`·`neg` 합)을 읽는다 (src/KspBridge.Mod.Modules.Control/VesselReader.cs) — C5, C6
- [x] T023 코어 hold 구현을 만든다. 기체 값을 읽어 `AttitudeLaw`에 넘기고 명령의 x·y·z를 `pitch`·`roll`·`yaw`에 쓴다. 방향이 없으면 회전만 멈춘다. `Describe`는 `type`과 `target`이다 (src/KspBridge.Mod.Modules.Control/AttitudeHold.cs) — C5, C6, C7
- [x] T024 단계 `attitude`(활성 기체가 없으면 실패, SAS 끄기, `SetHold`, 정렬 판정, 진행 값 여섯. 방향을 정할 수 없으면 `angle_error_deg`·`roll_error_deg`는 `null`)와 `attitude_off`(`ReleaseHold`)를 만든다 (src/KspBridge.Mod.Modules.Control/AttitudeSteps.cs) — C4, C5, C6
- [x] T025 [P] 단계 `stage`를 만든다. `StageRef`로 N을 정하고 N의 부품 목록을 잡아 둔 뒤 `onStageActivate`를 구독한 채 `ActivateStage(N)`을 부르고 바로 푼다. 성공이면 `Stage` 액션 그룹을 전환하고 이벤트 `stage_activated`를 남긴다 (src/KspBridge.Mod.Modules.Control/StageStep.cs) — C8
- [x] T026 [P] 단계 `action_group`을 만든다. SAS를 켜는 경우 `CanEngageSAS`가 거짓이면 바꾸지 않고 실패, 참이면 `ReleaseHold("sas")`. `SetGroup`·`ToggleGroup`을 부르고 이벤트 `action_group_changed`를 남긴다 (src/KspBridge.Mod.Modules.Control/ActionGroupStep.cs) — C9
- [x] T027 모듈을 등록한다. 단계 종류 다섯, 요청 처리 없음. 각 단계의 설명에 인자(이름·범위·기본값)와 완료·실패 조건을 쓴다 (src/KspBridge.Mod.Modules.Control/ControlModule.cs) — C1

체크포인트: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과한다.

## 단계 5: 도구 설명과 문서

- [x] T028 [P] `get_plan` 설명에 `hold`·`hold_error`를, `get_flight_events` 설명에 새 이벤트(`hold_released`, `throttle_cut`, `stage_activated`, `action_group_changed`, `plan_end_error`)를 더한다 (src/KspBridge.Server.Modules.Core/CoreTools.cs) — C1, C6, C7
- [x] T029 [P] 계획 단계 다섯과 `hold`, 모듈이 `context.Plan`을 쓰는 법을 더한다. 기존 설명의 다른 부분은 바꾸지 않는다 (ReadMe.md) — C1, C7

체크포인트: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과한다.

## 단계 6: 기준 spec 반영

- [x] T030 새 기능의 기준 spec을 만든다. `파일:` 줄, spec.md의 완료 조건을 옮긴 요구(R번호), 이력 한 줄 (docs/features/flight-control.md) — C1, C2, C3, C4, C5, C6, C7, C8, C9, C10, C11
- [x] T031 [P] 계획 범위 hold와 종료 동작, `hold` 요약의 요구와 이력 한 줄을 반영한다 (docs/features/flight-plan.md) — C3, C6, C7, C10

체크포인트: 기준 spec의 `파일:` 줄이 plan.md "바꿀 파일·구조"의 파일과 맞고, 요구가 spec.md의 완료 조건을 빠짐없이 담는다.

## 단계 7: 게시와 게임 검증

게임 검증은 실제 `mcp__kspbridge__*` 도구 호출의 응답으로만 판정하고, spec C11의 판정에는 계획을 제출하기 전이나 끝난 뒤의 응답만 쓴다. 장면을 바꾸기 전에 `get_status`로 게임 상태를 확인하고, 게임이 꺼져 있거나 다른 비행이 진행 중이면 멈추고 사용자에게 알린다. 자세 단계에는 `timeout_s` 300을 준다.

- [x] T032 Mod 세트와 서버 모듈을 게시해 핫리로드로 반영한다. `get_status`의 서버·Mod 모듈에 `control`이 있고 `list_step_types`에 다섯 단계와 인자·완료·실패 조건 설명이 나오는지 확인한다 (파일 없음) — C1
- [x] T033 발사를 확인한다. spec C11 "발사"의 계획과 계측, 판정 뒤 `debug_revert` (파일 없음) — C2, C5, C6, C8, C11
- [x] T034 스테이징 거절을 확인한다. 발사대의 `Kerbal X`에서 계획 `stage` → `stage`가 두 번째 단계의 `step_failed`로 끝나고 이유가 거절이다. 판정 뒤 `debug_revert` (파일 없음) — C8
- [x] T035 단계 진행을 확인한다. 발사대의 `Kerbal X`(스테이징 전, 스로틀 0)에서 plan.md 검증 전략의 C5 계획 둘(`attitude` 피치 0°·헤딩 90°·롤 0°의 여섯 값과 각도 오차, `surface_prograde`의 `angle_error_deg` `null`), `abort_plan`이 `planner_abort`로 끝냄. 판정 뒤 `debug_revert` (파일 없음) — C5
- [x] T036 나머지 액션 그룹을 확인한다. 발사대의 `Kerbal X`에서 `1`~`10`·`gear`·`light`·`brakes`·`abort`를 `toggle`로 두 번씩 실행하는 계획과 `action_group_changed`의 상태. 판정 뒤 `debug_revert` (파일 없음) — C9
- [x] T037 궤도에서 기준 방향 여덟을 확인한다. spec C11 "기준 방향"의 계획, 계측 표본(개수 상한 20)마다 목표 방향과의 각도 3° 이하 (파일 없음) — C4, C5, C6, C11
- [x] T038 피치·헤딩·롤을 확인한다. spec C11 "피치·헤딩·롤"의 계획과 계측 (파일 없음) — C4, C5, C6, C11
- [x] T039 자세 해제와 `hold` 요약을 확인한다. plan.md 검증 전략의 C6 계획(`hold_released` 사유 `attitude`·`attitude_off`, 조종 입력 최소·최대 0)과 C7 계획(`wait` 중 `current.hold`, `abort_plan` 뒤 `last`에 `hold` 없음, 제출 응답 `null`) (파일 없음) — C6, C7
- [x] T040 SAS와 RCS를 확인한다. spec C11 "SAS"의 세 확인 (파일 없음) — C5, C6, C9, C11
- [x] T041 스로틀을 확인한다. spec C11 "스로틀 유지·내림", 계획 `throttle` 0.4 → `measure_start` → `wait` 2초 → `measure_stop`의 스로틀 최소·최대 0.4, 계획 `throttle` 0.3 → `wait` 600초 중 `debug_revert` 뒤 그 계획의 `throttle_cut`이 없음 (파일 없음) — C2, C3, C11
- [x] T042 다시 읽기를 확인한다. T041의 `debug_revert`로 기체가 발사대에 있으므로 `debug_set_orbit`으로 기준 방향 시험과 같은 궤도에 다시 둔 뒤, spec C11 "다시 읽기"의 계획 실행 중 `reload`한다 (파일 없음) — C3, C10, C11
- [x] T043 실패 경로를 확인한다. `C8 Probe Only`의 `stage`, 에디터 도구로 만든 craft `C9 No SAS`(부품 `probeCoreSphere.v2` 하나)의 `action_group`(sas, on), `bad_request` 세 경우와 그 뒤 `get_status` (파일 없음) — C2, C4, C8, C9, C11
- [x] T044 완료 조건마다 실행한 명령과 결과, 확인하지 못한 것(액션 그룹의 부품 동작, 게임에서의 단계 예외, 활성 기체 없음)을 작업 문서 `## 검증`에 적는다 (파일 없음) — C1, C2, C3, C4, C5, C6, C7, C8, C9, C10, C11

체크포인트: 완료 조건 C1~C11마다 확인 결과나 확인하지 못한 이유가 작업 문서에 있다.

## 단계 8: 조종 불가 판정(D022~D024)

- [x] T045 활성 기체가 조종 불가(`Vessel.IsControllable`이 거짓)이면 이유 `the vessel is not controllable`로 실패하는 확인을 `ControlStep`에 더하고, `attitude`의 `Begin`에서 SAS와 hold를 바꾸기 전에, `Judge`에서도 확인한다. `attitude`의 설명(`Describe`)에 실패 조건을 더한다 (src/KspBridge.Mod.Modules.Control/AttitudeSteps.cs) — C1, C5
- [x] T046 `throttle`의 `Begin`에서 `AtEnd` 등록과 스로틀을 쓰기 전에 조종 불가를 확인한다. `throttle`의 설명(`Describe`)에 실패 조건을 더한다 (src/KspBridge.Mod.Modules.Control/ThrottleStep.cs) — C1, C2, C3
- [x] T047 [P] `throttle`·`attitude`가 조종 불가인 기체에서 실패한다는 설명을 더한다. 기존 설명의 다른 부분은 바꾸지 않는다 (ReadMe.md) — C2, C5

체크포인트: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과한다. 조종 불가 판정의 단위 테스트는 두지 않는다(기체를 직접 읽음, plan.md 검증 전략).

## 단계 9: 기준 spec 반영

- [x] T048 spec.md의 조종 불가 변경(용어, C2·C3·C5·C9, 범위 밖)을 요구 목록에 반영한다. 이번 작업의 이력은 이미 있으므로 새 줄을 더하지 않고 그 줄의 바꾼 요구를 맞춘다 (docs/features/flight-control.md) — C2, C3, C5, C9

체크포인트: 기준 spec의 요구가 spec.md의 완료 조건을 빠짐없이 담는다.

## 단계 10: 게시와 재확인

게임 검증의 방법은 단계 7과 같다.

- [x] T049 Mod 세트를 게시해 핫리로드로 반영한다. `list_step_types`의 `throttle`·`attitude` 설명에 조종 불가 실패 조건이 나오는지 확인한다 (파일 없음) — C1
- [x] T053 판정이 들어간 코드로 영향받는 확인을 다시 한다. spec C11 "발사"와 "스로틀 유지·내림" (파일 없음) — C2, C3, C5, C11
- [x] T054 T045~T049·T053의 명령과 결과를 작업 문서 `## 검증`에 더하고, 단계 7의 T033 첫 시도 기록을 맞춘다 (파일 없음) — C1, C2, C3, C5, C11

체크포인트: 단계 8~10의 완료 조건(C1, C2, C3, C5, C9, C11)마다 확인 결과나 확인하지 못한 이유가 작업 문서에 있다.

## 의존 관계

- 단계 1 → 단계 2 → 단계 3 → 단계 4 → 단계 5 → 단계 6 → 단계 7 순서다. 뒤 단계의 코드가 앞 단계의 형식과 프로젝트를 쓴다.
- T001 → T002 → T003 → T005 순서다. 요약과 Coordinator가 hold 형식을 쓰고 테스트가 셋을 쓴다.
- T010은 T006~T009 뒤에 한다(더할 프로젝트 파일이 있어야 한다).
- T011 → T012, T013 → T014, T015 → T016, T017 → T018, T019 → T020 순서다. T017은 T015 뒤에 한다(오차 계산이 목표 벡터 형식을 쓴다).
- T023은 T022 뒤에, T024는 T023 뒤에, T027은 T021·T024·T025·T026 뒤에 한다(등록할 단계 종류가 있어야 한다).
- T030·T031은 단계 1~5가 끝난 뒤에 한다(`파일:` 줄에 실제로 만든 파일을 적는다). 기준 spec 반영은 게임 검증보다 먼저 한다.
- 단계 8~10은 단계 7 뒤에 한다(spec 변경 D022~D024). 단계 8 → 단계 9 → 단계 10 순서다.
- T046은 T045 뒤에 한다(확인 함수가 있어야 한다). T048은 T045~T047 뒤에, T049는 T048 뒤에 한다(기준 spec 반영은 게임 검증보다 먼저 한다).
- T053은 T049 뒤에 하고, T054는 마지막이다.
- T032가 T033~T043보다 먼저다. T033~T043은 번호 순서로 한다(spec C11의 항목은 그 순서이고, 발사대에서 하는 T034~T036을 발사 뒤에 이어서 한다). T043은 T042의 다시 읽기로 바뀐 세대에서 하고, T044는 마지막이다.
