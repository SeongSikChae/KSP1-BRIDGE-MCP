# Debug 보강: 활성 기체 전환과 저장·불러오기 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 순수 판정과 단위 시험

- [x] T001 [P] 저장 이름 검사 `SaveName.Parse(raw, forSave)`: `null`이면 `quicksave`, 빈 문자열·허용 글자(영문자·숫자·`-`·`_`·공백) 밖·64자 초과는 `bad_request`, `forSave`이면 `persistent`(대소문자 무시)도 `bad_request` (src/KspBridge.Mod.Modules.Debug/SaveName.cs) — C5·C6·C7·C8
- [x] T002 [P] 사전 판정 `SaveFileFacts`(`HasGame`·`Version`·`Scene`·`HasFlightState`·`VesselCount`·`ActiveVessel`·`ActivePid`)와 `SaveFileCheck.Check(facts, major, minor, revision)`. 순서는 손상(`GAME` 없음, `version` 정수 세 개 형식 아님, `scene` 정수 아님, `FLIGHTSTATE`가 있는데 `activeVessel` 없음·정수 아님) → 비행 아님(`scene`≠7, `FLIGHTSTATE` 없음, `VESSEL` 0개, `activeVessel` 범위 밖) → 활성 기체 손상(`pid` 없음·GUID 아님, 손상으로 알림) → 버전 다름(주.부.수정) (src/KspBridge.Mod.Modules.Debug/SaveFileCheck.cs) — C8
- [x] T003 [P] 사후 판정 `LoadedGameFacts`(`GameNull`·`GameCompatible`·`FlightStateNull`·`FlightStateCompatible`·`ProtoVesselIds`)와 `LoadedGameCheck.Check(facts, activePid)`. 통과하면 `activePid`와 같은 `vesselID`의 위치를 돌려주고, 아니면 이유(게임 없음, 게임 호환 아님, 비행 상태 없음, 비행 상태 호환 아님, 활성 기체 없음)를 돌려준다 (src/KspBridge.Mod.Modules.Debug/LoadedGameCheck.cs) — C7·C8
- [x] T004 [P] 전환 판정 `VesselFacts`(`Found`·`Active`·`Loaded`·`Owned`·`Name`)와 `VesselSwitchCheck.Decide(target, force, clearToSaveResult)`. 순서는 없음 → 이미 활성(`Already`) → 로드되지 않음(먼 기체는 받지 않는다는 이유) → 미소유 → `force`가 false일 때만 결과가 `CLEAR`가 아님(이유에 결과 이름) → `Proceed` (src/KspBridge.Mod.Modules.Debug/VesselSwitchCheck.cs) — C2·C3·C4
- [x] T005 [P] 저장 판정 `QuickSaveCheck.Decide(activeDead, clearToSaveResult)`: 파괴 먼저 거절, 그다음 `NOT_WHILE_ON_A_LADDER`·`NOT_WHILE_MOVING_OVER_SURFACE`·`NOT_WHILE_ABOUT_TO_CRASH` 거절(이유에 결과 이름), 그 밖은 통과 (src/KspBridge.Mod.Modules.Debug/QuickSaveCheck.cs) — C6
- [x] T006 이름 규칙 시험: 생략, 허용 글자, 공백 포함, 빈 문자열, `../x`, 65자, `persistent`·`Persistent`(저장은 거절, 불러오기는 허용) (tests/KspBridge.Mod.Modules.Debug.Tests/SaveNameTests.cs) — C6·C8
- [x] T007 사전 판정 시험: 손상 각 조건, 비행 아님 각 조건(`FLIGHTSTATE` 없음·`VESSEL` 0개·`activeVessel` 범위 밖이 손상이 아니라 비행 아님으로 나옴), 활성 기체 `pid` 없음·형식 틀림, 버전 다름(주·부·수정 각각), 판정 순서, 정상 파일 통과 (tests/KspBridge.Mod.Modules.Debug.Tests/SaveFileCheckTests.cs) — C8
- [x] T008 사후 판정 시험: 거절 조건마다 거절, 활성 기체가 목록 처음·중간·끝에 있을 때와 목록이 파일보다 짧을 때 `pid`로 맞는 위치, `pid`가 목록에 없을 때 거절 (tests/KspBridge.Mod.Modules.Debug.Tests/LoadedGameCheckTests.cs) — C7·C8
- [x] T009 전환 판정 시험: 판정 순서, 이미 활성, 로드되지 않음, 미소유는 `force`와 상관없이 거절, `force` false에서 `CLEAR`가 아니면 거절하고 이유에 결과 이름, `force` true면 결과와 상관없이 진행 (tests/KspBridge.Mod.Modules.Debug.Tests/VesselSwitchCheckTests.cs) — C3·C4
- [x] T010 저장 판정 시험: 파괴 거절(결과가 `CLEAR`여도), 세 결과 거절, `NOT_IN_ATMOSPHERE`·`NOT_UNDER_ACCELERATION`·`CLEAR` 통과 (tests/KspBridge.Mod.Modules.Debug.Tests/QuickSaveCheckTests.cs) — C6

체크포인트: `dotnet test tests/KspBridge.Mod.Modules.Debug.Tests`가 통과한다.

## 단계 2: Mod 처리기

- [x] T011 처리기 `list_vessels`: 준비 판정(게임 있음, `Session.BlockReason()` 없음, 비행 장면, 활성 기체 있음, 아니면 장면 이름을 넣은 `not_ready`). `FlightGlobals.Vessels`마다 `id`(`persistentId`)·`name`·`type`·`situation`·`body`·`loaded`·`owned`·`active`·`distance_m`(`GetWorldPos3D()` 사이 거리) (src/KspBridge.Mod.Modules.Debug/DebugModule.cs) — C1
- [x] T012 처리기 `switch_vessel`: 같은 준비 판정, `id` 검사(0 이상 2^32−1 이하 정수, 아니면 `bad_request`), 대상 찾기, `force`가 false일 때만 `ClearToSave(logMsg: false)`, `VesselSwitchCheck` 판정, 거절 이유에 `GetNotClearToSaveStatusReason` 문구. `Proceed`면 `SetActiveVessel` 또는 `ForceSetActiveVessel`, false 반환이나 호출 뒤 활성 기체가 대상이 아니면 `bad_request`. 응답 `{id, name, already}` (src/KspBridge.Mod.Modules.Debug/DebugModule.cs) — C2·C3·C4
- [x] T013 처리기 `save`: 같은 준비 판정, `SaveName.Parse(name, forSave: true)`, `QuickSaveCheck`(`ActiveVessel.state == DEAD`, `ClearToSave(false)`). 통과하면 `onGameAboutToQuicksave` 발생 → `CurrentGame.Updated()`의 시작 장면 `FLIGHT` → `SaveGame(game, 이름, SaveFolder, OVERWRITE)` → 시작 장면 `SPACECENTER`. `persistent`는 쓰지 않는다. `SaveGame` 예외는 잡아 `bad_request`(게임이 저장하지 못했다와 예외 메시지). 응답 `{name}` (src/KspBridge.Mod.Modules.Debug/DebugModule.cs) — C5·C6
- [x] T014 처리기 `load`:
  - 준비 판정: 게임 있음, `BlockReason()` 없음, 장면이 `SPACECENTER`·`FLIGHT`·`EDITOR`·`TRACKSTATION`, 아니면 `not_ready`.
  - 이름 검사: `SaveName.Parse(name, forSave: false)`.
  - 파일 읽기: 파일 경로 `saves/<SaveFolder>/<이름>.sfs`가 없으면 `bad_request`. `ConfigNode.Load`를 `try/catch`로 부르고, 예외나 `null`이면 손상 `bad_request`.
  - 사전 판정: `SaveFileFacts`를 모아 `SaveFileCheck`.
  - 불러오기: `ROCManager.Instance?.ClearROCsCache()` → `LoadGameCfg(node, 이름, true, true)`(`try/catch`) → `LoadedGameFacts`로 사후 판정. 예외나 거절이면 id 사전이 지워졌다는 이유와 복구 안내를 넣은 `bad_request`이고, 장면은 바꾸지 않는다.
  - 실행: 통과하면 `UpdateScenarioModules` → `onGameStatePostLoad.Fire(node)` → `StartAndFocusVessel(game, 위치)`.
  - 응답: `{accepted: true, name}`.

  (src/KspBridge.Mod.Modules.Debug/DebugModule.cs) — C7·C8
- [x] T015 처리기 등록: `Register`에 `list_vessels`·`switch_vessel`·`save`·`load`를 `context.AddHandler`로 더한다 (src/KspBridge.Mod.Modules.Debug/DebugModule.cs) — C1·C2·C5·C7·C10

체크포인트: `dotnet build src/KspBridge.Mod.Modules.Debug`가 경고 없이 통과한다.

## 단계 3: MCP 도구와 문서

- [x] T016 [P] 도구 네 개를 더한다. 각 도구의 `Description`에 인자·응답·오류 코드를 적는다 (src/KspBridge.Server.Modules.Debug/DebugTools.cs) — C1·C2·C5·C7·C10
  - `debug_list_vessels`(`ReadOnly`·`Idempotent`)
  - `debug_switch_vessel`(`Destructive`, `id`, `force` 기본 false)
  - `debug_save`(`Destructive`, `name` 생략 가능)
  - `debug_load`(`Destructive`, `name` 생략 가능. 설명에 `get_status`로 준비 확인을 적는다)
- [x] T017 [P] 도구 표의 debug 모듈 줄 아래에 새 도구 네 개를 한 줄씩 더한다 (ReadMe.md) — C10

체크포인트: `dotnet build KspBridge.slnx`가 통과한다.

## 단계 4: 기준 spec 반영(마지막 구현 단계)

- [x] T018 `debug-tools` 기준 spec을 고친다. `server-hosting` 등 `ReadMe.md`가 속한 다섯 기준 spec은 바꾸지 않는다(D021) (docs/features/debug-tools.md) — C11
  - 요구 R5~R10 추가: R5 목록=C1, R6 전환=C2, R7 전환 오류·`force`=C3·C4, R8 저장=C5·C6, R9 불러오기=C7, R10 불러오기 오류=C8, C9의 실제 확인 기준은 R8·R9에 포함
  - 오류를 다루는 R7·R8·R10 끝에 "게임에 예외가 남거나 도구가 멈추지 않는다"를 넣는다(C10). 코어 비수정과 핫리로드는 작업 조건이라 R에 넣지 않는다
  - 이력 한 줄(2026-11-21, 디버그-보강, R5~R10 추가, docs/specs/디버그-보강/)
  - `파일:` 줄에 새 소스 5개·시험 5개·`ReadMe.md` 추가
  - 머리말과 기존 R의 결정 참조를 폴더 이름을 붙인 표기(`관측 D004` 등)로, 새 R은 `디버그-보강 D<n>`으로

체크포인트: `docs/features/debug-tools.md`를 기준점 diff로 읽어 R5~R10·이력·`파일:` 줄·결정 참조 표기를 확인한다.

## 단계 5: 검증(실제 게임, 실제 `mcp__kspbridge__*` 호출만)

- [x] T019 `dotnet build KspBridge.slnx`, `dotnet test KspBridge.slnx`, 기준점부터 `git diff --stat`으로 `src/KspBridge.Mod.Core`·`src/KspBridge.Server.Core`가 바뀌지 않았는지 확인 — C10
- [x] T020 서버 모듈과 Mod 세트를 게시해 핫리로드한다. 실행 중인 Claude Code에서 새 도구 네 개가 `mcp__kspbridge__debug_*`로 보이고 불리는지 확인한다. 장면을 바꾸기 전에 `get_status`로 다른 비행이 없는지 본다 — C10
- [x] T021 두 기체를 준비한다. `debug_launch`(`Kerbal X`, 발사대) → `debug_space_center` → `debug_launch`(`Aeris 3A`, SPH 활주로). 그다음 `debug_list_vessels`로 필드 구성, 두 기체의 `loaded`·`owned`, 활성 하나, 거리(약 1.7 km)를 확인한다. 로드되지 않았으면 멈추고 보고한다. 확인 뒤 `debug_save`(`two`)로 두 기체 상태를 저장한다(D022). 그다음 `debug_space_center`로 나가 우주센터에서 부르면 장면 이름을 넣은 `not_ready`인지 확인하고, `debug_load`(`two`)로 되돌린다 — C1
- [x] T022 전환을 확인한다 — C2·C3
  - 계획 없이 `debug_switch_vessel`로 전환한 뒤 `get_status` 활성 기체와 1초 안 `ready=true`
  - 같은 id로 다시 부르면 `already=true`
  - 없는 id·음수·소수 id에 `bad_request`
  - 위 id 오류 뒤마다 활성 기체 그대로, 1초 안 `ready=true`
  - 우주센터에서 `not_ready`: `debug_space_center`로 나가 확인한 뒤 `debug_load`(`two`)로 되돌린다
  - 로드되지 않은 기체: `debug_set_orbit`으로 활성 기체를 궤도로 옮긴 뒤 지상 기체 id에 `bad_request` → 활성 기체 그대로·1초 안 `ready=true` 확인 → `debug_load`(`two`)로 되돌린다
- [x] T023 계획 실행 중 전환을 확인한다 — C4
  - `two` 상태(두 기체 지상 정지)에서 시작한다
  - `submit_wait`(60초) 실행 중 `debug_switch_vessel`(`force: true`) → `get_plan` 끝난 사유 `vessel_change`
  - 같은 절차를 `force: false`로(두 기체가 지상에 멈춰 저장 가능 검사를 통과)
  - 계획 없이 `force: true` 전환
- [x] T024 저장·불러오기 순서를 확인한다 — C5·C9
  - 궤도에 올린 기체(`debug_set_orbit`)에서 `debug_save`(`t1`)를 하고, 저장 전후로 `persistent.sfs` 수정 시각을 비교한다.
  - 궤도를 바꾼 뒤 `debug_load`(`t1`) → `get_flight` Ap·Pe가 저장 때 값과 각 1 km 안
  - 다른 궤도에서 `debug_save`(`t2`)를 하고, `t1`·`t2`를 차례로 불러 각 값 확인
  - 이름을 생략한 저장이 `quicksave.sfs`를 쓰는지
  - 계획 실행 중 저장 뒤 계획이 `running`으로 남는지
- [x] T025 저장 오류를 확인한다. 각 오류 뒤 파일이 생기거나 바뀌지 않았는지(목록·수정 시각) 본다. 처리기는 장면 판정을 이름 검사보다 먼저 하므로 다음 순서로 한다 — C6
  - 비행 장면에서 이름 `persistent`·`../x`·65자·빈 문자열에 `bad_request`
  - 그다음 `debug_space_center`로 나가 `not_ready`(이 우주센터 상태에서 T026을 시작한다)
- [x] T026 불러오기 장면을 확인한다. `t1`을 우주센터·에디터(`editor_enter`)·비행 장면에서 각각 불러, `get_status`가 비행 장면과 저장 때 활성 기체를 보이는지 본다. 추적소는 갈 MCP 수단이 없어 T014 장면 조건 목록(코드)으로 확인했다고 보고한다(D023). 비행 장면에서 계획 실행 중 불러온 뒤 `get_plan` 끝난 사유가 `new_flight`인지 본다 — C7
- [x] T027 불러오기 오류를 확인한다 — C8
  - 없는 이름에 `bad_request`
  - 장면을 불러오는 중 `not_ready`: `debug_load`(`t1`) 직후 바로 `debug_load`를 한 번 더 불러 확인한다(D023)
  - 받지 않는 장면(메인 메뉴 등) `not_ready`: 갈 MCP 수단이 없어 T014 장면 조건(코드)으로 확인했다고 보고한다(D023)
  - 세이브 폴더에 만든 시험 파일마다 `bad_request`이고 장면·계획·`persistent.sfs` 수정 시각이 그대로인지 본다. 확인 뒤 지운다. `noroster`는 사후 판정 경로라 기체 id 사전이 지워지므로 맨 마지막에 하고, 그 뒤 `debug_load`(`t1`)로 비행을 다시 시작한다.
    - `broken.sfs`: 내용이 깨진 파일
    - `oldver.sfs`: `version`을 `1.11.0`으로 바꾼 것
    - `notflight.sfs`: `scene`을 `5`로 바꾼 것
    - `noroster.sfs`: `ROSTER`를 지운 것
- [x] T028 마무리 확인 — C10·C11
  - `ReadMe.md` 도구 표와 `docs/features/debug-tools.md`를 diff로 확인
  - 확인을 마친 뒤 `get_status`가 정상이고 도구 호출이 멈추지 않았는지

체크포인트: 완료 조건 C1~C11마다 실행한 호출과 결과를 보고에 적는다. 실제 게임에서 만들 수 없는 경로(저장 가능 검사 실패 상태, 미소유 기체, 파괴된 기체)는 단위 시험으로, 갈 수 없는 장면(추적소, 메인 메뉴)은 코드로 확인했다고 밝힌다.

## 의존 관계

- 단계 1의 T001~T005는 서로 다른 파일이라 동시에 할 수 있다. T006~T010은 각자 대응하는 판정(T001~T005)이 끝난 뒤에 한다.
- 단계 2(T011~T015)는 같은 파일(`DebugModule.cs`)이라 차례로 한다. T012는 T004, T013은 T001·T005, T014는 T001·T002·T003에 의존한다.
- 단계 3의 T016은 Mod 처리기 이름·인자(plan 인터페이스)만 따르므로 단계 2와 동시에 할 수 있다. T017은 다른 파일이라 언제든 할 수 있다.
- 단계 4(T018)는 구현 단계의 마지막이고 단계 1~3 뒤에 한다. 4절 검증 전에 해야 구현 후 검토 diff에 들어간다.
- 단계 5는 단계 1~4가 끝난 뒤에 한다. T020(게시·핫리로드)이 T021~T028보다 먼저다. T021에서 저장한 `two`(두 기체 상태)를 T022·T023이 쓰며, 상태를 깨는 확인 뒤에는 `debug_load`(`two`)로 되돌린다(D022). T024의 `t1`을 T026·T027이 쓴다.
