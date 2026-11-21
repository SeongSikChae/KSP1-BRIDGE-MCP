# 관측: 비행 상태 조회와 계획 진행 표시 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 코어 계획 진행 보고

- [x] T001 `IStepProgress` 인터페이스 추가 (src/KspBridge.Mod.Core/ModuleApi.cs) — C8
- [x] T002 `Plan.ToDict(double? now)`와 진행 계산: 실행 중 계획에만 `elapsed_s`·`step_elapsed_s`·`step_timeout_remaining_s`·`step_progress`·`step_progress_error`를 붙인다. 현재 단계가 시작되지 않았으면(제출 직후·단계 사이) 단계 값은 `null`이다. 진행 보고 전에 그 단계 `StepContext.UT`를 지금 UT로 맞추고, 예외는 잡아 `step_progress_error`로 돌린다 (src/KspBridge.Mod.Core/Coordination/Plan.cs) — C7·C8
- [x] T003 `CurrentSummaryWithProgress()` 추가, `Submit` 응답을 진행 값 판으로. `CurrentSummary`·`LastSummary`는 그대로 (src/KspBridge.Mod.Core/Coordination/Coordinator.cs) — C7
- [x] T004 `plan.get`의 `current`가 `CurrentSummaryWithProgress()`를 쓰게 함. `StatusPlan`은 그대로 (src/KspBridge.Mod.Core/Status/CoreModule.cs) — C7
- [x] T005 `WaitRun`이 `IStepProgress` 구현(`target_s`, `remaining_s` ≥ 0) (src/KspBridge.Mod.Modules.Wait/WaitModule.cs) — C8
- [x] T006 [P] `get_plan`·`submit_plan` 설명에 진행 값 필드 설명 추가(동작 그대로) (src/KspBridge.Server.Modules.Core/CoreTools.cs) — C7·C12
- [x] T007 [P] 시험 프로젝트에 wait 모듈 참조 추가 (tests/KspBridge.Mod.Core.Tests/KspBridge.Mod.Core.Tests.csproj) — C8
- [x] T008 진행 값 시험: 제출 직후 단계 값 `null`·계획 경과 시간 있음, 실행 중 값, 단계 사이 `null`, timeout 남은 시간, 진행 보고 예외 시 계획 계속·`step_progress_error`, 진행 보고 안 하는 단계 `null`, 끝난 계획(`LastSummary`)과 `CurrentSummary`에 진행 값 없음 (tests/KspBridge.Mod.Core.Tests/CoordinatorTests.cs) — C7·C8
- [x] T009 wait 진행 보고 시험: `WaitModule.Register`로 등록한 단계의 `target_s`·`remaining_s`, 시간이 지나도 0 미만이 아님 (tests/KspBridge.Mod.Core.Tests/WaitProgressTests.cs) — C8

체크포인트: `dotnet test tests/KspBridge.Mod.Core.Tests`가 통과한다.

## 단계 2: flight Mod 모듈

- [x] T010 [P] 프로젝트 파일(check 모듈 틀, `InternalsVisibleTo` `KspBridge.Mod.Modules.Flight.Tests`) (src/KspBridge.Mod.Modules.Flight/KspBridge.Mod.Modules.Flight.csproj) — C12
- [x] T011 [P] 순수 3D 벡터 (src/KspBridge.Mod.Modules.Flight/Vec.cs) — C2
- [x] T012 자세 계산: 피치 = asin(f·up), 헤딩 = atan2(f·east, f·north) 0~360, 롤 = 수평 기준 윗면과 윗면 사이 각(오른쪽 날개 내려가면 +) (src/KspBridge.Mod.Modules.Flight/Attitude.cs) — C2·C11
- [x] T013 [P] 스테이지 판정: 입력은 `currentStage`와 스테이징 아이콘 있는 부품의 (번호, 작동 상태) 목록. 마지막으로 작동한 스테이지(작동 부품이 남으면 `currentStage`, 없으면 `null`), 다음 스테이지(`IDLE` 부품의 최대 번호), 기준 스테이지 (src/KspBridge.Mod.Modules.Flight/StageRef.cs) — C2
- [x] T014 [P] 스냅숏 데이터 형(위치·속도·궤도·자세 벡터·조종 상태·질량·추력·중력·스테이지 부품·계산기 스테이지 값·자원·천체) (src/KspBridge.Mod.Modules.Flight/FlightData.cs) — C2·C5
- [x] T015 [P] 준비 판정: 코어 장면 차단 사유, 비행 장면 아님(지금 장면 이름), 활성 기체 없음 → `not_ready` 이유. 입력 잠금은 보지 않음 (src/KspBridge.Mod.Modules.Flight/FlightReadiness.cs) — C10
- [x] T016 보고서: `FlightData` → 응답 사전. 열린 궤도 `null`, 현재 TWR(질량·중력 0이면 0), 경도 −180~180, 기준 스테이지 값(계산기 값 없으면 0), 계산 준비 전 `null`과 이유, `include`별 스테이지 표(기준 스테이지 이하)·자원 목록(이름 순)·천체 정보(영향권 무한이면 `null`), 모르는 `include`는 `bad_request` (src/KspBridge.Mod.Modules.Flight/FlightReport.cs) — C2·C3·C5·C6·C10
- [x] T017 게임 값 읽기: 활성 기체 위치·고도, 지형 높이(`TerrainAltitude(allowNegative: true)`, `body.ocean`이면 음수 0), 속도, 궤도, `up`·`north`·`east`·`ReferenceTransform`, 스로틀·SAS·RCS, `GetTotalMass`, 엔진 `finalThrust` 합, 중력, 스테이징 아이콘 있는 부품 번호·`Part.State`, `VesselDeltaV`(`IsReady`, `OperatingStageInfo`, 합계), 자원 합계, 천체 상수 (src/KspBridge.Mod.Modules.Flight/FlightSnapshot.cs) — C2·C3·C4·C5
- [x] T018 모듈 등록: 처리기 `get`(인자 `include` 검사 → 준비 판정 → 스냅숏 → 보고서). 게임 예외는 잡아 오류 응답으로 (src/KspBridge.Mod.Modules.Flight/FlightModule.cs) — C1·C10

체크포인트: flight Mod 모듈이 빌드된다.

## 단계 3: flight 시험

- [x] T019 [P] 시험 프로젝트(check 시험 틀) (tests/KspBridge.Mod.Modules.Flight.Tests/KspBridge.Mod.Modules.Flight.Tests.csproj) — C2
- [x] T020 자세 시험: 수직(피치 90), 동쪽 수평(헤딩 90·피치 0), 북쪽, 롤 0과 오른쪽으로 30° 기운 경우(+30), 왼쪽(−30) (tests/KspBridge.Mod.Modules.Flight.Tests/AttitudeTests.cs) — C2·C11
- [x] T021 스테이지 판정 시험: 발사 전(모두 `IDLE`), 스테이징 없이 궤도로 옮긴 기체, 고정대가 잡은 채 점화, 부스터 분리 뒤 앞 스테이지 엔진이 남은 경우, 작동 부품이 모두 떨어진 경우, 분리를 마친 분리기만 남은 경우(아이콘 없음 → 세지 않음), 남은 스테이지 없음 (tests/KspBridge.Mod.Modules.Flight.Tests/StageRefTests.cs) — C2
- [x] T022 보고서 시험: 기본 필드 구성, 열린 궤도 `null`, 현재 TWR 식, 계산 준비 전 `null`과 이유(나머지 값은 정상), `include` 유무에 따른 키 차이(C2 값 구성 같음), 스테이지 표 범위, 자원 합계·이름 순, 천체 정보와 Sun 영향권 `null`, 모르는 `include` `bad_request`, 기준 스테이지 계산기 값 없음 → 0 (tests/KspBridge.Mod.Modules.Flight.Tests/FlightReportTests.cs) — C2·C3·C5·C6·C10
- [x] T023 준비 판정 시험: 로딩 중, 비행 장면 아님(장면 이름 포함), 비행 준비 안 됨, 활성 기체 없음, 준비됨 (tests/KspBridge.Mod.Modules.Flight.Tests/FlightReadinessTests.cs) — C10

체크포인트: `dotnet test tests/KspBridge.Mod.Modules.Flight.Tests`가 통과한다.

## 단계 4: flight 서버 모듈

- [x] T024 [P] 프로젝트 파일(check 서버 모듈 틀) (src/KspBridge.Server.Modules.Flight/KspBridge.Server.Modules.Flight.csproj) — C12
- [x] T025 `get_flight` 도구(`ReadOnly`·`Idempotent`, 인자 `include` 문자열 배열, 설명에 필드·단위·`include` 값) (src/KspBridge.Server.Modules.Flight/FlightTools.cs) — C1·C5

체크포인트: 서버 flight 모듈이 빌드된다.

## 단계 5: debug 궤도 설정

- [x] T026 [P] 궤도 요청 검증·변환(순수): 천체 이름(대소문자 무시, 생략 시 지금 천체), Ap ≥ Pe, Pe 하한(대기 상한, 대기 없으면 `radiusMax − radius`, 모르면 거절), rA = R + Ap < 영향권 반경(천체 중심 기준), 장반경 ≤ 0.99 × 영향권(무한이면 생략), 경사각 기본 0, 장반경·이심률 계산 (src/KspBridge.Mod.Modules.Debug/OrbitRequest.cs) — C9·C10
- [x] T027 [P] 대기 상태 기계(순수): 대기 → 적용됨 / 시간 초과(10초) / 취소, 대기 중 새 요청 거절 (src/KspBridge.Mod.Modules.Debug/PendingOrbit.cs) — C9·C10
- [x] T028 처리기 `set_orbit.start`(준비 판정 `not_ready`, 계획 실행 중 `bad_request`, 검증 `bad_request`, `SetShipOrbit` 호출)·`set_orbit.result`(천체·`ORBITING`·Ap/Pe 1 km 확인), 장면 전환·모듈 정지 때 취소 (src/KspBridge.Mod.Modules.Debug/DebugModule.cs) — C9·C10
- [x] T029 [P] `InternalsVisibleTo` `KspBridge.Mod.Modules.Debug.Tests` 추가 (src/KspBridge.Mod.Modules.Debug/KspBridge.Mod.Modules.Debug.csproj) — C9
- [x] T030 `debug_set_orbit` 도구(시작 뒤 50 ms 간격으로 결과 되묻기, 15초 상한) (src/KspBridge.Server.Modules.Debug/DebugTools.cs) — C9
- [x] T031 [P] 시험 프로젝트(check 시험 틀, debug 모듈 참조) (tests/KspBridge.Mod.Modules.Debug.Tests/KspBridge.Mod.Modules.Debug.Tests.csproj) — C9
- [x] T032 궤도 요청 시험: 변환 값, Ap < Pe, 대기 상한 이하 Pe, 대기 없는 천체 지형 하한·모름, 영향권 밖 rA, 장반경 상한, 무한 영향권 생략, 없는 천체, 기본 경사각 (tests/KspBridge.Mod.Modules.Debug.Tests/OrbitRequestTests.cs) — C9·C10
- [x] T033 대기 상태 기계 시험: 적용, 10초 시간 초과, 취소, 대기 중 새 요청 거절, 끝난 뒤 새 요청 허용 (tests/KspBridge.Mod.Modules.Debug.Tests/PendingOrbitTests.cs) — C9·C10

체크포인트: `dotnet test tests/KspBridge.Mod.Modules.Debug.Tests`가 통과한다.

## 단계 6: 솔루션·문서

- [x] T034 새 프로젝트 넷 추가(Mod·서버 flight 모듈, flight·debug 시험) (KspBridge.slnx) — C12
- [x] T035 도구 목록에 `get_flight`·`debug_set_orbit`, `get_plan` 진행 값 설명(같은 계획 요약을 돌려주는 `submit_plan`·`submit_wait` 응답에도 붙는다는 한 문장 포함) (ReadMe.md) — C1·C7·C9

체크포인트: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과한다.

## 단계 7: 기준 spec 반영(마지막 구현 단계)

- [x] T036 [P] 새 기준 spec: 파일 줄(flight 모듈 쌍·시험), 요구 R1~(spec C1~C6·C10·C11 중 `get_flight` 부분), 이력 (docs/features/flight-observation.md) — C1~C6·C10·C11
- [x] T037 [P] 새 기준 spec(기존 기능, 기준 spec 없음): 파일 줄(코어 계획 실행·wait·CoreTools·시험), 요구(지금 계획 실행 동작 + C7·C8, 오류 코드 범위 D021~D023), 이력(골격과 이번 작업) (docs/features/flight-plan.md) — C7·C8
- [x] T038 [P] 새 기준 spec(기존 기능, 기준 spec 없음): 파일 줄(debug 모듈 쌍·시험), 요구(지금 `debug_launch`·`debug_revert`·`debug_space_center` 동작 + C9·C10의 `debug_set_orbit`), 이력 (docs/features/debug-tools.md) — C9·C10

체크포인트: 세 기준 spec의 `파일:` 줄이 실제 파일과 맞는다.

## 단계 8: 실제 게임 검증

- [x] T039 장면을 바꾸는 호출(`debug_launch`·`debug_space_center`·`debug_revert`·`editor_enter`·`editor_load`) 전마다 `get_status`로 다른 비행이 없는지 확인한다. 다른 사람의 비행이 있으면 멈추고 사용자에게 묻는다(T042~T046 내내 지킨다) — C11
- [x] T040 서버 모듈·Mod 세트 게시와 핫리로드. 실행 중인 Claude Code에서 다시 연결하지 않고 `get_flight`·`debug_set_orbit`이 나타나 호출되는지 확인 — C12
- [x] T041 `git diff --stat`(기준점부터)으로 바뀐 파일이 C12 허용 목록 안인지 확인 — C12
- [x] T042 우주센터에서(우주센터가 아니면 `debug_space_center`로 이동) `get_flight`·`debug_set_orbit` → `not_ready` 확인 — C10
- [x] T043 `ComSat Lx`: `editor_enter`(VAB) → `editor_load` → `editor_get_ship`(`mass_t`, 부품 id) → 부품마다 `editor_get_part_settings`(자원 현재량·최대량) → `check_ship`(Kerbin) → `debug_launch` → `get_flight include=["stages","resources"]`로 질량 0.01 t·자원별 합계·스테이지별 해면·진공 Δv 1% 대조, 발사대 지형 높이(해발 고도 − 지형 위 고도) 기록 — C4·C11
- [x] T044 `Kerbal X`: `debug_space_center` → `editor_enter`(VAB) → `editor_load` → `editor_get_stages` → `debug_launch` → `get_flight`(기본·`include` 전부)로 C11 발사대 항목 전부(발사대 지형 높이를 T043 값과 0.5 m 안에서 대조), 스테이지 번호 집합, `include` 유무 키 비교, 계산 준비 표시가 나오면 기록 — C1·C2·C3·C5·C6·C11
- [x] T045 T044의 비행 장면(`Kerbal X` 발사대)에서 이어서 `submit_plan`(wait 30초, timeout 60초) 응답 진행 값 → 몇 초 뒤 `get_plan` 진행 값 → `abort_plan` → `submit_wait`(10초) 응답 진행 값 확인 — C7·C8
- [x] T046 `Aeris 3A`: 비행 장면에서 `debug_launch`(SPH, 활주로) → `get_flight`로 헤딩·롤 확인 → `submit_wait`(60초) → `debug_set_orbit` `bad_request` 확인 → `get_plan`(계획 그대로 실행 중)·`get_flight`(상황 그대로)로 변화 없음 확인 → `abort_plan` → 잘못된 인자(Ap < Pe, Pe 60 km, 없는 천체) `bad_request` → `debug_set_orbit`(Kerbin 120/90 km, 30°) → 첫 `get_flight`로 C9·C11 지정 궤도 항목 계산 대조 → `debug_set_orbit`(Mun, Ap 50 km, Pe 1 km) `bad_request` — C9·C10·C11

체크포인트: C1~C12 확인 결과를 명령·응답 값과 함께 보고에 적는다.

## 의존 관계

- 단계 1의 T001 → T002 → T003 → T004 순서(인터페이스 → 요약 계산 → 코디네이터 → 핸들러). T005는 T001 뒤, T008·T009는 T002~T005 뒤, T009는 T007 뒤(wait 모듈 참조).
- 단계 2: T011 → T012, T014 → T016·T017, T013·T015 → T016, T016·T017 → T018. 단계 2는 단계 1과 독립이다.
- 단계 3은 단계 2의 해당 파일 뒤이고, T010·T019 → T020~T023(시험 프로젝트와 `InternalsVisibleTo`가 있어야 빌드). 단계 4는 단계 2와 독립이지만 응답 형태를 맞추려고 T016 뒤에 하고, T024 → T025(프로젝트 파일이 있어야 빌드).
- 단계 5는 단계 1·2와 독립. T026·T027 → T028, T028 → T030, T029·T031 → T032·T033(시험 프로젝트와 `InternalsVisibleTo`가 있어야 빌드).
- 단계 6은 단계 1~5 뒤(모든 프로젝트가 있어야 솔루션 빌드). 단계 7은 단계 6 뒤(파일 목록 확정). 단계 8은 단계 7 뒤.
