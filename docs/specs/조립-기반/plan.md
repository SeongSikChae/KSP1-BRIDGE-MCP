# 조립 기반: 에디터 진입, 부품 목록 조회, craft 저장·불러오기 계획

## 요약

골격의 모듈 방식대로 `editor` 기능 쌍을 새로 만든다. Mod 쪽 `KspBridge.Mod.Modules.Editor`는 `IBridgeModule`(이름 `editor`)로 요청 처리기 8개를 등록하고, 서버 쪽 `KspBridge.Server.Modules.Editor`는 같은 이름의 MCP 도구 8개를 둔다. 코어·`debug` 모듈·배포 스크립트는 고치지 않는다. 배포 스크립트는 `KspBridge.Mod.Modules.*`·`KspBridge.Server.Modules.*` 폴더를 자동으로 찾으므로 새 프로젝트도 그대로 배포된다.

KSP 쪽은 공개 API만 쓴다(`Assembly-CSharp.dll` 메타데이터로 확인):

| 동작 | API | 비고 |
|---|---|---|
| 진입 | `EditorDriver.StartEditor(EditorFacility)` | public static, 장면 로드 |
| 시설 전환 | `EditorDriver.StartEditor(다른 시설)` | 장면 로드. `EditorDriver.SwitchEditor`는 protected static이라 쓸 수 없음(구현 중 확인) |
| 이탈 | `HighLogic.LoadScene(GameScenes.SPACECENTER)` | `EditorLogic.exitEditor`는 확인 대화상자를 띄우므로 쓰지 않음 |
| 불러오기 | `EditorLogic.LoadShipFromFile(string)` | public static. 대화상자 없이 `ShipConstruct.LoadShip` → `SpawnConstruct` |
| 새로 시작 | `EditorDriver.StartEditor(현재 시설)` | 대화상자 없는 공개 경로가 이것뿐(`OnNewShipConfirm`은 private, `NewShip`은 대화상자) |
| 저장 | `ShipConstruct.SaveShip()` → `ConfigNode.Save(path)` | 경로·덮어쓰기 판정을 직접 하기 위해 |
| 질량 | `ShipConstruct.GetShipMass(out dry, out fuel)` | |
| 부품 목록 | `PartLoader.LoadedPartsList`, `ResearchAndDevelopment.PartTechAvailable` | |

## 바꿀 파일·구조

- `src/KspBridge.Mod.Modules.Editor/KspBridge.Mod.Modules.Editor.csproj`: Debug 모듈 csproj와 같은 틀(net472, 세대 표식 이름, Core·Loader 참조). 에디터 이름 칸(`TMP_InputField`)이 `UnityEngine.UI` 타입을 상속하므로 `UnityEngine.UI` 참조를 더한다.
- `src/KspBridge.Mod.Modules.Editor/EditorModule.cs`: `IBridgeModule` 구현. 처리기 등록, 장면·준비 판정, 진입·이탈·전환·새로 시작.
- `src/KspBridge.Mod.Modules.Editor/PartCatalog.cs`: 부품 목록 조회(C3). `AvailablePart` → 응답 사전.
- `src/KspBridge.Mod.Modules.Editor/ShipView.cs`: 조립 중인 기체 조회(C4).
- `src/KspBridge.Mod.Modules.Editor/CraftFiles.cs`: craft 저장·불러오기·목록(C5, C6, C8). 경로 계산과 파일 탐색.
- `src/KspBridge.Mod.Modules.Editor/EditorQuery.cs`: KSP에 의존하지 않는 순수 로직. craft 이름 검사, 부품 필터(분류·검색)·페이지 나누기. 단위 테스트 대상.
- `src/KspBridge.Server.Modules.Editor/KspBridge.Server.Modules.Editor.csproj`: Debug 서버 모듈 csproj와 같은 틀.
- `src/KspBridge.Server.Modules.Editor/EditorTools.cs`: MCP 도구 8개. 인자를 JSON으로 묶어 `bridge.CallAsync("editor", op, ...)`로 넘기는 얇은 층.
- `tests/KspBridge.Mod.Modules.Editor.Tests/KspBridge.Mod.Modules.Editor.Tests.csproj`, `EditorQueryTests.cs`: Mod.Core.Tests와 같은 틀(net472, MSTest). `EditorQuery`만 시험한다.
- `KspBridge.slnx`: 위 세 프로젝트 추가.
- `ReadMe.md`: 도구 목록에 `editor_*` 추가.

## 결정

- **새 `editor` 모듈 쌍**: 사용자 결정. 검증 도구(`debug`)와 조립 기능을 섞지 않고, 다음 조립 작업(부착 조작)을 같은 모듈에 쌓는다(메모리 규칙: 기능별 별도 모듈).
- **진입은 우주센터에서만, 에디터 안에서는 시설 전환**: spec C1 범위. 비행 장면에서 바로 에디터로 가는 경로는 `debug_space_center`를 거치면 되고, 비행 저장 처리까지 맡으면 범위가 커진다.
  검토한 대안: 비행·추적소에서도 진입 허용. 진행 중 비행의 저장 여부를 이 모듈이 판단해야 해서 고르지 않았다.
- **이탈·전환·새로 시작·불러오기는 저장하지 않은 변경을 버린다**: 게임의 확인 대화상자를 띄우면 MCP로 응답할 수 없다(화면 조작 없이 검증 원칙). 도구 설명에 "먼저 `editor_save`"를 적는다.
  검토한 대안: 변경 여부를 추적해 `discard=true`가 없으면 거부. KSP에 공개된 "저장 안 됨" 표식이 없어 직접 추적해야 하고, 그 추적이 틀리면 오히려 혼란을 준다.
- **새로 시작 = `EditorDriver.StartEditor(현재 시설)`로 에디터 장면 다시 열기**: 대화상자 없는 공개 경로가 이것뿐이다. 장면 로드라서 진입과 같은 방식(`accepted` 후 `get_status` 확인)으로 다룬다.
  검토한 대안: private `OnNewShipConfirm`을 리플렉션으로 호출. KSP 내부 구현에 묶여 깨지기 쉽다. 루트 부품 삭제: 루트 삭제는 게임이 막는 경로가 있어 확실하지 않다.
- **에디터 준비 판정은 모듈 안에서**: 코어 `BlockReason()`(장면 로딩)을 먼저 보고, 이어서 `EditorLogic.fetch`와 `EditorLogic.fetch.ship`이 있는지, 요청한 시설과 `EditorDriver.editorFacility`가 같은지 본다. 입력 잠금(`InputLockManager`)은 판정에 넣지 않는다(메모리 규칙: 포커스 잠금은 MCP를 막지 않음). 코어를 고치지 않으므로 `get_status.ready`는 장면 로딩만 반영하고, 에디터 세부 준비는 각 도구가 `not_ready`로 알린다.
- **저장 경로를 직접 계산**: `<KSP>/saves/<저장>/Ships/<VAB|SPH>/<이름>.craft`. 덮어쓰기 검사(C5)를 저장 전에 하려면 경로를 먼저 알아야 한다. 저장할 때 `ship.shipName`과 에디터 이름 칸을 같은 이름으로 맞춘다. 썸네일은 만들지 않는다(범위 밖, 게임 목록에서 기본 그림으로 보임).
  검토한 대안: `ShipConstruction.SaveShip(ship, name)`. 내부 경로 규칙에 기대야 하고 덮어쓰기를 막을 수 없다.
- **craft 이름 검사**: 비어 있거나, 경로 구분자·`..`·파일 이름에 못 쓰는 문자가 있으면 `bad_request`. 조용히 바꾸지 않는다(Planner가 저장된 이름을 그대로 알 수 있게).
- **불러오기 출처 순서**: 저장 게임 `Ships/<시설>`을 먼저, 없으면 기본 제공 `<KSP>/Ships/<시설>`. `debug` 모듈의 `FindCraft`와 같은 순서지만, 코어·`debug`를 고치지 않으므로 `CraftFiles`에 따로 둔다(공통화는 #35에서 판단).
- **불러오기 실패는 미리 막고, 결과도 확인한다**(검토 지적 1): 게임의 불러오기(`ShipConstruct.LoadShip`)는 읽을 수 없는 파일이나 설치되지 않은 부품이 있으면 로그만 남기고 빈 에디터를 남긴다. 그래서 불러오기 전에 파일을 `ConfigNode.Load`로 읽어, 읽기 실패·`PART` 노드 0개·설치되지 않은 부품(`KSPUtil.GetPartName` → `PartLoader.getPartInfoByName`)이면 지금 기체를 건드리지 않고 `bad_request`를 돌려준다. 불러온 뒤에는 에디터 부품 수가 파일의 `PART` 수와 다르면 `bad_request`로 알린다(`LoadShipFromFile`은 같은 호출 안에서 동기로 끝난다).
- **부품 목록 기준**: `PartLoader.LoadedPartsList`에서 `category == none`(EVA 등 숨김)과 `TechHidden`을 빼고, R&D가 있는 모드(경력·과학)에서는 `PartTechAvailable`이 거짓인 부품을 뺀다. 구매 여부는 보지 않는다(spec 범위 밖).
- **부품 식별자는 `craftID`**: craft 파일에 저장되고 기체 안에서 유일해, 저장·불러오기 뒤에도 같은 부품을 가리킨다. 다음 작업(부착 조작)의 대상 지정에도 쓴다.

## 인터페이스·데이터

MCP 도구(Mod 처리기 `editor/<op>`와 1:1):

| 도구 | op | 인자 | 결과 요지 |
|---|---|---|---|
| `editor_enter` | `enter` | `facility`(VAB/SPH) | `{accepted, facility, already?}` |
| `editor_leave` | `leave` | 없음 | `{accepted}` |
| `editor_list_parts` | `parts` | `category?`, `search?`, `limit`(기본 50, 최대 200), `offset`(기본 0) | `{total, offset, parts:[...]}` |
| `editor_get_ship` | `ship` | 없음 | 아래 기체 형식 |
| `editor_save` | `save` | `name?`(없으면 현재 이름), `overwrite`(기본 false) | `{saved, name, path}` |
| `editor_load` | `load` | `name`, `facility?`(기본 현재 시설) | `{loaded, name, source, path, part_count}` |
| `editor_new` | `new` | 없음 | `{accepted}` |
| `editor_list_crafts` | `crafts` | `facility?`(기본 현재 시설, 에디터 밖이면 필수) | `{crafts:[{name, source, part_count}]}` |

부품 항목:

```json
{ "name": "liquidEngine2", "title": "LV-T45 \"Swivel\"", "category": "Engine",
  "sizes": ["size1"], "dry_mass_t": 1.5, "cost": 1200,
  "resources": [ { "name": "LiquidFuel", "max": 0 } ],
  "engine": { "thrust_vac_kn": 215, "thrust_asl_kn": 167.97, "isp_vac_s": 320, "isp_asl_s": 250,
              "propellants": [ { "name": "LiquidFuel", "ratio": 0.9 }, { "name": "Oxidizer", "ratio": 1.1 } ] },
  "modules": ["ModuleEnginesFX", "ModuleGimbal", "..."] }
```

- `sizes`: `AvailablePart.bulkheadProfiles`를 쉼표로 나눈 값.
- 엔진 해면 추력 = 진공 추력 × (해면 Isp / 진공 Isp). Isp는 `atmosphereCurve.Evaluate(0)`(진공), `Evaluate(1)`(해면).
- `search`는 `name`·`title`에 대소문자 무시 부분 일치. `category`는 `PartCategories` 이름과 대소문자 무시 일치.

기체:

```json
{ "name": "Test", "facility": "VAB", "part_count": 3, "mass_t": 5.2, "dry_mass_t": 1.9,
  "parts": [ { "id": 4294, "name": "mk1pod.v2", "title": "...", "parent": null, "attach": null,
               "parent_node": null, "node": null, "stage": 0, "symmetry": [] } ] }
```

- `attach`: `stack`(노드) 또는 `surface`. `parent_node`는 부모 쪽 노드 id, `node`는 이 부품 쪽 노드 id(표면 부착이면 `srfAttach`).
- `stage`: `inverseStage`. `symmetry`: 대칭 짝의 `id` 목록.

오류 코드: `not_ready`(게임 없음, 장면·에디터 준비, 다른 장면에서 호출. 모두 현재 장면을 함께 알림), `bad_request`(인자, 없는 craft, 읽을 수 없거나 설치되지 않은 부품이 있는 craft, 불러오기 결과 불일치, 빈 기체 저장, 덮어쓰기 거부, 이름 규칙).

## 오류·위험

- **시설 전환은 에디터 장면 다시 로드**: `StartEditor`가 `HighLogic.LoadScene`을 부르므로 코어 `FlightSession`이 로딩을 추적한다. 로드가 끝난 직후 에디터 초기화가 남아 있을 수 있어, 모듈의 준비 판정(요청 시설 = `EditorDriver.editorFacility`, `EditorLogic.fetch.ship` 있음)으로 한 번 더 막는다. 첫 KSP 시험에서 확인하고, 판정이 모자라면 구현 결함으로 고친다.
- **`LoadShipFromFile` 직후 기체가 바로 바뀌는지**: 내부에서 `StartEditor`와 `SpawnConstruct`를 동기로 부르지만, 부품 초기화 일부가 다음 프레임일 수 있다. 응답 직후 `editor_get_ship`으로 확인하고, 늦으면 준비 판정에 반영한다(로드 타이밍 오류는 Mod 결함으로 다룬다).
- **게임 대화상자**: 이 모듈은 대화상자를 띄우는 API(`exitEditor`, `NewShip`, `saveShip` 등)를 부르지 않는다.
- **연구 데이터가 없는 상태**: 샌드박스는 `ResearchAndDevelopment.Instance`가 없다. 이때는 연구 필터를 건너뛴다.
- **부품 수가 많음**: 기본 부품만 수백 개라 응답이 커진다. `limit` 기본 50, 최대 200으로 자르고 `total`을 준다.
- **다른 사람의 비행**: 진입·이탈은 장면을 바꾼다. 도구 설명과 검증 절차에서 `get_status`를 먼저 본다(메모리 규칙).

## 검증 전략

KSP 확인은 화면을 보지 않고 MCP 응답으로만 한다. 장면을 바꾸기 전에 `get_status`로 다른 비행이 진행 중인지 확인한다. 저장 시험 파일은 이름 앞에 `bridge-test-`를 붙이고 끝나면 지운다.

- C1: 우주센터에서 `editor_enter(VAB)` → `get_status`가 `scene=EDITOR, ready=true`. `editor_enter(SPH)`로 전환 → `editor_get_ship.facility=SPH`. `editor_leave` → `scene=SPACECENTER, ready=true`.
- C2: 진입 직후 `ready=false`인 동안 `editor_get_ship`을 불러 `not_ready`를 받는다(로딩이 빨라 못 잡으면 그 사실을 적는다). 우주센터에서 `editor_get_ship`·`editor_save`·`editor_load`·`editor_new` → `not_ready`와 현재 장면. 그 뒤 Player.log에 이 모듈의 예외가 없는지 확인한다.
- C3: `editor_list_parts`(필터 없음)의 `total`과 항목 필드 확인. `category=Engine` → 모두 엔진이고 `engine` 값이 있음. `search=swivel` → Swivel 포함. `limit/offset`으로 두 페이지가 겹치지 않음. 경력 저장이 있으면 그 저장에서 `total`이 샌드박스보다 작은지 확인하고, 없으면 확인하지 못했다고 적는다. 필터·페이지 로직은 단위 테스트로도 확인한다.
- C4: 빈 에디터에서 `editor_get_ship` → `part_count=0`. 기본 제공 `Kerbal X`를 불러와 부품 트리(부모, 노드, 표면 부착, 스테이지, 대칭)가 채워지는지 확인한다.
- C5: `editor_save(name=bridge-test-1)` → 파일이 생기고 `path` 일치. 같은 이름 다시 저장 → `bad_request`, `overwrite=true` → 성공. 빈 기체 저장 → `bad_request`. 이름 규칙 위반(`a/b`) → `bad_request`(단위 테스트로도 확인).
- C6: `editor_load(Kerbal X)` → `source=stock`, `editor_get_ship`에 Kerbal X 부품. `editor_load(bridge-test-1)` → `source=save`. 없는 이름 → `bad_request`. 설치되지 않은 부품 이름을 넣은 시험 craft와 읽을 수 없는 시험 craft → `bad_request`이고 `editor_get_ship`의 기체가 그대로다. 판정 로직은 단위 테스트로도 확인한다.
- C7: 기체가 있는 상태에서 `editor_new` → 다시 준비된 뒤 `editor_get_ship.part_count=0`.
- C8: `editor_list_crafts(VAB)`에 `Kerbal X`(stock)와 `bridge-test-1`(save)이 있고 부품 수가 맞다.
- C9: `git diff --stat <기준점>`에 코어·`debug`·배포 스크립트 파일이 없음. KSP를 켠 채 배포해 `get_status.versions.modules`에 Editor 모듈이 보이고, 클라이언트 재시작 없이 `editor_*` 도구가 호출되면 통과.
- 빌드: `dotnet build KspBridge.slnx` 경고 0, `dotnet test` 통과.

## 원칙 점검

- 적용 규칙
  - `workflow/spec-driven.md`, `git/github-issue.md`(#39)
  - `dotnet/project-creation.md`: `{Solution}.{역할}` 이름, src/tests 배치, slnx, MSTest
  - `git/commit-message.md`, `shell/powershell-windows.md`
  - 메모리 규칙: 기능별 별도 모듈(코어 고치지 않음), 화면 없이 MCP로 검증, 장면 전환 전 `get_status`, 포커스 잠금은 준비 판정에 넣지 않음, 로드 직후 실패는 준비 판정 결함으로 다룸, 비행 추적은 MCP 상태로(로그는 디버깅에만)
  - 골격 plan의 결정: 처리기는 메인 스레드에서만, Mod JSON은 자체 구현, 서버 모듈은 `IBridgeClient`만 사용
- 충돌
  - net472(Mod 모듈과 그 테스트): 골격과 같은 이유(KSP 1.12.5 Unity Mono)로 예외.
  - C2 확인에 Player.log를 본다: 메모리 규칙은 "로그는 디버깅에만"이지만, 게임 안 예외가 남지 않았음은 MCP 응답으로 알 수 없어 확인 용도로만 읽는다.
