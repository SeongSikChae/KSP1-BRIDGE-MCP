# 시간 가속 단계 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 기준 값

- [x] T001 게시 전의 값 기록: 게임 상태를 `get_status`로 확인하고 다른 비행을 건드려도 되는지 사용자에게 확인한 뒤 `debug_save`(이름 `before-34`)로 저장하고, `C33 Loss Test`를 발사대에 올려 `get_flight`(`include` 셋 포함) 응답을 작업 문서에 적는다 — C11

체크포인트: 저장 `before-34`가 있고 게시 전 `get_flight` 응답이 작업 문서에 있다.

## 단계 2: 가속 상태

- [x] T002 `WarpState`(배율, 물리 여부, 레일 위)와 `FlightData.Warp` (src/KspBridge.Mod.Modules.Flight/FlightData.cs) — C11
- [x] T003 가속 상태 읽기 `Warp(Vessel)`(공개)와 `TakeBasic`에서 채우기 (src/KspBridge.Mod.Modules.Flight/FlightSnapshot.cs) — C11
- [x] T004 기본 응답의 `time_warp`와 `TimeWarp(WarpState)`(공개) (src/KspBridge.Mod.Modules.Flight/FlightReport.cs) — C10·C11
- [x] T005 단위 테스트: 기본 응답의 키, 방식 `none`·`rails`·`physics` (tests/KspBridge.Mod.Modules.Flight.Tests/FlightReportTests.cs) — C11

체크포인트: 빌드가 통과하고 `KspBridge.Mod.Modules.Flight.Tests`·`KspBridge.Mod.Modules.Condition.Tests`가 통과한다.

## 단계 3: warp 모듈

- [x] T006 Mod 모듈 프로젝트 (src/KspBridge.Mod.Modules.Warp/KspBridge.Mod.Modules.Warp.csproj) — C1
- [x] T007 서버 모듈 프로젝트와 이름 (src/KspBridge.Server.Modules.Warp/KspBridge.Server.Modules.Warp.csproj, src/KspBridge.Server.Modules.Warp/WarpModule.cs) — C1
- [x] T008 테스트 프로젝트 (tests/KspBridge.Mod.Modules.Warp.Tests/KspBridge.Mod.Modules.Warp.Tests.csproj) — C1
- [x] T009 새 프로젝트 셋을 솔루션에 더한다 (KspBridge.slnx) — C1
- [x] T010 인자 검증 (src/KspBridge.Mod.Modules.Warp/WarpArgs.cs) — C2
- [x] T011 [P] 목표 UT 계산과 시작 실패 판정 (src/KspBridge.Mod.Modules.Warp/WarpTarget.cs) — C3·C4
- [x] T012 [P] 배율 규칙: `RailsWanted`(영향권 전환 조건 포함, D011)·`PhysicsWanted`·`Decide` (src/KspBridge.Mod.Modules.Warp/WarpRule.cs) — C5·C6
- [x] T013 게임 인터페이스와 값 형식 (src/KspBridge.Mod.Modules.Warp/IWarpGame.cs) — C5·C6·C7·C8
- [x] T014 단계의 진행: `Start`·`Tick`·`Stop`, 배율 최대와 막힌 이유, 스로틀 되돌리기, 진행 값 (src/KspBridge.Mod.Modules.Warp/WarpSession.cs) — C3·C4·C5·C6·C7·C8·C10
- [x] T015 `IWarpGame`의 게임 구현: 읽기, 레일 상한(`getMaxOnRailsRateIdx` 리플렉션), 영향권 전환까지 남은 시간, 배율·방식 설정과 멈춘 상태 지키기(D012), 자동 가속 끄기, 게임 설정, 스로틀 (src/KspBridge.Mod.Modules.Warp/GameWarp.cs) — C5·C6·C7·C8
- [x] T016 단계 `warp`: 설명, 코어 연결, 이벤트 `warp_completed`·`warp_released`, 진행 (src/KspBridge.Mod.Modules.Warp/WarpStep.cs) — C1·C3·C4·C6·C7·C8·C9·C10
- [x] T017 모듈 등록 (src/KspBridge.Mod.Modules.Warp/WarpModule.cs) — C1
- [x] T018 단위 테스트: 인자 (tests/KspBridge.Mod.Modules.Warp.Tests/WarpArgsTests.cs) — C2
- [x] T019 [P] 단위 테스트: 목표 종류별 UT, `lead_s`, 실패 조건 (tests/KspBridge.Mod.Modules.Warp.Tests/WarpTargetTests.cs) — C3·C4
- [x] T020 [P] 단위 테스트: 단 고르기(`max_rate`, 남은 시간, 물리, 영향권 전환까지 남은 시간의 경계), 막힘, 방식 바꿈, 즉시 내림, 전환 중에는 올리지 않음 (tests/KspBridge.Mod.Modules.Warp.Tests/WarpRuleTests.cs) — C5
- [x] T021 가짜 게임: 시간 진행, 1초 전환과 시작값, 레일 상한(지금 배율에 따른 영향권 상한 포함), 게임이 스스로 내림, 두 `FixedUpdate` 순서, 레일 이탈 지연 (tests/KspBridge.Mod.Modules.Warp.Tests/FakeWarpGame.cs) — C5·C6·C7·C8
- [x] T022 단위 테스트: 활성 기체 없음, 다시 맞춤, 자동 가속, 막힌 뒤 돌아감, 게임 설정, 영향권 전환을 단 3 이하로 넘고 다시 올리지 않음(두 순서), 도착 오차, 이미 지난 목표, 늦은 레일 이탈, 사람이 켜 둔 가속에서 시작, 되돌리기, 스로틀, `Tick` 예외 뒤 `Stop`, 배율 최대와 막힌 이유, 남은 시간 (tests/KspBridge.Mod.Modules.Warp.Tests/WarpSessionTests.cs) — C4·C5·C6·C7·C8·C9·C10

체크포인트: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과한다.

## 단계 4: 도구 설명과 문서

- [x] T023 [P] `get_flight` 설명의 `time_warp` (src/KspBridge.Server.Modules.Flight/FlightTools.cs) — C11
- [x] T024 [P] `get_flight_events` 설명의 `warp_completed`·`warp_released` (src/KspBridge.Server.Modules.Core/CoreTools.cs) — C10
- [x] T025 [P] `throttle` 단계 설명의 `warp` 뒤 스로틀 (src/KspBridge.Mod.Modules.Control/ThrottleStep.cs) — C13
- [x] T026 [P] 단계 `warp`와 `get_flight`의 `time_warp` (ReadMe.md) — C1·C11
- [x] T027 [P] 새 기능의 기준 spec (docs/features/flight-warp.md) — C1·C2·C3·C4·C5·C6·C7·C8·C9·C10·C12
- [x] T028 [P] 요구와 이력 반영: `get_flight`의 `time_warp` (docs/features/flight-observation.md) — C11
- [x] T029 [P] 요구와 이력 반영: R2·R3의 예외 (docs/features/flight-control.md) — C13

체크포인트: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과하고, 기준 spec 셋의 이력에 이번 작업이 있다.

## 단계 5: 게임 검증

- [x] T030 게시(`dotnet publish src/KspBridge.Mod.Core -c Release`, `dotnet publish src/KspBridge.Server.Modules.Warp -c Release`)하고 핫리로드로 반영한다. `get_status`의 모듈과 `list_step_types`의 `warp` 설명을 확인한다 — C1
- [x] T031 게시 뒤 발사대의 `get_flight`를 T001과 비교한다(시각, 전기 현재량, `time_to_apoapsis_s`·`heading_deg`·`roll_deg` 제외) — C11
- [x] T032 C12 실패 경로 — C2·C4·C12
- [x] T033 C12 발사대 — C5·C6·C8·C12
- [x] T034 C12 궤도 항목: 짧은 목표, 이미 지난 목표, 시간, 최대 배율, Ap·Pe — C3·C5·C6·C10·C12
- [x] T035 C12 스로틀과 자세(전기가 남은 비행) — C8·C12·C13
- [x] T036 C12 시각, 시간 초과 — C5·C6·C7·C12
- [x] T037 C12 중단: `abort_plan`(실행 중 값과 중단 뒤 스로틀 포함), `debug_revert`, `debug_space_center`, 다시 읽기 — C7·C8·C10·C11·C12·C13
- [x] T038 시험 기체 `C34 Coast Test`를 에디터 도구로 만들어 저장한다(부품 이름은 `editor_list_parts`, TWR·Δv는 `check_ship`) — C12 (D013으로 쓰지 않게 됨)
- [x] T041 바뀐 C12를 기준 spec R11에 반영한다 (docs/features/flight-warp.md) — C12
- [x] T039 C12 물리 가속(`C33 Loss Test`, D015) — C5·C6·C10·C12
- [x] T043 시작 뒤 25 물리 프레임은 레일 배율을 올리지 않는다(D017) (src/KspBridge.Mod.Modules.Warp/WarpSession.cs) — C5
- [x] T044 단위 테스트: 시작 뒤의 기다림 (tests/KspBridge.Mod.Modules.Warp.Tests/WarpSessionTests.cs) — C5
- [x] T045 기준 spec R5에 반영한다 (docs/features/flight-warp.md) — C5
- [x] T046 다시 게시하고 궤도에서 `stage` → `warp`(`seconds` 8, `physics_rate` 4)가 레일이 아니라 물리 가속을 쓰는지 본다 — C5

체크포인트: 작업 문서 `## 검증` 절에 C1~C13마다 확인한 명령·도구 호출과 결과, 확인하지 못한 것이 적혀 있다.

## 의존 관계

- T001은 게시 전의 값이라 T030보다 먼저 한다.
- T002 → T003·T004 → T005.
- T006·T008 → T010~T022. T009는 T006·T007·T008 뒤에 한다. T004 → T014·T016(가속 상태를 쓴다).
- T010·T011·T012·T013 → T014 → T016 → T017. T013 → T015 → T016.
- 테스트 T018~T020은 짝이 되는 코드 뒤에, T021은 T013 뒤에, T022는 T014·T021 뒤에 한다.
- T023은 T004 뒤에, T024~T026은 T016 뒤에, T027~T029는 구현이 끝난 뒤에 한다(기준 spec은 구현된 요구를 적는다).
- T030은 T001~T029 뒤에 한다. T031~T037은 T030 뒤에 T031부터 차례로 한다. T038은 에디터 장면으로 가므로 T037 뒤에 하고, T041은 T039보다 먼저, T039는 T038 뒤에 한다.
