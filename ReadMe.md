# KSP1 Bridge MCP

LLM(MCP 클라이언트)이 Kerbal Space Program 1.12.5를 제어하는 브리지입니다.

## 구조

Planner – Coordinator – Controller 세 계층으로 나뉩니다.

| 계층 | 위치 | 역할 |
|---|---|---|
| Planner | MCP 클라이언트의 LLM | 상태를 보고 계획을 제출·교체·중단 |
| Coordinator | KSP 안 (`KspBridge.Mod.Core`) | 계획을 단계별로 물리 프레임마다 판정하고 다음 단계로 넘김 |
| Controller | KSP 안 (`KspBridge.Mod.Core`) | 계획 실행 중 활성 기체의 `OnFlyByWire`에서 단계가 기체를 제어 |

```
MCP 클라이언트 ── http://127.0.0.1:65531/mcp ── KspBridge.Server (상주, 콘솔 또는 Windows 서비스)
                                                 └ 모듈 세대: KspBridge.Server.Modules.*  (도구)
                                    http://127.0.0.1:65530
                                                KspBridge.Mod.Loader (KSP 플러그인, 고정)
                                                 └ 모듈 세대: KspBridge.Mod.Core, KspBridge.Mod.Modules.*  (단계 종류)
```

양쪽 모두 **고정 호스트**와 **모듈 세대**로 나뉩니다.

- 고정 호스트: 서버 프로세스, KSP Loader. 포트와 MCP 세션을 유지합니다.
- 모듈 세대: 게시할 때마다 통째로 다시 읽힙니다.
  - 서버: collectible `AssemblyLoadContext`로 읽고, 이전 세대는 언로드합니다. 열린 MCP 세션에는 `notifications/tools/list_changed`가 갑니다.
  - KSP: `Assembly.Load(byte[])`로 읽습니다. Mono는 어셈블리를 내리지 못하므로 이전 세대는 메모리에 남습니다.
  - 실행 중인 계획은 `end_reason=reload`로 끝나고, 조종 콜백도 해제됩니다.

## 요구 사항

- .NET SDK 10
- KSP 1.12.5

## 빌드와 게시

`KspBridge.user.props.example`을 `KspBridge.user.props`로 복사하고 경로를 적습니다(커밋하지 않습니다).

- `KspDir`: KSP 설치 폴더. 빌드 참조와 Mod 게시 대상에 씁니다.
- `ServerModulesDirs`: 서버 모듈을 게시할 폴더. 실행하는 서버의 모듈 폴더(`ContentRootPath` + `ModulesDir`)를 적고, 여러 개면 `;`로 나눕니다.
- `ModDeployDir`(선택): Mod를 게시할 `GameData/KspBridge` 폴더. 비우면 `<KspDir>/GameData/KspBridge`입니다.

빌드는 아무 데도 복사하지 않습니다. 배포는 게시(publish)로 합니다.

```powershell
dotnet build KspBridge.slnx
```

| 게시할 프로젝트 | 결과 |
|---|---|
| `KspBridge.Server` | 게시 폴더에 서버 호스트, 모든 서버 모듈(`modules/`, `manifest.json`), 설정 샘플 `kspbridge.sample.yaml` |
| `KspBridge.Server.Modules.*` 가운데 아무것 | 모든 서버 모듈과 `manifest.json`을 `ServerModulesDirs`의 폴더마다 둡니다. 목록에 없는 dll·pdb는 지웁니다 |
| `KspBridge.Mod.Core`, `KspBridge.Mod.Modules.*` 가운데 아무것 | 새 세대 표식으로 Mod 모듈 전체를 빌드해 `GameData/KspBridge/PluginData/modules/`에 두고 이전 세대를 지웁니다 |
| `KspBridge.Mod.Loader` | `GameData/KspBridge/Plugins/KspBridge.Mod.Loader.dll`, 없을 때만 `GameData/KspBridge/settings.cfg`(Mod HTTP 포트, 기본 65530) |

```powershell
dotnet publish src/KspBridge.Server.Modules.Wait
dotnet publish src/KspBridge.Mod.Core
dotnet publish src/KspBridge.Mod.Loader
dotnet publish src/KspBridge.Server -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o <게시 폴더>
```

- 모듈은 게시 순서대로 파일을 다 둔 뒤 `manifest.json`을 마지막에 씁니다. 실행 중인 서버와 KSP는 이 파일이 바뀌면 새 세대를 저절로 읽습니다. 서버나 KSP를 멈출 필요가 없습니다.
- Mod 모듈은 세대 이름으로 `KspBridge.Mod.Core`를 참조하므로 하나만 게시해도 늘 전체가 함께 나갑니다.
- 고정 호스트(서버 exe와 호스트 DLL, Mod Loader)는 실행 중이면 잠겨 있어 게시가 실패합니다. 서버나 KSP를 멈춘 뒤 게시하고 다시 시작합니다.
- Visual Studio 폴더 게시 프로필을 쓸 때는 기존 파일 삭제(`DeleteExistingFiles`)를 켜지 않습니다. 켜면 게시 폴더의 설정 파일·`modules`·로그가 지워집니다. CLI의 `dotnet publish -p:PublishProfile=...`는 프로필의 임시 폴더(`PublishDir`)까지만 쓰므로, 게시 폴더로 바로 보내려면 `-o`를 씁니다.

## 서버 실행과 MCP 등록

서버는 클라이언트가 띄우지 않습니다. 게시 폴더의 `kspbridge.sample.yaml`을 복사해 설정 파일(예: `kspbridge.yaml`)을 만들고 값을 맞춘 뒤 콘솔이나 Windows 서비스로 실행합니다.

```powershell
& "<게시 폴더>\KspBridge.Server.exe" --config "<게시 폴더>\kspbridge.yaml" --log "<게시 폴더>\logs"
```

- 콘솔: 위 명령으로 실행하고 Ctrl+C로 끝냅니다.
- Windows 서비스: 같은 exe와 인자로 서비스를 등록합니다. 서비스는 현재 폴더 `C:\Windows\System32`에서 시작하므로 `--config`·`--log`와 설정 파일의 `ContentRootPath`를 절대 경로로 적습니다. 실행 계정은 등록할 때 정합니다.
- 서비스로 실행 중일 때 서버 호스트를 바꾸려면 서비스를 멈추고, 서버를 게시한 뒤, 서비스를 다시 시작합니다. 서버 모듈은 서비스를 멈추지 않고 게시합니다.
- 설정 오류처럼 서버가 시작하기 전에 난 오류는 stderr와 함께 `<--log 폴더>/KspBridge.Server.startup.log`에도 남습니다. 서비스가 시작되지 않으면 이 파일을 봅니다.

Claude Code에는 한 번만 등록합니다.

```powershell
claude mcp add --transport http kspbridge http://127.0.0.1:65531/mcp
```

서버 프로세스를 다시 실행하면 기존 MCP 세션이 끊길 수 있습니다. 그때는 클라이언트에서 다시 연결합니다(Claude Code: `/mcp`).

종료 코드는 다음과 같습니다.

| 코드 | 의미 |
|---|---|
| 0 | 정상 종료, `--help` |
| 1 | 인자 오류 |
| 2 | 설정 오류 |
| 3 | 호스트 실행 오류 |

## 도구

| 도구 | 모듈 | 설명 |
|---|---|---|
| `reload` | 호스트 | 서버나 Mod 모듈 세대를 강제로 다시 읽음 |
| `get_status` | core | 씬, 준비 여부, 버전·세대, 기체, 계획, Controller 상태 |
| `submit_plan`, `get_plan`, `abort_plan` | core | 계획 제출·조회·중단 |
| `get_flight_events` | core | 단계·계획 이벤트(`since`로 새 이벤트만 읽기) |
| `list_step_types` | core | 로드된 단계 종류와 매개변수 |
| `submit_wait` | wait | `wait` 한 단계 계획 |
| `debug_launch`, `debug_revert`, `debug_space_center` | debug | 검증용 발사·되돌리기·우주센터 이동 |
| `editor_enter`, `editor_leave` | editor | VAB/SPH 에디터 진입·시설 전환·우주센터로 이탈 |
| `editor_list_parts` | editor | 쓸 수 있는 부품 목록(분류·검색·페이지) |
| `editor_get_ship` | editor | 조립 중인 기체(부품 트리, 부착, 스테이지, 대칭, 질량) |
| `editor_save`, `editor_load`, `editor_new`, `editor_list_crafts` | editor | craft 저장·불러오기·새로 시작·목록 |
| `editor_place_root`, `editor_set_root` | editor | 빈 에디터에 루트 부품 놓기, 다른 부품을 루트로 |
| `editor_attach_node`, `editor_attach_surface` | editor | 노드 부착, 표면 부착(방사 대칭 1~8, 대칭 묶음에 붙이면 묶음 전체에) |
| `editor_remove`, `editor_replace`, `editor_move` | editor | 제거(대칭 짝·아래 부품 포함), 다른 부품으로 교체, 이동·회전 |
| `editor_get_part_settings`, `editor_set_part_settings` | editor | 에디터에서 바꿀 수 있는 자원 양·모듈 설정(추력 제한, 짐벌, 분리 힘 등) |
| `editor_get_stages`, `editor_set_stage`, `editor_swap_stages` | editor | 스테이지 조회, 부품 옮기기, 두 스테이지 바꾸기 |

조립 편집 도구는 기체를 craft로 다시 만들어 에디터에 다시 불러오는 방식이다. 편집할 때마다 아래 두 파일을 쓴다.

- `GameData/KspBridge/PluginData/editor/edit.craft`: 편집한 기체를 불러오기 위한 임시 craft(KSP는 `PluginData`를 읽지 않는다).
- `Ships/@thumbs/editor/edit.png`: 게임이 임시 craft의 썸네일을 편집마다 그리지 않도록 두는 작은 자리 표시 이미지. 이 파일 하나가 게임 폴더에 남는다.

편집하면 에디터의 카메라·선택이 초기화되고, 게임의 되돌리기(undo) 기록은 남지 않는다. 마우스에 들려 있던 부품은 버려지며 결과의 `dropped_held_part`로 알린다.

## 기능 모듈 추가

기능 하나는 서버 모듈과 Mod 모듈 한 쌍이며, 코어 코드는 고치지 않습니다.

1. **Mod 모듈**: `src/KspBridge.Mod.Modules.<이름>/`
   - `IBridgeModule`을 구현합니다.
   - `Register`에서 `context.AddStepType(...)`(Coordinator가 판정하고 Controller가 제어할 단계)와 `context.AddHandler(op, ...)`(`POST /api/<이름>/<op>`)를 등록합니다.
   - GameEvents 같은 외부 구독은 `context.Subscriptions`에 등록합니다. 그래야 reload할 때 해제됩니다.
   - csproj는 `KspBridge.Mod.Modules.Wait`를 본뜹니다.
2. **서버 모듈**: `src/KspBridge.Server.Modules.<이름>/`
   - `[assembly: KspBridgeModule("<이름>")]`을 붙입니다.
   - `[McpServerToolType]` 클래스에 `[McpServerTool(Name = ...)]` 메서드를 만듭니다.
   - 생성자나 매개변수로 `IBridgeClient`를 받아 `CallAsync("<이름>", "<op>", args)`를 부릅니다.
   - csproj는 `KspBridge.Server.Modules.Wait`를 본뜹니다.
3. 두 프로젝트를 `KspBridge.slnx`에 추가하고 서버 모듈과 Mod 모듈을 게시합니다. 게시는 `src/KspBridge.Server.Modules.*`, `src/KspBridge.Mod.Modules.*` 폴더를 찾아 매니페스트에 넣습니다.

보정·튜닝 기능도 주 기능을 고치지 말고 별도 모듈로 추가합니다.

## 테스트

```powershell
dotnet test KspBridge.slnx
```
