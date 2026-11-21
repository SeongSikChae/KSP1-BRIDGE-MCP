# Debug 보강: 활성 기체 전환과 저장·불러오기 계획

## 요약

기존 debug 모듈 쌍에 새 도구 네 개를 더한다. Mod 모듈 `DebugModule`에 처리기 네 개(`list_vessels`, `switch_vessel`, `save`, `load`)를, 서버 모듈 `DebugTools`에 MCP 도구 네 개(`debug_list_vessels`, `debug_switch_vessel`, `debug_save`, `debug_load`)를 둔다. 코어는 고치지 않는다.

판정 로직은 게임 API를 부르지 않는 순수 클래스 다섯 개로 떼어 단위 시험한다. 게임 API를 부르는 부분은 `DebugModule`에 둔다.
- `SaveName`: 저장 이름 규칙
- `SaveFileCheck`: 불러올 파일의 사전 판정(파일 값)
- `LoadedGameCheck`: 불러오기의 사후 판정(게임이 만든 결과)
- `VesselSwitchCheck`: 전환 판정
- `QuickSaveCheck`: 저장 판정

게임 동작은 KSP 1.12.5 `Assembly-CSharp` 디컴파일로 확인했다.
- 전환: `FlightGlobals.SetActiveVessel`·`ForceSetActiveVessel`
- 저장 가능 검사: `FlightGlobals.ClearToSave`
- 퀵세이브: `QuickSaveLoad.QuickSaveClearToSave`·`doSave`
- 퀵로드: `QuickSaveLoad.quickLoad`·`onQuickloadPipelineFinished`
- 저장 파일 다루기: `GamePersistence.SaveGame`·`LoadSFSFile`·`LoadGameCfg`
- 비행 시작: `FlightDriver.StartAndFocusVessel`·`PostInit`
- 저장 파일 읽기: `Game(ConfigNode)`
- 업그레이드 처리: `KSPUpgradePipeline.Process`

## 바꿀 파일·구조

- `src/KspBridge.Mod.Modules.Debug/DebugModule.cs`: 처리기 `list_vessels`·`switch_vessel`·`save`·`load`를 등록하고 구현한다. 게임 API 호출, 판정 입력 모으기, 응답 만들기를 맡는다.
- `src/KspBridge.Mod.Modules.Debug/SaveName.cs`(새 파일): 저장 이름 검사(글자 규칙, 64자, `persistent` 예약, 생략 시 `quicksave`). 순수 클래스다.
- `src/KspBridge.Mod.Modules.Debug/SaveFileCheck.cs`(새 파일): 불러올 파일에서 모은 값(`SaveFileFacts`)과 지금 게임 버전으로 판정한다. 손상, 비행 중 저장 파일 아님, 버전 다름을 가리고 이유를 돌려준다. 순수 클래스다.
- `src/KspBridge.Mod.Modules.Debug/VesselSwitchCheck.cs`(새 파일): 대상 기체에서 모은 값(`VesselFacts`), `force`, 저장 가능 검사 결과 이름으로 전환 판정(이미 활성 / 진행 / 거절과 이유)을 한다. 순수 클래스다.
- `src/KspBridge.Mod.Modules.Debug/QuickSaveCheck.cs`(새 파일): 저장 판정. 활성 기체 파괴 여부와 저장 가능 검사 결과 이름으로 저장 가능 여부와 거절 이유를 돌려준다. 순수 클래스다.
- `src/KspBridge.Mod.Modules.Debug/LoadedGameCheck.cs`(새 파일): 게임이 저장 파일로 만든 결과(`LoadGameCfg` 반환값)에서 모은 값(`LoadedGameFacts`)을 사후 판정한다(D017). 순수 클래스다.
- `src/KspBridge.Server.Modules.Debug/DebugTools.cs`: MCP 도구 네 개를 더한다. 인자를 Mod 처리기로 넘기고 결과를 그대로 돌려준다.
- `tests/KspBridge.Mod.Modules.Debug.Tests/SaveNameTests.cs`(새 파일): 이름 규칙 단위 시험.
- `tests/KspBridge.Mod.Modules.Debug.Tests/SaveFileCheckTests.cs`(새 파일): 파일 판정 단위 시험.
- `tests/KspBridge.Mod.Modules.Debug.Tests/VesselSwitchCheckTests.cs`(새 파일): 전환 판정 단위 시험.
- `tests/KspBridge.Mod.Modules.Debug.Tests/QuickSaveCheckTests.cs`(새 파일): 저장 판정(파괴 거절, 퀵세이브 검사) 단위 시험.
- `tests/KspBridge.Mod.Modules.Debug.Tests/LoadedGameCheckTests.cs`(새 파일): 불러온 게임 결과 사후 판정 단위 시험.
- `ReadMe.md`: 도구 표(debug 모듈 줄 근처)에 `debug_list_vessels`·`debug_switch_vessel`·`debug_save`·`debug_load`를 한 줄씩 더한다(D020). 이 파일은 여러 기능이 함께 쓰는 문서로, 기준 spec `flight-plan`·`server-hosting`·`bridge-connection`·`deployment`·`editor`의 `파일:` 줄에 있다. 이번 변경은 도구 표에 debug 도구 줄을 더하는 것뿐이라 다섯 기능의 요구는 어느 것도 바뀌지 않는다. `deployment` R9는 게시 방법 등의 설명을 정하는 요구라 도구 표와 무관하다. 그래서 다섯 기준 spec에는 반영하지 않고, 이번 기능의 기준 spec `debug-tools`의 `파일:` 줄에 `ReadMe.md`를 더한다(D021).
- `docs/features/debug-tools.md`: 다음을 반영한다.
  - 요구 R5~R10을 더한다.
  - 이력에 이번 작업 한 줄을 더한다.
  - `파일:` 줄에 새 소스·시험 파일과 `ReadMe.md`를 더한다(D021).
  - 머리말과 기존 결정 참조를 변경 폴더 이름을 붙인 표기(`관측 D004` 등)로 바꾼다(spec C11, D010).

## 결정

- 도구는 기존 debug 모듈 쌍에 더한다. 기능이 debug(검증용 도구)이고 기준 spec도 하나이기 때문이다. / 검토한 대안: 새 모듈 쌍. 기능이 갈리지 않고 모듈 수만 늘어 고르지 않았다.
- 판정 로직은 게임 API를 부르지 않는 순수 클래스로 떼고, 판정에 필요한 게임 값은 `DebugModule`이 모아 넘긴다. 기존 `OrbitRequest`·`PendingOrbit`와 같은 방식이라, 시험 프로젝트가 KSP·Unity API를 부르지 않고 단위 시험할 수 있다. / 검토한 대안: 시험에서 `ConfigNode`를 직접 만듦. KSP 클래스를 시험 중에 실행하게 되어 Unity 없는 환경에서 깨질 수 있어 고르지 않았다.
- 저장 가능 검사는 게임 `FlightGlobals.ClearToSave(logMsg: false)`의 결과(열거형 이름)를 그대로 판정에 넘긴다(D014). 거절 이유에는 결과 이름과 게임 문구 `FlightGlobals.GetNotClearToSaveStatusReason`를 함께 넣는다.
- 전환은 판정이 "진행"일 때만 게임 함수를 부른다. `force`가 false면 `SetActiveVessel`, true면 `ForceSetActiveVessel`이다. 게임 함수가 false를 돌려주거나 호출 뒤 활성 기체가 대상이 아니면 `bad_request`이다(C3 "그 밖의 이유"). 미소유 판정(D006)은 `force`와 상관없이 Mod가 먼저 한다. 게임 강제 전환이 소유 검사를 건너뛰기 때문이다.
- 저장 판정은 `QuickSaveCheck`가 한다. 게임 F5(`QuickSaveLoad.quickSave`)처럼 활성 기체가 파괴(`Vessel.State.DEAD`)됐으면 먼저 거절한다. `ClearToSave`는 파괴된 기체에 `CLEAR`를 돌려주므로 결과 이름만으로는 가릴 수 없다. 그다음 퀵세이브 검사 세 결과를 거절한다.
- 저장 실행은 게임 `doSave`의 순서를 따른다. `GameEvents.onGameAboutToQuicksave`를 발생시키고, `HighLogic.CurrentGame.Updated()`의 시작 장면을 `FLIGHT`로 둔 채 `GamePersistence.SaveGame(game, 이름, HighLogic.SaveFolder, SaveMode.OVERWRITE)`으로 저장한 뒤 시작 장면을 `SPACECENTER`로 되돌린다.
  - `doSave`의 `persistent` 쓰기 줄은 뺀다(D007).
  - 게임 F5가 쓰는 `BACKUP` 모드는 쓰지 않는다. `BACKUP`은 설정 `SAVE_BACKUPS`에 따라 게임 백업 폴더를 통째로 지우거나 오래된 백업을 지워 지정한 파일 밖을 건드린다. `OVERWRITE`는 지정한 저장 파일과, 게임이 그 파일과 함께 쓰는 메타 파일(`<이름>.loadmeta`)만 쓴다. 이 메타 파일은 같은 저장의 일부로 본다.
  - 난이도 설정 `CanQuickSave`는 보지 않는다(D008).
- 불러오기 사전 판정은 `ConfigNode`에서 직접 값을 읽어 `SaveFileCheck`로 한다. 사전 판정에는 게임 `LoadGameCfg`와 `new Game(node)`를 쓰지 않는다. `LoadGameCfg`는 판정 전에 지금 비행의 기체 id 사전을 지우고(`FlightGlobals.ClearpersistentIdDictionaries`), `Game` 생성자는 비행 상태 객체를 만들며 형식이 틀리면 `int.Parse` 예외를 낸다(D009·D015).
- 불러오기는 두 단계로 판정한다(D017).
  - 사전 판정(`SaveFileCheck`): 파일 값으로 한다. 기체 id 사전을 지우기 전에 걸러 낼 수 있는 것을 거른다.
  - 사후 판정(`LoadedGameCheck`): 게임이 실제로 만든 결과로 한다. 게임이 파일에서 무엇을 파싱하는지 짐작해 판정 항목을 늘리지 않고, 결과가 불러올 수 있는 상태인지 본다.
- 불러오기 실행은 사전 판정을 통과한 뒤 게임 퀵로드의 성공 경로를 따른다.
  1. `ROCManager.Instance?.ClearROCsCache()`
  2. `GamePersistence.LoadGameCfg(node, 이름, true, true)`를 `try/catch`로 부름
  3. 사후 판정(`LoadedGameCheck`): `game`이 `null`이 아님, `game.compatible`, `game.flightState`가 `null`이 아님, `flightState.compatible`, `flightState.protoVessels`에 저장 때 활성 기체의 `pid`와 같은 `vesselID`가 있음. 통과하면 그 위치를 얻는다. 게임 퀵로드의 `game.flightState != null`·`game.compatible` 확인보다 넓게 본다.
  4. `GamePersistence.UpdateScenarioModules(game)`
  5. `GameEvents.onGameStatePostLoad.Fire(node)`
  6. `FlightDriver.StartAndFocusVessel(game, 3에서 얻은 위치)`

  2·3에서 예외가 나거나 어긋나면 4 이후를 실행하지 않는다. 업그레이드 처리(`KSPUpgradePipeline`)는 부르지 않는다(D015). 판정을 통과한 파일은 지금 버전 파일이라 업그레이드할 것이 없다.
- 기체 목록의 거리는 활성 기체와 각 기체의 `GetWorldPos3D()` 사이 거리(m)다. 로드되지 않은 기체도 궤도로 위치를 낸다.

## 인터페이스·데이터

MCP 도구(서버 모듈 `DebugTools`, Mod 처리기는 `debug.<이름>`):

| 도구 | 인자 | Mod 처리기 | 응답 |
|---|---|---|---|
| `debug_list_vessels` | 없음 | `list_vessels` | `vessels`: 배열, 원소 `{id, name, type, situation, body, loaded, owned, active, distance_m}` |
| `debug_switch_vessel` | `id`(정수, 필수), `force`(bool, 기본 false) | `switch_vessel` | `{id, name, already}`(`already`는 이미 활성 기체였으면 true) |
| `debug_save` | `name`(문자열, 생략하면 `quicksave`) | `save` | `{name}` |
| `debug_load` | `name`(문자열, 생략하면 `quicksave`) | `load` | `{accepted: true, name}`(그 뒤 호출하는 쪽이 `get_status`로 준비 확인) |

- `id`는 기체 `persistentId`(32비트 부호 없는 정수)다. JSON 수로 받아 0 이상 2^32−1 이하의 정수가 아니면 `bad_request`이다.
- `type`·`situation`은 게임 열거형 이름(`VesselType`, `Vessel.Situations`)이다. `owned`는 `DiscoveryInfo.Level == DiscoveryLevels.Owned`이다.

순수 클래스:

- `SaveName.Parse(string raw, bool forSave)`는 이름을 돌려주거나 `bad_request`를 던진다.
  - `null`이면 `quicksave`이다.
  - 빈 문자열, 허용 글자(영문자·숫자·`-`·`_`·공백) 밖의 글자, 64자 초과이면 `bad_request`이다.
  - `forSave`이면 `persistent`(대소문자 무시)도 `bad_request`이다. 불러오기는 C8대로 글자 규칙만 본다.
- `SaveFileFacts`: 파일에서 모은 값이다. `DebugModule`이 `ConfigNode`에서 읽어 채운다.
  - `HasGame`: `GAME` 노드 있음
  - `Version`: `GAME/version` 문자열
  - `Scene`: `GAME/scene` 문자열
  - `HasFlightState`: `GAME/FLIGHTSTATE` 있음
  - `VesselCount`: `FLIGHTSTATE` 아래 `VESSEL` 수
  - `ActiveVessel`: `FLIGHTSTATE/activeVessel` 문자열
  - `ActivePid`: `activeVessel`이 가리키는 `VESSEL` 노드의 `pid` 문자열(그 노드가 없으면 `null`)
- `SaveFileCheck.Check(SaveFileFacts facts, int major, int minor, int revision)`는 통과면 `null`, 거절이면 이유 문자열을 돌려준다.
  - 손상: `GAME` 없음, `version`이 정수 세 개를 점으로 이은 형식이 아님, `scene`이 정수가 아님, `FLIGHTSTATE`가 있는데 `activeVessel`이 없거나 정수가 아님.
  - 비행 중 저장 파일 아님: `scene`이 `FLIGHT`(7)가 아님, `FLIGHTSTATE` 없음, `VESSEL` 0개, `activeVessel`이 0 이상 `VESSEL` 수 미만이 아님.
  - 활성 기체 손상: `activeVessel`이 가리키는 `VESSEL`의 `pid`가 없거나 GUID 형식이 아님(D018). 비행 아님 판정 뒤에 하므로 그 노드는 반드시 있고, `ActivePid`가 `null`이면 노드에 `pid`가 없다는 뜻이다(D019). 거절 이유는 손상으로 알린다.
  - 버전 다름: `version`의 주.부.수정이 지금 게임과 다름.
  - 판정 순서는 손상 → 비행 중 저장 파일 아님 → 활성 기체 손상 → 버전이다(D019).
  - 사전 판정은 위 값만 본다. 그 밖의 값이 깨져 생기는 실패는 사후 판정과 예외 처리가 맡는다(D016·D017, 오류·위험).
- `LoadedGameFacts`: `LoadGameCfg` 결과에서 모은 값이다. `DebugModule`이 채운다.
  - `GameNull`, `GameCompatible`, `FlightStateNull`, `FlightStateCompatible`, `ProtoVesselIds`(`flightState.protoVessels` 순서대로의 `vesselID` 목록)
- `LoadedGameCheck.Check(LoadedGameFacts facts, Guid activePid)`는 통과면 저장 때 활성 기체의 위치(0부터), 아니면 거절 이유를 돌려준다(D018).
  - 거절 조건: 게임 없음, 게임 호환 아님, 비행 상태 없음, 비행 상태 호환 아님, `ProtoVesselIds`에 `activePid`가 없음.
  - 활성 기체는 게임의 `activeVesselIdx`가 아니라 사전 판정 때 읽어 둔 `pid`로 찾는다. 게임은 불러오는 도중 승무원 값이 없는 EVA 기체를 정리하며(`CompatibilityUtilities.CleanUpUnsanitaryEVAKerbals`) 기체 목록과 `activeVessel`을 바꾼다. 정리 뒤 `activeVesselIdx`가 저장 때 활성 기체를 가리키는지는 디컴파일로 확정할 수 없다(난독화된 분기). 게임은 이 번호를 범위 안으로 끌어와(`FlightDriver`의 `Mathf.Clamp`) 불러올 뿐이다. 식별자로 찾으면 정리 방식과 상관없이 spec C7(저장 때의 활성 기체로 연다)이 지켜진다. 기체 수도 파일과 견주지 않는다.
- `VesselFacts`: 대상 기체 값이다.
  - `Found`, `Active`, `Loaded`, `Owned`, `Name`
- `VesselSwitchCheck.Decide(VesselFacts target, bool force, string clearToSaveResult)`는 `Already`, `Proceed`, `Reject(이유)` 가운데 하나를 돌려준다.
  - 순서: 없음 → 이미 활성 → 로드되지 않음 → 미소유 → (`force`가 false일 때만) 저장 가능 검사 결과가 `CLEAR`가 아님.
  - `clearToSaveResult`는 `force`가 false일 때만 의미가 있다.
- `QuickSaveCheck.Decide(bool activeDead, string clearToSaveResult)`는 저장할 수 있으면 `null`, 아니면 거절 이유를 돌려준다. `activeDead`이면 파괴 이유를 돌려준다. 그 밖에는 결과가 `NOT_WHILE_ON_A_LADDER`·`NOT_WHILE_MOVING_OVER_SURFACE`·`NOT_WHILE_ABOUT_TO_CRASH`이면 결과 이름을 담은 이유를 돌려준다(D013·D014).

장면·준비 조건(기존 `RequireReadyGame`과 `context.Session` 사용):

- `list_vessels`·`switch_vessel`·`save`: 게임이 있고, `Session.BlockReason()`이 `null`이고, 비행 장면이며, 활성 기체가 있어야 한다. 아니면 `not_ready`이고 이유에 지금 장면 이름을 넣는다.
- `load`: 게임이 있고, `BlockReason()`이 `null`이고, 장면이 `SPACECENTER`·`FLIGHT`·`EDITOR`·`TRACKSTATION` 가운데 하나여야 한다. 아니면 `not_ready`이다.

## 오류·위험

- 전환 직후 한 물리 프레임 동안 `FlightGlobals.ready`가 false다. 게임 `setActiveVessel`이 거절할 때도 먼저 false로 둔다. 그동안 `get_status`가 `ready=false`를 보일 수 있다. 다음 `FixedUpdate`에서 게임이 되돌리므로 C2·C3의 1초 조건은 실제 게임에서 확인한다. Mod 판정에서 거절할 경우는 게임 함수를 부르지 않으므로 `ready`가 바뀌지 않는다.
- 전환 중 계획 종료: 게임이 `onVesselChange`를 같은 호출 안에서 발생시키므로, 코어 `FlightSession.OnVesselChange`가 계획을 `vessel_change`로 끝낸다. 처리기는 그 뒤에 응답한다.
- 사전 판정을 통과한 뒤의 실패(D016·D017): 사전 판정이 보지 않는 값이 깨진 파일은 `LoadGameCfg`에서 실패할 수 있다.
  - 예외를 내는 경우: `Game`·`FlightState`·`ProtoVessel`이 `GAME`·`FLIGHTSTATE`·기체·부품 노드의 여러 값을 그대로 `int.Parse` 등으로 읽는다.
  - 예외 없이 결과가 이상한 경우: `ROSTER`가 없으면 `flightState`가 `null`이다. `FLIGHTSTATE/version`이 0.18.0보다 낮으면 기체가 0개다.

  게임과 이 도구가 만드는 파일에서는 생기지 않는다.
  - 예외는 `try/catch`로 잡는다. 이상한 결과는 사후 판정으로 거른다. 둘 다 장면을 바꾸기 전(`UpdateScenarioModules` 앞)에 한다.
  - `LoadGameCfg`가 이미 기체 id 사전을 지운 뒤이므로 `bad_request`의 이유에 "지금 비행의 기체 id 사전이 지워졌으니 `debug_revert`나 다른 저장 파일로 비행을 다시 시작하라"를 넣는다. 장면과 계획은 바꾸지 않는다.
  - 판정용으로 `new Game(node)`를 미리 실행하는 방법은 쓰지 않는다. `ProtoVessel` 생성자가 기체 id 사전에 id를 등록해, 정상 불러오기에도 늘 부수 효과가 남기 때문이다.
- 손상 파일: `ConfigNode.Load`가 예외를 내거나 `null`을 돌려주면 손상으로 본다. 처리기 안에서 잡아 `bad_request`로 바꾸므로 게임에 예외가 남지 않는다.
- 저장 쓰기 실패: `GamePersistence.SaveGame`은 디렉터리 만들기와 파일 쓰기 실패를 예외로 낸다(`OVERWRITE` 모드에 `persistent`가 아닌 이름이면 빈 문자열을 돌려주는 경로는 없다). 호출을 `try/catch`로 감싸, 예외면 `bad_request`("게임이 저장하지 못했다"와 예외 메시지)를 돌려준다. 쓰다가 실패한 경우 파일 상태는 보장하지 않는다. spec C6의 "오류면 파일이 만들어지거나 바뀌지 않는다"는 쓰기 전에 하는 판정 오류(장면·이름·파괴·퀵세이브 검사)에 대한 것이다.
- 에디터·추적소에서 불러오기: `StartAndFocusVessel`은 장면과 상관없이 비행 장면을 불러오지만, 게임 퀵로드는 비행 장면에서만 부른다. 그래서 우주센터·에디터에서 실제로 확인하고, 추적소는 갈 MCP 수단이 없어 코드로 확인한다(C7, D023).
- 시험용 두 번째 기체: MCP만으로 단을 분리할 수 없다(단계 종류가 `wait`뿐). 그래서 두 번 발사해 로드된 두 기체를 만든다. 발사대에 기체를 올리고 `debug_space_center`로 저장한 뒤, 활주로에 다른 기체를 올린다. 발사대와 활주로는 약 1.7 km 떨어져 있어 지상 기체의 게임 로드 거리 안으로 예상한다. 첫 확인에서 `debug_list_vessels`의 `loaded`로 실제로 확인하고, 로드되지 않으면 멈추고 보고한다.
- 실제 게임에서 만들 수 없는 상태가 있다. 그래서 아래 경로는 단위 시험과 코드로만 확인하고 보고에 밝힌다.
  - 저장 가능 검사 실패 상태: 대기 안 비행, 가속 중, 사다리 등. 스로틀·조종 단계가 아직 없다.
  - 로드된 미소유 기체.

## 검증 전략

빌드와 단위 시험은 `dotnet build`·`dotnet test`로 확인한다. 실제 게임 확인은 다음 조건을 지킨다.
- 서버 모듈과 Mod 세트를 게시해 핫리로드한 뒤, 실행 중인 Claude Code에서 실제 `mcp__kspbridge__*` 호출로만 한다(MCP 검증 규칙).
- 장면을 바꾸기 전에 `get_status`로 다른 비행이 없는지 본다.
- 게임 화면은 보지 않고 MCP 응답만으로 판정한다.

- C1: 두 기체 상태(발사대 `Kerbal X`, 활주로 `Aeris 3A`)에서 `debug_list_vessels`를 부른다.
  - 필드 구성, 두 기체의 `loaded=true`·`owned=true`, 활성 기체 하나, 두 기체 사이 거리(약 1.7 km)를 확인한다.
  - 우주센터에서 부르면 `not_ready`이고 이유에 장면 이름이 들어가는지 확인한다.
- C2: 계획 없이 `debug_switch_vessel`(다른 기체 id)을 부른다.
  - 응답, `get_status`의 활성 기체, 1초 안 `ready=true`를 확인한다.
  - 같은 id로 다시 부르면 `already=true`인지 확인한다.
- C3: 실제로 확인할 것
  - 없는 id, 음수·소수 id에 `bad_request`
  - 우주센터에서 `not_ready`
  - 각 오류 뒤 활성 기체가 그대로인지와 `ready=true`
  - 로드되지 않은 기체에 `bad_request`: 궤도로 옮긴(`debug_set_orbit`) 기체에서 지상 기체 id로 확인한다.

  `VesselSwitchCheckTests`로 확인할 것
  - 판정 순서
  - 미소유는 `force`와 상관없이 거절
  - `force`가 false이고 결과가 `CLEAR`가 아니면 거절하며 이유에 결과 이름이 들어감
- C4: 실제 게임에서 `submit_wait`(60초)로 계획을 실행한 상태에서 `debug_switch_vessel`(`force: true`)을 부른다.
  - `get_plan`의 끝난 사유가 `vessel_change`인지 확인한다(계획 기준 spec R2의 실제 확인).
  - 같은 절차를 `force: false`로도 한다. 두 기체가 지상에 멈춰 있어 저장 가능 검사를 통과한다.
  - 계획 없이 `force: true` 전환에 오류가 없는지 확인한다.
  - 저장 가능 검사 실패 상태에서 `force`가 건너뛰는 것은 `VesselSwitchCheckTests`로 확인한다. 실제 게임에서는 만들 수 없다(오류·위험).
- C5·C9: 궤도에 올린 기체에서 `debug_save`(`t1`)를 부른다.
  - `persistent.sfs`의 수정 시각을 저장 전후로 비교해 바뀌지 않았는지 확인한다.
  - `debug_set_orbit`으로 궤도를 바꾸고 `debug_load`(`t1`)를 부른 뒤, 준비가 되면 `get_flight`의 Ap·Pe가 저장 때 값과 각 1 km 안인지 확인한다.
  - 다른 궤도에서 `debug_save`(`t2`)를 하고 `t1`, `t2`를 차례로 불러 각 값을 확인한다.
  - 이름을 생략한 저장이 `quicksave.sfs`를 쓰는지 응답 `name`과 파일로 확인한다.
  - 계획 실행 중 저장한 뒤 계획이 `running`으로 남는지 확인한다.
- C6: 실제로 확인할 것
  - 우주센터에서 `not_ready`
  - 이름 `persistent`·`../x`·65자·빈 문자열에 `bad_request`
  - 각 오류 뒤 파일이 생기거나 바뀌지 않았는지(파일 목록·수정 시각)

  `SaveNameTests`와 `QuickSaveCheckTests`로 확인할 것
  - 이름 규칙
  - 파괴된 기체 거절(실제 게임에서는 만들 수 없음)
  - 퀵세이브 검사 거절과 대기 안·가속 중 통과
- C7: 비행 중 저장한 `t1`을 우주센터·에디터·비행 장면에서 각각 `debug_load`로 불러, `get_status`가 비행 장면과 저장 때 활성 기체를 보이는지 확인한다.
  - 추적소는 갈 MCP 수단이 없어 실제로 확인하지 않는다(D023). 추적소를 받는 것은 처리기 장면 조건 목록(코드)으로 확인한다. 불러오기 실행은 `StartAndFocusVessel`이 장면과 상관없이 비행 장면을 불러오는 같은 경로이므로, 다른 세 장면의 실제 확인으로 갈음한다.
  - 비행 장면에서 계획 실행 중 불러온 뒤 `get_plan`의 끝난 사유가 `new_flight`인지 확인한다.
- C8: 실제로 확인할 것
  - 없는 이름에 `bad_request`
  - 세이브 폴더에 만든 시험 파일 세 개에 각각 `bad_request`. 시험 파일은 확인 뒤 지운다.
    - 내용이 깨진 `broken.sfs`
    - `t1`을 복사해 `version`을 `1.11.0`으로 바꾼 `oldver.sfs`
    - `scene`을 `5`로 바꾼 `notflight.sfs`
  - 각 오류 뒤 `get_status`의 장면·계획과 `persistent.sfs` 수정 시각이 그대로인지
  - 장면을 불러오는 중에 `not_ready`: `debug_launch`나 `debug_load` 직후 바로 `debug_load`를 불러 확인한다(D023).
  - 받지 않는 장면(메인 메뉴 등)에서 `not_ready`: 그 장면으로 갈 MCP 수단이 없어 처리기 장면 조건(코드)으로 확인한다(D023).

  `SaveFileCheckTests`로 확인할 것
  - 손상(값 없음·형식 틀림)
  - 비행 아님(`scene`·`FLIGHTSTATE`·`VESSEL`·`activeVessel`). `FLIGHTSTATE` 없음·`VESSEL` 0개·`activeVessel` 범위 밖이 손상이 아니라 비행 아님으로 나오는지 포함
  - 활성 기체 `pid` 없음·형식 틀림
  - 버전 다름의 판정과 순서

  `LoadedGameCheckTests`로 확인할 것
  - 사후 판정의 거절 조건마다 거절, 정상 결과 통과
  - 활성 기체가 목록 중간·끝에 있거나 목록이 파일보다 짧아도(정리된 경우) `pid`로 맞는 위치를 돌려줌

  실제 게임에서 추가로 확인할 것
  - `t1`을 복사해 `ROSTER` 노드를 지운 `noroster.sfs`에 `bad_request`이고 장면이 그대로인지(사후 판정 경로). 확인 뒤 지우고, 비행을 `debug_load`(`t1`)로 다시 시작한다.
- C10: 다음을 확인한다.
  - 기준점부터의 `git diff --stat`으로 코어(`src/KspBridge.Mod.Core`, `src/KspBridge.Server.Core`)가 바뀌지 않았는지 본다.
  - 게시와 핫리로드 뒤, 실행 중인 Claude Code에서 새 도구 네 개가 `mcp__kspbridge__debug_*`로 보이고 불리는지 본다.
  - 확인을 마친 뒤 `get_status`가 정상이고 도구 호출이 멈추지 않았는지 본다.
  - `ReadMe.md` 도구 표에 새 도구 네 개가 있는지 diff로 본다.
- C11: `docs/features/debug-tools.md`에서 다음을 diff로 읽어 확인한다.
  - R5~R10과 이력 줄이 있다.
  - `파일:` 줄에 새 파일이 있다.
  - 결정 참조에 폴더 이름이 붙어 있다.

## 원칙 점검

- 적용 규칙:
  - Bizadm `workflow/spec-driven.md`(기준 spec 반영은 tasks 마지막 구현 단계), `git/github-issue.md`(커밋에 #35 참조, 종결 조건), `workflow/repository-content.md`(저장소 문서에 개인 경로 없음, 저장소 기준 상대 경로)
  - 전역 `mcp-verification.md`(실제 `mcp__` 호출로만 검증), `language-preference.md`(코드·주석은 영어, 대화는 한국어)
  - 저장소 기준 spec `debug-tools`(이번에 R5~R10 추가), `flight-plan` R2(바꾸지 않고 실제 확인만), `deployment`(게시 기반 배포, 이번에 바꾸지 않음), `ReadMe.md`가 `파일:` 줄에 있는 `flight-plan`·`server-hosting`·`bridge-connection`·`deployment`·`editor`(도구 표 줄만 더해 요구가 바뀌지 않아 반영하지 않음, D021)
  - 작업 원칙:
    - 기능은 코어 밖 모듈로 더한다.
    - 게임 동작은 디컴파일이나 실측으로 확인한 뒤 설계한다.
    - 장면 전환 직후 실패는 재시도로 넘기지 않고 Mod 결함으로 고친다.
    - 입력 잠금으로 준비를 판정하지 않는다.
    - 요청 단위·세션 없는 설계로 한다.
    - 게임 화면 없이 MCP 응답으로 검증한다.
- 충돌: 없음.
  - 코어는 바꾸지 않는다(C10).
  - 준비 판정은 코어 `Session.BlockReason()`과 게임 상태만 보고 입력 잠금은 보지 않는다.
  - 새 도구는 모두 한 호출 안에서 끝나거나(`list`·`switch`·`save`) 장면 불러오기를 시작만 하고(`load`) 준비는 `get_status`로 본다. 세션 상태에 기대지 않는다.
  - 불러오기 사전 판정은 게임 함수 대신 파일 값을 직접 읽어 부수 효과 없이 한다. 디컴파일로 확인한 부수 효과는 기체 id 사전 초기화와 업그레이드 대화상자다(D015). 사전 판정을 통과한 뒤의 실패는 `LoadGameCfg`의 id 사전 초기화를 감수하고 사후 판정과 예외 처리로 잡는다. 장면을 바꾸기 전에 `bad_request`와 복구 방법을 알린다(D016·D017).
  - 실제 게임에서 만들 수 없는 상태(저장 가능 검사 실패, 미소유 기체, 파괴된 기체)는 단위 시험과 코드로 확인하고 보고에 밝힌다. MCP 검증 규칙은 MCP 도구 동작 판정에 관한 것이고, 이 경로들의 도구 동작은 실제 호출로 확인하는 다른 경로와 같은 처리기를 쓴다.
