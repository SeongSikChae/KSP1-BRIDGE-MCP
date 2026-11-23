# 상승과 원궤도화 단계 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: control 모듈의 공개 부분 나누기

- [x] T001 `RequireVessel`·`RequireControllable`을 공개 `ControlStep`으로 옮긴다 (src/KspBridge.Mod.Modules.Control/ControlStep.cs, src/KspBridge.Mod.Modules.Control/AttitudeSteps.cs) — C11
- [x] T002 `AttitudeHold`를 공개로 하고 목표·표시 이름(`type`)을 바꿀 수 있게 한다. 오차 계산 공개 (src/KspBridge.Mod.Modules.Control/AttitudeHold.cs) — C3·C10·C11
- [x] T003 `AttitudeTarget`·`AttitudeDirection`·`FlightFrame`·`TargetVectors`·`AttitudeError`·`AttitudeLaw`·`VesselAttitude`·`VesselReader`를 공개로 한다(동작 그대로) (src/KspBridge.Mod.Modules.Control/AttitudeTarget.cs, src/KspBridge.Mod.Modules.Control/AttitudeLaw.cs, src/KspBridge.Mod.Modules.Control/VesselReader.cs) — C3·C11
- [x] T004 스로틀 쓰기와 계획 종료 때 내리기를 `ThrottleControl`(`Write`, `CutAtEnd`)로 옮기고 `throttle` 단계가 그것을 쓴다. 단계 설명에 `ascend`·`circularize`의 스로틀 (src/KspBridge.Mod.Modules.Control/ThrottleControl.cs, src/KspBridge.Mod.Modules.Control/ThrottleStep.cs) — C4·C11
- [x] T005 스테이지 판정과 작동을 `StageActivator`로 옮겨 결과(거절, 작동한 부품 수)를 돌려주게 하고, `stage` 단계가 그것을 쓴다. 이벤트는 부르는 쪽이 남긴다(`stage` 단계는 지금처럼, `AutoStager`는 `reason`을 더해) (src/KspBridge.Mod.Modules.Control/StageActivator.cs, src/KspBridge.Mod.Modules.Control/StageStep.cs) — C6·C11
- [x] T006 `attitude` 단계 설명의 hold가 끝나는 조건에 `ascend`·`circularize` (src/KspBridge.Mod.Modules.Control/AttitudeSteps.cs) — C11

체크포인트: `dotnet build KspBridge.slnx`가 통과하고 기존 `KspBridge.Mod.Modules.Control.Tests`가 고치지 않고 통과한다.

## 단계 2: ascent 모듈의 규칙

- [x] T007 Mod 모듈 프로젝트(control·flight 참조) (src/KspBridge.Mod.Modules.Ascent/KspBridge.Mod.Modules.Ascent.csproj) — C1
- [x] T008 테스트 프로젝트 (tests/KspBridge.Mod.Modules.Ascent.Tests/KspBridge.Mod.Modules.Ascent.Tests.csproj) — C1
- [x] T009 서버 모듈 프로젝트와 이름 (src/KspBridge.Server.Modules.Ascent/KspBridge.Server.Modules.Ascent.csproj, src/KspBridge.Server.Modules.Ascent/AscentModule.cs) — C1
- [x] T010 새 프로젝트 셋을 솔루션에 더한다 (KspBridge.slnx) — C1
- [x] T011 인자 검증 (src/KspBridge.Mod.Modules.Ascent/AscentArgs.cs) — C2
- [x] T012 한 프레임의 값과 엔진 정보 형식 (src/KspBridge.Mod.Modules.Ascent/AscentReading.cs) — C3·C4·C5·C6·C8
- [x] T013 [P] 피치·처짐 방지·선회 기본값·시작 실패 (src/KspBridge.Mod.Modules.Ascent/TurnProfile.cs) — C2·C3·C7
- [x] T014 [P] 방위·경사각 허용 범위 (src/KspBridge.Mod.Modules.Ascent/LaunchAzimuth.cs) — C3·C7
- [x] T015 [P] 받음각 한계, 벡터와 피치·방위 변환 (src/KspBridge.Mod.Modules.Ascent/AoaLimit.cs) — C3
- [x] T016 [P] 조종 상실 판정 (src/KspBridge.Mod.Modules.Ascent/ControlLossRule.cs) — C7
- [x] T017 [P] 자동 스테이징과 엔진 소진 판정(D015의 겹치지 않는 두 조건) (src/KspBridge.Mod.Modules.Ascent/StageRule.cs) — C6·C7
- [x] T018 [P] 연소 시간 추정 (src/KspBridge.Mod.Modules.Ascent/BurnEstimate.cs) — C8
- [x] T019 `ascend`의 진행: 닿음·다시 채움, 국면, 스로틀(Ap·동압), 목표 방향, 완료(C5), 실패, 기록, 진행 값 (src/KspBridge.Mod.Modules.Ascent/AscendLogic.cs) — C3·C4·C5·C7·C10
- [x] T020 `circularize`의 진행: 시작 실패, 시작 시각, 국면, 방향과 고정, 5° 문, 스로틀, 완료(남은 Δv, 엔진 추력 기준의 다시 늘어 완료), 진행 값 (src/KspBridge.Mod.Modules.Ascent/CircularizeLogic.cs) — C8·C9·C10
- [x] T021 단위 테스트: 인자 (tests/KspBridge.Mod.Modules.Ascent.Tests/AscentArgsTests.cs) — C2
- [x] T022 [P] 단위 테스트: 선회 (tests/KspBridge.Mod.Modules.Ascent.Tests/TurnProfileTests.cs) — C3·C7
- [x] T023 [P] 단위 테스트: 방위 (tests/KspBridge.Mod.Modules.Ascent.Tests/LaunchAzimuthTests.cs) — C3·C7
- [x] T024 [P] 단위 테스트: 받음각 한계 (tests/KspBridge.Mod.Modules.Ascent.Tests/AoaLimitTests.cs) — C3
- [x] T025 [P] 단위 테스트: 조종 상실 (tests/KspBridge.Mod.Modules.Ascent.Tests/ControlLossRuleTests.cs) — C7
- [x] T026 [P] 단위 테스트: `Kerbal X` 순서와 plan 검증 전략 C6의 경우 (tests/KspBridge.Mod.Modules.Ascent.Tests/StageRuleTests.cs) — C6·C7
- [x] T027 [P] 단위 테스트: 연소 시간 (tests/KspBridge.Mod.Modules.Ascent.Tests/BurnEstimateTests.cs) — C8
- [x] T028 단위 테스트: `ascend` 진행(plan 검증 전략 C4·C5·C7·C10의 경우) (tests/KspBridge.Mod.Modules.Ascent.Tests/AscendLogicTests.cs) — C4·C5·C7·C10
- [x] T029 단위 테스트: `circularize` 진행(plan 검증 전략 C8·C9·C10의 경우) (tests/KspBridge.Mod.Modules.Ascent.Tests/CircularizeLogicTests.cs) — C8·C9·C10

체크포인트: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과한다.

## 단계 3: 게임 연결과 단계

- [x] T030 활성 기체에서 값과 엔진 목록 읽기(분리 스테이지, 추진제, 진공 최대 추력, Δv 계산기 표) (src/KspBridge.Mod.Modules.Ascent/AscentReader.cs) — C3·C4·C5·C6·C8
- [x] T031 자동 스테이징 실행: 1초 쿨다운, 거절 재시도, 작동한 부품 없음 실패, 이벤트 `reason`과 횟수 (src/KspBridge.Mod.Modules.Ascent/AutoStager.cs) — C6·C7
- [x] T032 단계 `ascend`: 설명(고체 부스터·`abort_on.no_thrust` 안내 포함), 시작 검사, SAS 끄기, hold, `throttle_cut` 등록, 프레임마다 적용, 완료 때 hold 바꾸기, 이벤트 `ascend_completed`, 진행 (src/KspBridge.Mod.Modules.Ascent/AscendStep.cs) — C1·C3·C4·C5·C7·C10·C11
- [x] T033 단계 `circularize`: 같은 방식, 이벤트 `circularize_completed` (src/KspBridge.Mod.Modules.Ascent/CircularizeStep.cs) — C1·C8·C9·C10·C11
- [x] T034 모듈 등록 (src/KspBridge.Mod.Modules.Ascent/AscentModule.cs) — C1
- [x] T035 [P] `get_flight_events` 설명에 `ascend_completed`·`circularize_completed`와 `stage_activated`의 `reason` (src/KspBridge.Server.Modules.Core/CoreTools.cs) — C6·C10
- [x] T036 [P] 단계 `ascend`·`circularize`, 고체 부스터 안내, 예시 계획 (ReadMe.md) — C1

체크포인트: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과한다.

## 단계 4: 기준 spec

- [x] T037 [P] 새 기능의 기준 spec (docs/features/flight-ascent.md) — C1·C2·C3·C4·C5·C6·C7·C8·C9·C10·C12
- [x] T038 [P] 요구와 `파일:` 줄·이력 반영: R2 예외, R3·R6에 두 단계 (docs/features/flight-control.md) — C11

체크포인트: 두 기준 spec의 이력에 이번 작업이 있고 `파일:` 줄이 plan "바꿀 파일·구조"와 맞는다.

## 단계 5: 게임 검증

- [x] T039 KSP와 서버를 띄우고 `get_status`로 다른 사람의 비행·계획이 있는지 본다(있으면 사용자에게 묻는다). 발사대에 `Kerbal X`(승무원 탑승)를 올린 뒤 그 비행 장면에서 `debug_save`(이름 `before-36`)로 저장한다(`debug_save`는 비행 장면에서만 저장한다, docs/features/debug-tools.md R8) — C12
- [x] T040 게시(`dotnet publish src/KspBridge.Mod.Core -c Release`, `dotnet publish src/KspBridge.Server.Modules.Ascent -c Release`)하고 핫리로드로 반영한다. `get_status`의 모듈과 `list_step_types`의 두 단계 설명을 실제 MCP 호출로 확인한다 — C1
- [x] T041 C12 오류: `bad_request` 다섯 가지와 발사대의 `step_failed` 둘 — C2·C7·C8·C12
- [x] T042 C12 궤도 진입(`Kerbal X`, 승무원). 비행 중 10~20초마다 `get_plan`·`get_flight`로 이상을 본다(판정에는 쓰지 않음, `step_progress`·`hold` 모양은 C10으로 기록) — C3·C4·C5·C6·C8·C9·C10·C12 (D017 전 코드의 결과, T058에서 다시)
- [x] T043 C12 경사각(`Kerbal X`, `inclination_deg` 45) — C3·C12 (1차 결과 실패 42.8°, 의도 변경 D016으로 T056에서 다시)
- [x] T044 C12 다시 읽기(`debug_set_orbit` Ap 1,000 km·Pe 100 km의 `Kerbal X`) — C11·C12
- [x] T045 조종 상실 시험 기체를 에디터 도구로 만들어 저장하고(`probeCoreOcto.v2` 루트, 고체 부스터, 무게중심 앞 날개), C12 조종 상실을 한다 — C7·C12 (1차 결과 실패, 조종 상실 판정 없이 part_loss, 의도 변경 D017로 T057에서 다시)
체크포인트: 작업 문서 `## 검증` 절에 T040~T045의 명령·도구 호출과 결과(실패 포함)가 적혀 있다.

## 단계 6: 의도 변경 D016~D018 반영

- [x] T047 `circularize`에 `inclination_deg`(0~180) (src/KspBridge.Mod.Modules.Ascent/AscentArgs.cs) — C2
- [x] T048 [P] `circularize`용 β(두 해 가운데 지금 수평 궤도 속도에 가까운 해, 같으면 `north`). 대체 규칙은 기존 clamp 그대로 (src/KspBridge.Mod.Modules.Ascent/LaunchAzimuth.cs) — C3·C8
- [x] T049 [P] 조종 상실에 엔진 추력 합 ≥ 0.01 kN 조건(D017) (src/KspBridge.Mod.Modules.Ascent/ControlLossRule.cs, src/KspBridge.Mod.Modules.Ascent/AscendLogic.cs) — C7
- [x] T050 `circularize`의 경사각: 시작 실패, 원궤도 속도 방향(β 또는 지금 수평 방향), 남은 Δv에 면 몫 (src/KspBridge.Mod.Modules.Ascent/CircularizeLogic.cs) — C7·C8
- [x] T051 단위 테스트: `circularize` `inclination_deg` 범위 (tests/KspBridge.Mod.Modules.Ascent.Tests/AscentArgsTests.cs) — C2
- [x] T052 [P] 단위 테스트: 대체 규칙(위도 −0.1°에서 0 → 정동, 179.95 → 정서), `circularize`용 β (tests/KspBridge.Mod.Modules.Ascent.Tests/LaunchAzimuthTests.cs) — C3·C8
- [x] T053 [P] 단위 테스트: 동압 1 kPa 아래에서 추력 중이면 셈, 추력 없으면 세지 않음 (tests/KspBridge.Mod.Modules.Ascent.Tests/ControlLossRuleTests.cs, tests/KspBridge.Mod.Modules.Ascent.Tests/AscendLogicTests.cs) — C7
- [x] T054 단위 테스트: `inclination_deg`의 연소 방향, 남은 Δv에 면 몫, 낼 수 없는 경사각의 시작 실패 (tests/KspBridge.Mod.Modules.Ascent.Tests/CircularizeLogicTests.cs) — C7·C8
- [x] T055 단계 설명: 두 단계에 같은 `inclination_deg`를 주라는 안내, `circularize`의 `inclination_deg`, 조종 상실 조건 (src/KspBridge.Mod.Modules.Ascent/AscendStep.cs, src/KspBridge.Mod.Modules.Ascent/CircularizeStep.cs), 같은 안내와 예시 (ReadMe.md), 요구·이력(R12 경사각 시험 포함) (docs/features/flight-ascent.md) — C1·C2·C3·C7·C8·C12

체크포인트: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과하고, flight-ascent.md의 요구가 spec C1·C2·C3·C7·C8·C12와 맞는다.

## 단계 7: 다시 하는 게임 검증

- [x] T056 게시(T040과 같은 명령)하고 `list_step_types`의 새 설명을 실제 MCP 호출로 확인한 뒤, `debug_load` `before-36`의 `Kerbal X`로 C12 경사각(`ascend` → `warp` → `circularize`, 두 단계에 `inclination_deg` 45). 비행 중 10~20초마다 이상을 본다 — C1·C3·C7·C8·C12
- [x] T058 `debug_load` `before-36`의 `Kerbal X`로 C12 궤도 진입을 다시 한다(D017 뒤 위험 9와 경사각 없는 `circularize` 경로, D019). 비행 중 10~20초마다 이상을 본다 — C3·C4·C5·C6·C7·C8·C9·C12
- [x] T057 `debug_launch`로 `Ascent Flip Test`를 올려 C12 조종 상실 — C7·C12
- [x] T046 시험이 끝나면 `debug_load` `before-36`으로 T039의 발사대 상태로 되돌린다 — C12

체크포인트: 작업 문서 `## 검증` 절에 T056·T058·T057의 명령·도구 호출과 결과가 있고, C1~C12마다 최종 확인 결과나 확인하지 못한 이유가 있다.

## 의존 관계

- 단계 1(T001~T006)은 ascent 모듈이 공개 형식을 쓰므로 T019·T020·T030~T033보다 먼저 한다. T001 → T002~T006(옮긴 `ControlStep`을 쓴다).
- T007·T008 → T011~T029. T009는 독립. T010은 T007·T008·T009 뒤에 한다.
- T011·T012 → T013~T020. T013~T018 → T019·T020(규칙을 합친다).
- 테스트 T021~T029는 짝이 되는 코드 뒤에 한다.
- T030·T031 → T032·T033 → T034. T035·T036은 T032·T033 뒤에 한다.
- T037·T038은 구현이 끝난 뒤에 한다(기준 spec은 구현된 요구를 적는다).
- T039는 시험 뒤 되돌아갈 저장본(발사대의 `Kerbal X`)을 게시 전의 Mod로 만들므로 T040보다 먼저 한다. T040 → T041~T045. T045는 에디터 장면으로 가므로 비행 시험(T041~T044) 뒤에 한다. T046은 단계 7의 마지막(T057 뒤)에 한다.
- T047 → T050·T051. T048 → T050·T052. T049 → T053. T050 → T054. T047~T054 → T055 → T056 → T058 → T057.
