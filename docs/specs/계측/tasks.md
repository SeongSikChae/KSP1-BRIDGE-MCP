# 게임 검증용 계측 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 프로젝트 뼈대

- [x] T001 Mod 계측 모듈 프로젝트를 만든다. flight 모듈 프로젝트와 같은 구성에 flight 모듈 프로젝트 참조와 테스트용 `InternalsVisibleTo`를 더한다 (src/KspBridge.Mod.Modules.Measure/KspBridge.Mod.Modules.Measure.csproj) — C1
- [x] T002 [P] 서버 계측 모듈 프로젝트를 만든다. wait 서버 모듈 프로젝트와 같은 구성이다 (src/KspBridge.Server.Modules.Measure/KspBridge.Server.Modules.Measure.csproj) — C1
- [x] T003 [P] 테스트 프로젝트를 만든다. flight 모듈 테스트 프로젝트와 같은 구성에 계측·flight 모듈 참조를 둔다 (tests/KspBridge.Mod.Modules.Measure.Tests/KspBridge.Mod.Modules.Measure.Tests.csproj) — C1
- [x] T004 새 프로젝트 셋을 솔루션에 더한다 (KspBridge.slnx) — C1

체크포인트: `dotnet build KspBridge.slnx`가 통과한다.

## 단계 2: 게임에 닿지 않는 부분과 단위 테스트

- [x] T005 표본 한 벌의 값(struct)과 항목 표(응답 이름, 수치/이산 구분, 값 읽는 법)를 만든다 (src/KspBridge.Mod.Modules.Measure/Sample.cs) — C6
- [x] T006 계측 상태 기계를 만든다. 시작(실행 중이면 거절, 끝난 계측 버림), 표본 추가(UT가 진행하지 않으면 건너뜀, 상한에 이르면 `sample_limit`로 끝냄), 끝내기(사유·끝 UT·오류 설명), 번호 매기기 (src/KspBridge.Mod.Modules.Measure/Recorder.cs) — C2, C3, C5, C9
- [x] T007 상태 기계의 단위 테스트를 쓴다. 시작·끝, 실행 중 재시작 거절, 끝난 계측 교체, 상한 도달 시 사유와 계측 전체 표본 수, 진행하지 않은 UT 건너뜀, 사유 `error`와 오류 설명 (tests/KspBridge.Mod.Modules.Measure.Tests/RecorderTests.cs) — C2, C3, C5, C9, C10
- [x] T008 [P] 표본 상한 인자 검증(정수, 1 이상 180,000 이하, 기본 30,000), 준비 판정(이유 문장), 게임 이벤트에서 종료 사유를 정하는 규칙을 만든다 (src/KspBridge.Mod.Modules.Measure/MeasureArgs.cs) — C2, C9, C10
- [x] T009 T008의 단위 테스트를 쓴다. 상한 경계값, 준비 판정의 이유 문장, 이벤트와 사유의 연결(`new_flight`·`scene_change`·`vessel_change`) (tests/KspBridge.Mod.Modules.Measure.Tests/MeasureArgsTests.cs) — C2, C9, C10
- [x] T010 [P] 게임 각속도 벡터(rad/s)를 피치·요·롤 각속도(°/s, D008 부호)로 바꾸는 계산을 만든다 (src/KspBridge.Mod.Modules.Measure/AngularRate.cs) — C6
- [x] T011 T010의 단위 테스트를 쓴다. `Vec`로 ω × r을 직접 계산해 세 방향의 부호를 확인한다 (tests/KspBridge.Mod.Modules.Measure.Tests/AngularRateTests.cs) — C6
- [x] T012 조회 응답을 만든다. 계측 없음, 머리 정보(상태·UT·사유·오류 설명·계측 전체 표본 수·표본 상한·기체 이름), 범위 고르기와 범위 안 표본 수, 수치 항목 요약, 이산 항목의 변화 목록, 프레임 간격, 표본 추리기(`stride`, 열 이름과 행), 조회 인자 검증 (src/KspBridge.Mod.Modules.Measure/MeasureReport.cs) — C7, C8, C10
- [x] T013 T012의 단위 테스트를 쓴다. 전체·범위 요약, 빈 범위, 표본 하나인 범위, 변화 목록, 프레임 간격, 표본 추리기와 `stride`, 항목 선택, 계측 없음, 실행 중 계측, 조회 인자 오류, 사유 `error`의 오류 설명과 다른 사유의 `null`, 수치·이산 항목이 모두 요약에 나오는 것 (tests/KspBridge.Mod.Modules.Measure.Tests/MeasureReportTests.cs) — C6, C7, C8, C10

체크포인트: `dotnet test KspBridge.slnx`가 통과한다.

## 단계 3: 게임에 닿는 부분

- [x] T014 활성 기체에서 표본을 읽는다. 읽는 곳은 plan.md "표본 항목" 표를 따르고, 스테이징 아이콘 판정은 flight 모듈과 같은 식으로 두며 출처를 주석에 적는다. 부품의 모듈 목록을 직접 돌아 프레임마다 새 목록을 만들지 않는다 (src/KspBridge.Mod.Modules.Measure/SampleReader.cs) — C5, C6
- [x] T015 계획 단계 종류 `measure_start`·`measure_stop`을 만든다. 제출 때 상한 검증, 시작 프레임에 실행하고 같은 프레임에 완료, 활성 기체가 없거나 계측 상태가 맞지 않으면 예외로 단계 실패, 조종 입력 없음 (src/KspBridge.Mod.Modules.Measure/MeasureSteps.cs) — C4, C10
- [x] T016 모듈을 등록한다. 요청 처리(`start`·`stop`·`get`), 단계 종류 등록, 타이밍 훅 걸기·떼기, 물리 프레임 콜백(표본 읽기, 예외는 사유 `error`로 끝냄), 게임 이벤트 구독, 세대가 멈출 때의 해제 등록 (src/KspBridge.Mod.Modules.Measure/MeasureModule.cs) — C1, C2, C3, C5, C9, C10

체크포인트: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과한다.

## 단계 4: MCP 도구와 문서

- [x] T017 MCP 도구 `measure_start`·`measure_stop`·`get_measurement`를 만든다. 도구 설명에 항목 이름과 단위, 각속도·조종 입력의 + 방향, 헤딩·롤 평균이 각도 넘김을 보정하지 않는다는 사실, 표본 상한의 기본값과 최대값을 적는다 (src/KspBridge.Server.Modules.Measure/MeasureTools.cs) — C1, C2, C3, C6, C7, C8
- [x] T018 [P] 도구 표에 세 도구를 더하고 계획 단계와 표본 상한을 짧게 설명한다. 기존 내용은 바꾸지 않는다 (ReadMe.md) — C1

체크포인트: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과한다.

## 단계 5: 기준 spec 반영

- [x] T019 새 기능의 기준 spec을 만든다. `파일:` 줄, spec.md의 완료 조건을 옮긴 요구(R번호), 이력 한 줄 (docs/features/flight-measurement.md) — C1, C2, C3, C4, C5, C6, C7, C8, C9, C10, C11

체크포인트: 기준 spec의 `파일:` 줄이 plan.md "바꿀 파일·구조"의 새 파일과 맞고, 요구가 spec.md의 완료 조건을 빠짐없이 담는다.

## 단계 6: 게시와 게임 검증

게임 검증은 실제 `mcp__kspbridge__*` 도구 호출의 응답으로만 판정한다. 장면을 바꾸기 전에 `get_status`로 게임 상태를 확인하고, 게임이 꺼져 있거나 다른 비행이 진행 중이면 멈추고 사용자에게 알린다. 계측 판정에는 계획이 끝난 뒤의 응답만 쓴다.

- [x] T020 Mod 세트와 서버 모듈을 게시해 핫리로드로 반영한다. `get_status`로 새 세대를 확인하고, 클라이언트에 세 도구가 보이는지와 `list_step_types`에 두 단계가 나오는지 확인한다. 계측 도구 호출 전후 `get_plan`의 계획 상태가 바뀌지 않는지 본다 (파일 없음) — C1
- [x] T021 발사대의 `Kerbal X`에서 계획 `measure_start` → `wait` 5초 → `measure_stop`으로 구간 경계와 정지 상태 값을 확인하고, 같은 계측으로 표본 조회(개수 상한 10, 항목 둘 선택, 앞 절반 범위)를 확인한다. 요약에 plan.md "표본 항목" 표의 수치·이산 항목의 모든 이름이 있고, 표본 열에 `t_s`·`ut`와 그 모든 이름이 있는지도 확인한다 (파일 없음) — C2, C3, C4, C5, C6, C7, C8, C11
- [x] T022 표본 상한을 확인한다. 상한 50으로 도구 `measure_start` 뒤 `submit_wait` 5초가 끝난 다음 사유 `sample_limit`와 계측 전체 표본 수 50, 상한 0과 180,001의 `bad_request`, 그 뒤 `get_measurement`의 계측이 바뀌지 않음 (파일 없음) — C2, C9, C10, C11
- [x] T023 오류 동작을 확인한다. 우주센터 장면의 `measure_start`가 `not_ready`, 실행 중 재시작이 `bad_request`이고 실행 중이던 계측이 바뀌지 않음, 실행 중인 계측 없는 `measure_stop`이 `bad_request`, 그 뒤 `get_status` 정상 응답 (파일 없음) — C2, C3, C10, C11
- [x] T024 단계 실패와 제출 거절을 확인한다. 계획 `measure_start` → `measure_start`가 `step_failed`이고 계측은 실행 중, 계측 없는 계획 `measure_stop`이 `step_failed`, 단계 상한 0의 `submit_plan`이 `bad_request` (파일 없음) — C4, C10
- [x] T025 궤도에서 변하는 값을 확인한다. `debug_set_orbit`(Kerbin Ap 120 km·Pe 90 km) 뒤 계획 `measure_start` → `wait` 30초 → `measure_stop`의 표본마다 궤도 속도가 활력 방정식 값과 0.5% 안, 해발 고도의 처음과 끝이 다름 (파일 없음) — C5, C6, C8, C11
- [x] T026 자동 종료를 확인한다. 계측 중 `debug_revert` 뒤 사유 `new_flight`, 계측 중 `abort_plan` 뒤 계측이 실행 중, 계측 중 `debug_space_center` 뒤 사유 `scene_change` (파일 없음) — C4, C9, C11
- [x] T027 활성 기체 변경을 확인한다. `debug_launch`(`Kerbal X`, 발사대) → `debug_space_center` → `debug_launch`(`Aeris 3A`, 활주로)로 두 기체를 만들고 `debug_list_vessels`로 로드·소유를 확인한 뒤, 계측 중 `debug_switch_vessel`로 사유 `vessel_change`를 확인한다 (파일 없음) — C9
- [x] T028 Mod 다시 읽기를 확인한다. 계측을 실행한 채 `reload`한 뒤 `get_measurement`가 계측 `null`을 보인다 (파일 없음) — C9
- [x] T029 완료 조건마다 실행한 명령과 결과, 확인하지 못한 것(스로틀·추력·스테이지가 변하는 구간, 각속도·조종 입력의 부호, 게임에서의 수집 중 예외)을 작업 문서 `## 검증`에 적는다 (파일 없음) — C1, C2, C3, C4, C5, C6, C7, C8, C9, C10, C11

체크포인트: 완료 조건 C1~C11마다 확인 결과나 확인하지 못한 이유가 작업 문서에 있다.

## 의존 관계

- 단계 1 → 단계 2 → 단계 3 → 단계 4 → 단계 5 → 단계 6 순서다. 뒤 단계의 코드가 앞 단계의 프로젝트와 형식을 쓴다.
- T004는 T001~T003 뒤에 한다(더할 프로젝트 파일이 있어야 한다).
- T005가 단계 2의 나머지보다 먼저다(상태 기계와 조회가 표본 형식을 쓴다). T006 → T007, T008 → T009, T010 → T011, T012 → T013 순서다. T012는 T006 뒤에 한다(조회가 계측 상태를 읽는다).
- T014·T015는 단계 2 뒤에, T016은 T014·T015 뒤에 한다(모듈이 표본 읽기와 단계 종류를 등록한다).
- T017은 T016 뒤에 한다(도구가 부르는 Mod 요청의 이름과 인자가 정해져야 한다). T018은 T017과 동시에 할 수 있다.
- T019는 단계 1~4가 끝난 뒤에 한다(`파일:` 줄에 실제로 만든 파일을 적는다). 기준 spec 반영은 게임 검증보다 먼저 한다.
- T020이 T021~T028보다 먼저다. T028은 계측 데이터를 지우고 세대를 바꾸므로 T021~T027 뒤에 한다. T029는 마지막이다.
