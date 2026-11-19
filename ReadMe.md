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
MCP 클라이언트 ── http://127.0.0.1:65531/mcp ── KspBridge.Server (상주, 수동 실행)
                                                 └ 모듈 세대: KspBridge.Server.Modules.*  (도구)
                                    http://127.0.0.1:65530
                                                KspBridge.Mod.Loader (KSP 플러그인, 고정)
                                                 └ 모듈 세대: KspBridge.Mod.Core, KspBridge.Mod.Modules.*  (단계 종류)
```

양쪽 모두 **고정 호스트**와 **모듈 세대**로 나뉩니다.

- 고정 호스트: 서버 프로세스, KSP Loader. 포트와 MCP 세션을 유지합니다.
- 모듈 세대: 배포할 때마다 통째로 다시 읽힙니다.
  - 서버: collectible `AssemblyLoadContext`로 읽고, 이전 세대는 언로드합니다. 열린 MCP 세션에는 `notifications/tools/list_changed`가 갑니다.
  - KSP: `Assembly.Load(byte[])`로 읽습니다. Mono는 어셈블리를 내리지 못하므로 이전 세대는 메모리에 남습니다.
  - 실행 중인 계획은 `end_reason=reload`로 끝나고, 조종 콜백도 해제됩니다.

## 요구 사항

- .NET SDK 10
- KSP 1.12.5

## 빌드와 배포

1. `KspBridge.user.props.example`을 `KspBridge.user.props`로 복사하고 경로를 적습니다.
   - `KspDir`: KSP 설치 폴더
   - `ServerDir`: 서버를 배포할 폴더
2. 빌드합니다.

   ```powershell
   dotnet build KspBridge.slnx
   ```

3. 배포합니다. 빌드도 함께 합니다.

   ```powershell
   ./scripts/deploy.ps1
   ```

배포 결과는 다음과 같습니다.

- `GameData/KspBridge/Plugins/KspBridge.Mod.Loader.dll`: KSP가 시작할 때 읽는 고정 호스트입니다.
- `GameData/KspBridge/PluginData/modules/`: Mod 모듈 세대와 `manifest.json`입니다.
- `GameData/KspBridge/settings.cfg`: Mod HTTP 포트입니다(기본 65530).
- `<ServerDir>/`: 서버 실행 파일과 `kspbridge.yaml`입니다(처음 배포할 때 만들어집니다).
- `<ServerDir>/modules/`: 서버 모듈 세대와 `manifest.json`입니다.

KSP와 서버가 실행 중이면 `manifest.json` 변경을 보고 새 세대를 저절로 읽습니다. 고정 호스트 파일이 바뀌었을 때만 KSP나 서버를 다시 시작하라는 안내가 출력됩니다.

## 서버 실행과 MCP 등록

서버는 클라이언트가 띄우지 않으므로 직접 실행합니다.

```powershell
& "<ServerDir>\KspBridge.Server.exe" --config "<ServerDir>\kspbridge.yaml" --log "<ServerDir>\logs"
```

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
3. 두 프로젝트를 `KspBridge.slnx`에 추가하고 `./scripts/deploy.ps1`을 실행합니다. 배포 스크립트는 `*.Modules.*` 폴더를 찾아 매니페스트에 넣습니다.

보정·튜닝 기능도 주 기능을 고치지 말고 별도 모듈로 추가합니다.

## 테스트

```powershell
dotnet test KspBridge.slnx
```
