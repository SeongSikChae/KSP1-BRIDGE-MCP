# 조건 종료 판정과 안전 중단 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 기준 값

- [x] T001 게시 전의 값 기록: 게임 상태를 `get_status`로 확인하고 다른 비행을 건드려도 되는지 사용자에게 확인한 뒤, 시험 기체 `C33 Loss Test`를 에디터 도구로 만들어 저장하고(부품 이름은 `editor_list_parts`, 연소 시간·TWR은 `check_ship`), 발사대에 올려 `get_flight`(`include` 셋 포함) 응답을 작업 문서에 적는다 — C2

체크포인트: 시험 기체가 저장되어 있고 게시 전 `get_flight` 응답이 작업 문서에 있다.

## 단계 2: 코어 확장점

- [x] T002 `IPlanGuard`·`GuardOption`·`GuardTrip`, `StepContext.PlanId`·`StepIndex`, `ModuleContext.AddGuard` (src/KspBridge.Mod.Core/ModuleApi.cs) — C4·C5·C9
- [x] T003 `GuardRegistry`: 우선순위 순 목록, 키 중복 예외 (src/KspBridge.Mod.Core/Router.cs) — C5·C9
- [x] T004 종료 사유 `vessel_destroyed`·`part_loss`·`no_thrust`, 계획의 `abort_on`과 요약 (src/KspBridge.Mod.Core/Coordination/Plan.cs) — C5·C6·C7·C8
- [x] T005 Coordinator: `abort_on` 검증, 감시 `Start`·`Check`(프레임 맨 앞, 완료 직전)·`Stop`, 예외 처리, 단계 번호 채우기 (src/KspBridge.Mod.Core/Coordination/Coordinator.cs) — C5·C6·C7·C8·C9·C11
- [x] T006 `GuardRegistry`를 만들어 Coordinator와 모듈에 넘긴다 (src/KspBridge.Mod.Core/LogicEntry.cs) — C5·C9
- [x] T007 [P] 파괴된 활성 기체의 계획 거절 (src/KspBridge.Mod.Core/Session/FlightSession.cs) — C6
- [x] T008 [P] `get_status`의 계획 요약에 `abort_on`(이전 로직에서 넘어온 요약에 없으면 `null`) (src/KspBridge.Mod.Core/Status/CoreModule.cs) — C5
- [x] T009 단위 테스트: `abort_on` 기본값·검증·요약, 우선순위, 시간 초과·완료보다 먼저, 시작되지 않은 단계를 시작하지 않음, 완료 직전 판정, `Check`·`Start`·`Stop`의 예외, `Stop`이 한 번 불림, 계획마다 `Start`, 단계 번호, 조건과 시간 초과가 겹치면 시간 초과 (tests/KspBridge.Mod.Core.Tests/CoordinatorGuardTests.cs) — C3·C5·C6·C9·C11

체크포인트: `dotnet build KspBridge.slnx`가 통과하고 `KspBridge.Mod.Core.Tests`가 통과한다.

## 단계 3: 값 읽기와 스로틀

- [x] T010 [P] `TakeBasic`을 나누고 클래스와 `TakeBasic`만 공개, `Take`·`IsStaged`는 내부 전용 (src/KspBridge.Mod.Modules.Flight/FlightSnapshot.cs) — C2
- [x] T011 스로틀을 내리는 사유에 `part_loss`·`no_thrust` (src/KspBridge.Mod.Modules.Control/ThrottleRule.cs) — C10
- [x] T012 단계 설명의 종료 사유 (src/KspBridge.Mod.Modules.Control/ThrottleStep.cs) — C10
- [x] T013 단위 테스트: 새 종료 사유 셋 (tests/KspBridge.Mod.Modules.Control.Tests/ThrottleRuleTests.cs) — C10

체크포인트: 빌드가 통과하고 `KspBridge.Mod.Modules.Flight.Tests`·`KspBridge.Mod.Modules.Control.Tests`가 통과한다.

## 단계 4: condition 모듈

- [x] T014 Mod 모듈 프로젝트 (src/KspBridge.Mod.Modules.Condition/KspBridge.Mod.Modules.Condition.csproj) — C1
- [x] T015 서버 모듈 프로젝트와 이름 (src/KspBridge.Server.Modules.Condition/KspBridge.Server.Modules.Condition.csproj, src/KspBridge.Server.Modules.Condition/ConditionModule.cs) — C1
- [x] T016 테스트 프로젝트 (tests/KspBridge.Mod.Modules.Condition.Tests/KspBridge.Mod.Modules.Condition.Tests.csproj) — C1
- [x] T017 새 프로젝트 셋을 솔루션에 더한다 (KspBridge.slnx) — C1
- [x] T018 조건 값 이름 표와 값 꺼내기 (src/KspBridge.Mod.Modules.Condition/ConditionValues.cs) — C2
- [x] T019 조건 묶음의 판정과 요약 (src/KspBridge.Mod.Modules.Condition/ConditionSet.cs) — C2·C3·C4
- [x] T020 `wait_until` 인자 검증 (src/KspBridge.Mod.Modules.Condition/ConditionArgs.cs) — C2
- [x] T021 [P] 추력 없음 규칙 (src/KspBridge.Mod.Modules.Condition/NoThrustRule.cs) — C2·C8
- [x] T022 스로틀과 꺼진·작동 중인 엔진 수 읽기 (src/KspBridge.Mod.Modules.Condition/EngineReader.cs) — C2·C8
- [x] T023 단계 `wait_until`: 판정, 이벤트 `condition_met`, 진행, 설명 (src/KspBridge.Mod.Modules.Condition/WaitUntilStep.cs) — C1·C2·C3·C4
- [x] T024 [P] 손실 모음: 종류·원인 잇기, 10개 제한, 이벤트와 이유 문구 (src/KspBridge.Mod.Modules.Condition/LossLog.cs) — C6·C7·C9·C11
- [x] T025 손실 감시: 게임 이벤트 구독(`context.Subscriptions`), 활성 기체로 거르기, 처리기 예외, `parts_lost`·`vessel_destroyed`·`part_loss` (src/KspBridge.Mod.Modules.Condition/LossGuard.cs) — C6·C7·C9·C11
- [x] T026 추력 없음 감시 (src/KspBridge.Mod.Modules.Condition/NoThrustGuard.cs) — C8·C9
- [x] T027 모듈 등록(단계, 감시 둘) (src/KspBridge.Mod.Modules.Condition/ConditionModule.cs) — C1
- [x] T028 단위 테스트: 인자 (tests/KspBridge.Mod.Modules.Condition.Tests/ConditionArgsTests.cs) — C2
- [x] T029 [P] 단위 테스트: 값 이름마다 `FlightReport.Build`와 같음, 열린 궤도의 `null` (tests/KspBridge.Mod.Modules.Condition.Tests/ConditionValuesTests.cs) — C2·C3
- [x] T030 [P] 단위 테스트: `all`·`any`, 경계, `null`·NaN, 요약 (tests/KspBridge.Mod.Modules.Condition.Tests/ConditionSetTests.cs) — C2·C3·C4
- [x] T031 [P] 단위 테스트: 추력 없음 (tests/KspBridge.Mod.Modules.Condition.Tests/NoThrustRuleTests.cs) — C8
- [x] T032 [P] 단위 테스트: 손실 모음(종류·원인 잇기의 두 순서, 10개 제한, 비우기, 시작 전과 끝난 뒤, 다음 계획에 넘어가지 않음) (tests/KspBridge.Mod.Modules.Condition.Tests/LossLogTests.cs) — C7·C11

체크포인트: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과한다.

## 단계 5: 도구 설명과 문서

- [x] T033 `submit_plan`(`abort_on`)·`get_plan`(종료 사유, `abort_on`)·`get_flight_events`(`condition_met`·`parts_lost`·`plan_guard_error`)·`get_status` 설명 (src/KspBridge.Server.Modules.Core/CoreTools.cs) — C1·C5·C7
- [x] T034 [P] 단계 `wait_until`, 안전 중단과 `abort_on`, 모듈이 감시를 더하는 법을 더하고, `throttle` 행의 스로틀을 내리는 종료 사유에 `part_loss`·`no_thrust`를 넣는다 (ReadMe.md) — C1·C5·C10
- [x] T035 [P] 새 기능의 기준 spec (docs/features/flight-condition.md) — C1·C2·C3·C4·C6·C7·C8·C11·C12
- [x] T036 [P] 요구와 이력 반영: 계획 감시, `abort_on`, 안전 중단의 판정 순서, 파괴된 기체의 제출 거절, 단계 번호 (docs/features/flight-plan.md) — C5·C6·C9·C11
- [x] T037 [P] 요구와 이력 반영: 스로틀을 내리는 종료 사유 (docs/features/flight-control.md) — C10

체크포인트: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과하고, 기준 spec 셋의 `파일:` 줄과 이력에 이번 작업이 있다.

## 단계 6: 게임 검증

- [x] T038 게시(`dotnet publish src/KspBridge.Mod.Core -c Release`, `dotnet publish src/KspBridge.Server.Modules.Condition -c Release`)하고 핫리로드로 반영한다. `get_status`의 모듈과 `list_step_types`의 `wait_until` 설명을 확인한다 — C1
- [x] T039 게시 뒤 `get_flight`를 T001과 비교한다(발사대에서 잡음이 큰 세 값과 시각, 전기 현재량 제외) — C2
- [x] T040 발사대에서 조건 값 전체를 `condition_met`로 확인하고, 궤도에서 세 값을 확인한다(D011) — C2·C4
- [x] T041 계획을 붙잡아 두고 `step_progress`와 `abort_on` 요약(제출 응답·`current`·`last`·`get_status`)을 확인한다 — C4·C5
- [x] T042 C12 고도 조건, 이미 맞은 조건과 여러 조건, 궤도 조건, 시간 초과 — C3·C4·C12
- [x] T043 C12 추력 없음 조건과 스테이징, 추력 없음 중단, 분리는 손실이 아님 — C7·C8·C10·C12
- [x] T044 C12 부품 손실, 기체 파괴, 계획 없는 손실 — C6·C7·C9·C11·C12
- [x] T045 C12 오류 — C2·C5·C12

체크포인트: 작업 문서 `## 검증` 절에 C1~C12마다 확인한 명령·도구 호출과 결과, 확인하지 못한 것이 적혀 있다.

## 의존 관계

- T001은 게시 전의 값이라 T038보다 먼저 한다.
- T002·T003·T004 → T005 → T006·T009. T007·T008은 T004 뒤에 서로 독립이다.
- T010은 T018·T023보다 먼저, T011·T012·T013은 T004 뒤에 한다(종료 사유 이름을 쓴다).
- T014·T016 → T018~T032. T018·T019·T020·T022 → T023. T021 → T022·T026. T024 → T025. T023·T025·T026 → T027. 테스트 T028~T032는 짝이 되는 코드 뒤에 한다.
- T033·T034는 T005·T023·T025·T026 뒤에, T035~T037은 구현이 끝난 뒤에 한다(기준 spec은 구현된 요구를 적는다).
- T038은 T001~T037 뒤에 한다. T039~T045는 T038 뒤에 T039부터 차례로 한다. T044는 기체를 파괴하므로 T042·T043 뒤에 한다.
