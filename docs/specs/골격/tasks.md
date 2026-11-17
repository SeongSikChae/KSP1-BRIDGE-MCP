# 골격: P-C-C 구조와 MCP·Mod 핫리로드 기반 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고, 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 솔루션 뼈대

- [x] T001 솔루션, 공통 설정, CPM, 로컬 경로 예시, `.gitignore`에 `KspBridge.user.props` 추가 (`KspBridge.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `KspBridge.user.props.example`, `.gitignore`) — C1
- [x] T002 [P] 통신 규약 버전, 경로 상수, 오류 코드 (`src/Shared/Protocol.cs`) — C5, C6

체크포인트: `dotnet build KspBridge.slnx`가 경고 0으로 성공한다(빈 솔루션 포함).

## 단계 2: 서버 호스트와 SDK 시험

- [x] T003 [P] 서버 구성 요소. `ExitCode`, `AddServerSerilog`, 루프백 전용 `ConfigureServerKestrel` (`src/KspBridge.Server.Core/`) — C1
- [x] T004 서버 기동 흐름. 인자, YAML `AppConfiguration`과 검증, 종료 코드, ProblemDetails, `/health`, MCP 엔드포인트 `/mcp` (`src/KspBridge.Server/Program.cs`, `src/KspBridge.Server/AppConfiguration.cs`) — C1
- [x] T005 SDK 시험. 실행 중에 도구 컬렉션을 추가·제거하면 HTTP 클라이언트(이 세션)가 `list_changed`를 받는지, 서버를 다시 실행한 뒤 세션이 다시 맺어지는지 확인하고 결과를 이 줄 아래에 적는다. 시험 코드는 남기지 않는다(`src/KspBridge.Server/` 임시) — C3
  - 결과(2026-11-18, SDK 2.4.0, SDK 클라이언트로 시험):
    - 기본 세션 모드가 Stateless(2026-07-28 개정)라 서버가 먼저 알림을 보낼 수 없다. `SessionMode = Stateful`이어야 `listChanged=true`가 광고되고 `list_changed`가 도착한다.
    - HTTP 세션마다 `McpServerOptions`가 따로 만들어진다. `ConfigureSessionOptions`에서 모든 세션의 `ToolCollection`을 공유 컬렉션 하나로 지정하면, 실행 중 추가·제거가 모든 세션의 목록과 호출에 바로 반영된다. 도구 생성 시 `Services`를 넘겨야 DI 매개변수가 풀린다.
    - 서버를 다시 실행하면 SDK 클라이언트는 이전 세션을 복구하지 못했다(요청 취소). Claude Code의 동작은 T025/T028에서 확인한다.

체크포인트: 서버 exe가 `/health`에 응답하고, T005 결과가 plan의 가정과 맞는다. 맞지 않으면 멈추고 사용자와 다시 정한다.

## 단계 3: 서버 모듈 세대

- [x] T006 [P] 서버 모듈 계약. `IBridgeClient`, 모듈 표식 attribute (`src/KspBridge.Server.Contracts/`) — C9
- [x] T007 모듈 세대 로더(collectible ALC, `LoadFromStream`, 공유 어셈블리 위임), 매니페스트 감시와 디바운스, 도구 컬렉션 교체, 호스트 도구 `reload` (`src/KspBridge.Server/Modules/`) — C3
- [x] T008 Mod HTTP 클라이언트. 규약 헤더, 3 s 시간 제한, `mod_unreachable`/`mod_busy`/`protocol_mismatch` 오류 (`src/KspBridge.Server/Bridge/BridgeClient.cs`) — C5, C6
- [x] T009 서버 테스트. 세대 교체와 언로드(WeakReference), `list_changed`, Mod 미응답 시간 초과, 종료 코드 1·2 (`tests/KspBridge.Server.Tests/`) — C1, C3, C6
- [x] T010 [P] 루프백 바인딩 테스트 (`tests/KspBridge.Server.Core.Tests/`) — C1

체크포인트: `dotnet test`의 서버 테스트가 모두 통과한다.

## 단계 4: Mod 호스트와 코어

- [x] T011 Loader. KSPAddon(DontDestroyOnLoad), `HttpListener`, 메인 스레드 큐(2 s 대기), `ILogic`/`ILoaderHost` 계약, 바이트 로드와 AssemblyResolve 세대 로더, 매니페스트 감시, `/loader/status`, `/loader/reload`, 상태 이월 (`src/KspBridge.Mod.Loader/`) — C2, C4, C6
- [x] T012 Mod.Core 기반. 로직 진입점, JSON, 라우터, 규약 검사, 모듈 레지스트리(`IBridgeModule`, `IStepType`, `IStepRun`), 구독 정리기 (`src/KspBridge.Mod.Core/`) — C2, C5, C9
- [x] T013 Coordinator. 계획 모델, `FixedUpdate` 단계 판정, 단계 시간 초과, 중단, 이벤트 버퍼, 이월 스냅샷 (`src/KspBridge.Mod.Core/Coordination/`) — C4, C7
- [x] T014 Controller. 계획 실행 중에만 `OnFlyByWire`에 붙고 떼기, 기체 전환 처리, 브리지 처리기 수 집계 (`src/KspBridge.Mod.Core/Control/`) — C4
- [x] T015 비행 세션 추적(발사·되돌리기·씬·기체 전환으로 계획 종료와 `plan=null`), 준비 여부, `core/status` 처리기 (`src/KspBridge.Mod.Core/Session/`, `src/KspBridge.Mod.Core/Status/`) — C5, C8
- [x] T016 Mod.Core 테스트. JSON, 계획 파싱, Coordinator 상태 기계, 이벤트 버퍼, 이월 (`tests/KspBridge.Mod.Core.Tests/`) — C4, C7

체크포인트: Mod 계열이 net472로 경고 0으로 빌드되고, `KspBridge.Mod.Core.Tests`가 통과한다.

## 단계 5: 기능 모듈

- [x] T017 [P] `wait` 단계 종류 (`src/KspBridge.Mod.Modules.Wait/`) — C7, C9
- [x] T018 [P] Debug 처리기. 발사, 되돌리기, 우주센터 이동 (`src/KspBridge.Mod.Modules.Debug/`) — C8, C9
- [x] T019 [P] 코어 도구. `get_status`, `submit_plan`, `get_plan`, `abort_plan`, `get_flight_events`, `list_step_types` (`src/KspBridge.Server.Modules.Core/`) — C5, C7
- [x] T020 [P] 대기 도구 `submit_wait` (`src/KspBridge.Server.Modules.Wait/`) — C7, C9
- [x] T021 [P] Debug 도구. `debug_launch`, `debug_revert`, `debug_space_center` (`src/KspBridge.Server.Modules.Debug/`) — C8, C9

체크포인트: 솔루션 전체가 경고 0으로 빌드되고, 모든 테스트가 통과한다.

## 단계 6: 배포와 문서

- [x] T022 배포 스크립트. 빌드, 경로 읽기, 고정 호스트 파일은 바뀌었을 때만 복사, 잠겨 있으면 재시작 안내, 모듈 복사 뒤 매니페스트를 마지막에 기록 (`scripts/deploy.ps1`) — C1
- [x] T023 [P] ReadMe. 구조, 빌드·배포, 서버 실행, MCP 등록, 모듈 추가 방법 (`ReadMe.md`) — C1, C9

체크포인트: `scripts/deploy.ps1` 실행 뒤 `GameData/KspBridge`와 `ServerDir`에 파일과 매니페스트가 있다.

## 단계 7: 실제 검증

KSP 확인은 MCP 응답으로만 한다. 장면 전환 전에는 `get_status`로 상태를 확인한다.

- [x] T024 깨끗한 클론에서 빌드와 배포, 서버 exe 종료 코드와 `/health` 확인 — C1
- [x] T025 서버를 백그라운드로 실행하고, 사용자 승인 뒤 MCP 클라이언트에 등록 — C3, C5
  - 진행(2026-11-18): 서버를 실행하고 `~/.claude.json` 사용자 범위에 `kspbridge`(http)를 등록했다. 세션을 다시 연 뒤 도구가 보였다.
- [x] T026 KSP를 끈 상태에서 `mod_unreachable`(3 s 안)을 확인하고, KSP를 켠 뒤 `get_status` 필드를 대조 — C5, C6
- [x] T027 `wait` 2단계 계획의 완료 이벤트와 `abort_plan` 확인 — C7
- [x] T032 세대 표식 이름 빌드. `ModBuildStamp`가 있으면 Mod 로직 어셈블리 이름에 세대 표식을 붙이고, 배포 스크립트는 표식 붙은 파일을 매니페스트에 넣고 이전 세대 DLL을 지우며 (`src/KspBridge.Mod.Core/KspBridge.Mod.Core.csproj`, `src/KspBridge.Mod.Modules.Wait/KspBridge.Mod.Modules.Wait.csproj`, `src/KspBridge.Mod.Modules.Debug/KspBridge.Mod.Modules.Debug.csproj`, `scripts/deploy.ps1`) — C2
  - 추가(2026-11-18): T028 첫 시도에서 Unity Mono가 같은 단순 이름의 이전 세대 어셈블리를 돌려줘 2세대 로드가 거부됐다. plan.md 결정 갱신에 따른 작업.
- [x] T028 Mod.Core 표식 필드 reload(`mod.n` 증가)와 서버 시험 도구 추가·설명 변경을 클라이언트 재시작 없이 확인한 뒤 되돌림 — C2, C3
- [x] T029 계획 실행 중 `reload`. `aborted/reload`, `bridge_flybywire_handlers=0` — C4
- [x] T030 계획 실행 중 `debug_revert`와 `debug_space_center`. `end_reason`, `plan=null` — C8
- [x] T031 `git grep`으로 코어에 기능 이름이 없는지 확인하고, `list_step_types`에 `wait`가 있는지 확인 — C9

체크포인트: C1~C9를 모두 확인하고, 실행한 명령과 결과를 보고에 남긴다.

## 의존 관계

- 단계 1 → 나머지 전부(솔루션과 공통 설정이 먼저 있어야 함).
- T005(SDK 시험)는 T007보다 먼저 한다. 결과가 plan의 가정과 다르면 단계 3으로 가지 않는다.
- T006 → T007, T019~T021(모듈이 계약을 참조).
- T011 → T012 → T013, T014, T015(Core가 Loader 계약을 구현하고, Coordinator·Controller·세션이 Core 기반을 씀).
- T012 → T017, T018. T007·T008 → T019~T021.
- 단계 2~5 → T022(배포할 산출물이 있어야 함).
- T032 → T028~T031(Mod 로직 reload가 되어야 KSP 검증을 이어 갈 수 있음).
- 단계 6 → 단계 7. T026은 T025 뒤에, T027~T031은 T026 뒤에 한다.
