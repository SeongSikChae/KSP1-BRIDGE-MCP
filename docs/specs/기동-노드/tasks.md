# 기동 노드 생성과 실행 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: ascent 모듈의 공개 부분과 연소 판정 떼기

- [x] T001 maneuver 모듈이 쓰는 형식·함수를 공개로 한다: `AscentReading`·`EngineInfo`·`StageBurn`, `AscentReader.Read`, `BurnEstimate.Seconds`, `StageRule`·`StageReason`, `AutoStager`, `AoaLimit`의 `PitchHeading`·`AngleDeg`·`Direction`, `LaunchAzimuth.Normalize`(동작 그대로) (src/KspBridge.Mod.Modules.Ascent/AscentReading.cs, src/KspBridge.Mod.Modules.Ascent/AscentReader.cs, src/KspBridge.Mod.Modules.Ascent/BurnEstimate.cs, src/KspBridge.Mod.Modules.Ascent/StageRule.cs, src/KspBridge.Mod.Modules.Ascent/AutoStager.cs, src/KspBridge.Mod.Modules.Ascent/AoaLimit.cs, src/KspBridge.Mod.Modules.Ascent/LaunchAzimuth.cs) — C5·C6
- [x] T002 연소 판정(방향 고정 10 m/s, 5° 게이트, 스로틀 2초 줄이기·최소 0.05, `tolerance_mps` 완료, 다시 늘어 완료, 엔진 소진, 스테이징 이유)을 공개 `BurnControl`로 떼고 `CircularizeLogic`이 그것을 부른다(기다림·원궤도 속도·진행 값은 그대로) (src/KspBridge.Mod.Modules.Ascent/BurnControl.cs, src/KspBridge.Mod.Modules.Ascent/CircularizeLogic.cs) — C5
- [x] T003 단위 테스트: `BurnControl`(방향 고정, 게이트, 스로틀, 완료, 다시 늘어 완료와 추력 없는 프레임에 버림, 엔진 소진) (tests/KspBridge.Mod.Modules.Ascent.Tests/BurnControlTests.cs) — C5

체크포인트: `dotnet build KspBridge.slnx`가 통과하고 기존 `KspBridge.Mod.Modules.Ascent.Tests`(특히 `CircularizeLogicTests`)가 고치지 않고 통과한다.

## 단계 2: maneuver 모듈의 규칙

- [x] T004 Mod 모듈 프로젝트(flight·control·ascent 참조, 테스트용 `InternalsVisibleTo`) (src/KspBridge.Mod.Modules.Maneuver/KspBridge.Mod.Modules.Maneuver.csproj) — C1
- [x] T005 테스트 프로젝트(maneuver·ascent·control·flight 참조) (tests/KspBridge.Mod.Modules.Maneuver.Tests/KspBridge.Mod.Modules.Maneuver.Tests.csproj) — C1
- [x] T006 서버 모듈 프로젝트 (src/KspBridge.Server.Modules.Maneuver/KspBridge.Server.Modules.Maneuver.csproj) — C1
- [x] T007 새 프로젝트 셋을 솔루션에 더한다 (KspBridge.slnx) — C1
- [x] T008 [P] 인자 검증: `create_node`(시각 셋 가운데 하나, 성분·`goal`, 모두 0, `goal` 키 하나, 참만, 유한한 수, 종류, 모르는 키), `delete_node`(`index`·`all`), `execute_node`(`tolerance_mps`·`auto_stage`) (src/KspBridge.Mod.Modules.Maneuver/NodeArgs.cs) — C2·C4·C5
- [x] T009 [P] 장면·기체·노드 사용 가능 판정과 이유(시설 등급 없음, 추적소·관제 센터 미해금, 궤도 계산기 없음) (src/KspBridge.Mod.Modules.Maneuver/NodeAvailability.cs) — C2·C3·C4
- [x] T010 [P] 게임 식의 방향 셋(진행 Z, 법선 Y = `Cross(−r, v)`, 방사 X = `Cross(Y, Z)`)과 성분 ↔ 벡터 변환 (src/KspBridge.Mod.Modules.Maneuver/NodeFrame.cs) — C2·C3·C8
- [x] T011 [P] 닫힌·열린 궤도의 다음 Ap·Pe 시각(ObT와 주기, 엄격히 뒤) (src/KspBridge.Mod.Modules.Maneuver/ConicTimes.cs) — C2·C3
- [x] T012 시각 규칙: 착지·착수·발사 전이면 `bad_request`, `ut`·`in_s`·`at`(t₀ = 지금과 마지막 노드 중 늦은 쪽, 1 ms 안이면 한 주기 뒤), 지금·마지막 노드 뒤, `ESCAPE`·`ENCOUNTER` 끝 전, `FINAL` 제약 없음, 열린 궤도 Ap·지난 Pe 오류 (src/KspBridge.Mod.Modules.Maneuver/NodeTime.cs) — C2
- [x] T013 [P] 목표 Δv: Ap·Pe는 진행 방향 크기 이분법(1 mm), 범위 끝으로 낼 수 없는 목표 판정, 닫힌 궤도·영향권·고도 조건, 원궤도 속도 (src/KspBridge.Mod.Modules.Maneuver/NodeGoal.cs) — C2
- [x] T014 한 노드의 값과 `get_nodes` 항목(성분·크기, 예상 연소 시간·연소 시작 시각, 노드 뒤 궤도 요소와 첫 Ap·Pe UT 또는 `null`과 이유, 맨 앞 노드의 남은 Δv) (src/KspBridge.Mod.Modules.Maneuver/NodeInfo.cs) — C2·C3
- [x] T015 [P] `NodeStartReading`과 `NodeStart.Judge`(시작 실패 이유 또는 연소 시작 시각) (src/KspBridge.Mod.Modules.Maneuver/NodeStart.cs) — C5·C6
- [x] T016 `execute_node`의 진행: `NodeStart.Judge` 결과로 시작, 기다림·연소 국면, `BurnControl` 연결, 진행 값 (src/KspBridge.Mod.Modules.Maneuver/ExecuteNodeLogic.cs) — C5·C8
- [x] T017 [P] 단위 테스트: 인자 (tests/KspBridge.Mod.Modules.Maneuver.Tests/NodeArgsTests.cs) — C2·C4·C5
- [x] T018 [P] 단위 테스트: 준비 판정 (tests/KspBridge.Mod.Modules.Maneuver.Tests/NodeAvailabilityTests.cs) — C2·C3·C4
- [x] T019 [P] 단위 테스트: 방향 셋의 축과 왕복 (tests/KspBridge.Mod.Modules.Maneuver.Tests/NodeFrameTests.cs) — C2·C3
- [x] T020 [P] 단위 테스트: 다음 Ap·Pe 시각(구간 끝과 상관없음) (tests/KspBridge.Mod.Modules.Maneuver.Tests/ConicTimesTests.cs) — C3
- [x] T021 [P] 단위 테스트: 시각 규칙(plan 검증 전략 C2의 경우와 착지·착수·발사 전의 `bad_request`) (tests/KspBridge.Mod.Modules.Maneuver.Tests/NodeTimeTests.cs) — C2
- [x] T022 [P] 단위 테스트: 목표 Δv(Ap·Pe 1 mm, 원궤도, 낼 수 없는 목표, 고도·영향권 범위: Ap는 노드 시각 고도 이상·반지름 < 영향권, Pe는 0 이상·노드 시각 고도 이하) (tests/KspBridge.Mod.Modules.Maneuver.Tests/NodeGoalTests.cs) — C2
- [x] T023 [P] 단위 테스트: 항목 필드와 `null` 규칙 (tests/KspBridge.Mod.Modules.Maneuver.Tests/NodeInfoTests.cs) — C3
- [x] T024 [P] 단위 테스트: 시작 실패 각각과 연소 시작 시각 (tests/KspBridge.Mod.Modules.Maneuver.Tests/NodeStartTests.cs) — C5·C6
- [x] T025 단위 테스트: 실행 진행(`NodeStart.Judge` 결과를 씀, 지났으면 바로 연소, 국면, 연소 판정을 `BurnControl`에 넘김, 진행 값 모양) (tests/KspBridge.Mod.Modules.Maneuver.Tests/ExecuteNodeLogicTests.cs) — C5·C8

체크포인트: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과한다.

## 단계 3: 게임 연결, 단계, 도구

- [x] T026 노드 목록 읽기(`NodeInfo`, 노드 뒤 궤도, 상태 벡터, 연소 추정 입력)와 공개 `FirstNodeStart` (src/KspBridge.Mod.Modules.Maneuver/NodeReader.cs) — C3·C5·C6
- [x] T027 노드 만들기(`AddManeuverNode`, `OnGizmoUpdated`, Ap·Pe 할선 보정 최대 5번, `circular` 확인, 노드 뒤 궤도 없음·1 m 밖이면 지우고 `bad_request`)와 지우기(`ManeuverNode.RemoveSelf`, 번호·전부) (src/KspBridge.Mod.Modules.Maneuver/NodeEditor.cs) — C2·C4
- [x] T028 단계 `execute_node`: 설명(인자, 실패·완료 조건, `warp` → `execute_node` 안내, 호만 예, `abort_on.no_thrust` 안내), 시작 검사, SAS 끄기, hold, `throttle_cut` 등록, 매 프레임 노드 확인·적용·스테이징, 완료 때 스로틀 0·노드 지우기·hold 바꾸기, 이벤트 `node_executed`, 진행 (src/KspBridge.Mod.Modules.Maneuver/ExecuteNodeStep.cs) — C1·C5·C7·C8
- [x] T029 모듈 등록: 처리기 `create`·`get`·`delete`(장면·실행 중인 계획 검사, `create`는 착지·착수·발사 전 상태를 읽어 `NodeTime`에 넘김)와 단계 (src/KspBridge.Mod.Modules.Maneuver/ManeuverModule.cs) — C1·C2·C3·C4
- [x] T030 서버 도구 `create_node`·`get_nodes`·`delete_node`: `JsonElement?` 인자와 설명(인자·오류·호만 예), `RequestContext<CallToolRequestParams>`의 원래 인자를 그대로 넘김 (src/KspBridge.Server.Modules.Maneuver/ManeuverTools.cs) — C1·C2·C3·C4

체크포인트: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과한다.

## 단계 4: warp의 노드 목표

- [x] T031 warp 모듈과 그 테스트 프로젝트에 maneuver·ascent·control 참조 (src/KspBridge.Mod.Modules.Warp/KspBridge.Mod.Modules.Warp.csproj, tests/KspBridge.Mod.Modules.Warp.Tests/KspBridge.Mod.Modules.Warp.Tests.csproj) — C6
- [x] T032 `to` `node` 인자 (src/KspBridge.Mod.Modules.Warp/WarpArgs.cs) — C6
- [x] T033 노드 목표: `NodeStart.Judge` 결과의 실패 전달과 연소 시작 시각 − `lead_s` (src/KspBridge.Mod.Modules.Warp/WarpTarget.cs) — C6
- [x] T034 `ReadNode()`와 연결(`NodeReader.FirstNodeStart`), 세션이 노드 목표에 넘김 (src/KspBridge.Mod.Modules.Warp/IWarpGame.cs, src/KspBridge.Mod.Modules.Warp/GameWarp.cs, src/KspBridge.Mod.Modules.Warp/WarpSession.cs) — C6
- [x] T035 단계 설명에 `node` 목표와 실패 조건 (src/KspBridge.Mod.Modules.Warp/WarpStep.cs) — C1·C6
- [x] T036 단위 테스트: `node` 인자와 `lead_s`(`node`와 함께), 목표 UT, 실패 전달, 가짜 게임의 `ReadNode` (tests/KspBridge.Mod.Modules.Warp.Tests/FakeWarpGame.cs, tests/KspBridge.Mod.Modules.Warp.Tests/WarpArgsTests.cs, tests/KspBridge.Mod.Modules.Warp.Tests/WarpTargetTests.cs, tests/KspBridge.Mod.Modules.Warp.Tests/WarpSessionTests.cs) — C6

체크포인트: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과한다.

## 단계 5: 다른 모듈의 설명과 안내

- [x] T037 [P] `attitude` 설명의 hold가 끝나는 조건과 `throttle` 설명에 `execute_node` (src/KspBridge.Mod.Modules.Control/AttitudeSteps.cs, src/KspBridge.Mod.Modules.Control/ThrottleStep.cs) — C7
- [x] T038 [P] `get_flight_events` 설명에 `node_executed` (src/KspBridge.Server.Modules.Core/CoreTools.cs) — C8
- [x] T039 [P] 도구 셋, 단계 `execute_node`, `warp`의 `node` 목표, 호만 전이 예시 계획 (ReadMe.md) — C1

체크포인트: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과한다.

## 단계 6: 기준 spec

- [x] T040 [P] 새 기능의 기준 spec (docs/features/flight-maneuver.md) — C1·C2·C3·C4·C5·C8·C9
- [x] T041 [P] `warp`의 `node` 목표 요구와 `파일:` 줄·이력 (docs/features/flight-warp.md) — C6
- [x] T042 [P] R2 예외, R3·R6에 `execute_node`, `파일:` 줄·이력 (docs/features/flight-control.md) — C7
- [x] T051 [P] `파일:` 줄에 `BurnControl.cs`·`BurnControlTests.cs`, 이력(요구는 바뀌지 않음) (docs/features/flight-ascent.md) — C5

체크포인트: 네 기준 spec의 이력에 이번 작업이 있고 `파일:` 줄이 plan "바꿀 파일·구조"와 맞는다.

## 단계 7: 게임 검증

- [x] T043 KSP와 서버를 띄우고 `get_status`로 다른 사람의 비행·계획이 있는지 본다(있으면 사용자에게 묻는다). 발사대에 `Kerbal X`(승무원 탑승)를 올린 뒤 그 비행 장면에서 `debug_save`(이름 `before-37`)로 저장한다 — C9
- [x] T044 게시(`dotnet publish src/KspBridge.Mod.Core -c Release`, `dotnet publish src/KspBridge.Server.Modules.Maneuver -c Release`)하고 핫리로드로 반영한다. `get_status`의 모듈, Claude Code의 세 도구, `list_step_types`의 `execute_node`·`warp` 설명을 실제 MCP 호출로 확인한다 — C1
- [x] T045 C9 오류(spec의 `bad_request`·`step_failed` 항목, 지난 노드가 남는지) — C2·C4·C5·C6·C9
- [x] T046 C9 노드 조작(Ap 300 km·Pe 100 km) — C2·C3·C4·C9
- [x] T047 C9 호만 전이(100 km 원궤도). 비행 중 10~20초마다 `get_plan`·`get_flight`로 이상을 본다(판정에는 쓰지 않음, `step_progress`·`hold` 모양은 C8로 기록) — C2·C3·C5·C6·C8·C9
- [x] T048 C9 성분 지정(적도 100 km 원궤도, 성분마다 `debug_set_orbit`)과 부호 확인(방사: 다음 Ap가 먼저, 법선: `wait` 300초 뒤 위도 > 0) — C2·C5·C9
- [x] T049 C9 다시 읽기(`goal.apoapsis_m` 2000000 노드의 연소 중) — C7·C9
- [x] T050 시험이 끝나면 `debug_load` `before-37`로 T043의 발사대 상태로 되돌린다 — C9

체크포인트: 작업 문서 `## 검증` 절에 T044~T049의 명령·도구 호출과 결과(실패 포함)가 적혀 있고, C1~C9마다 확인 결과나 확인하지 못한 이유가 있다.

## 의존 관계

- 단계 1(T001·T002)은 maneuver 모듈이 공개 형식과 `BurnControl`을 쓰므로 T014·T016·T026~T028보다 먼저 한다. T001 → T002 → T003.
- T004·T005 → T008~T025. T006은 독립. T007은 T004·T005·T006 뒤에 한다.
- T010·T011 → T012·T013·T014. T015 → T016. 테스트 T017~T025는 짝이 되는 코드 뒤에 한다.
- T026 → T027·T028 → T029. T030은 T006 뒤.
- T026(`FirstNodeStart`) → T031~T036. T031 → T032~T036.
- T037~T039는 T028·T030·T035 뒤에 한다(설명할 내용이 정해진 뒤).
- T040~T042는 구현이 끝난 뒤에 한다(기준 spec은 구현된 요구를 적는다).
- T043은 시험 뒤 되돌아갈 저장본을 게시 전의 Mod로 만들므로 T044보다 먼저 한다. T044 → T045~T049. T050은 단계 7의 마지막에 한다.
