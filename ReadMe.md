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

- 고정 호스트: 서버 프로세스, KSP Loader. 포트를 유지합니다.
- 모듈 세대: 게시할 때마다 통째로 다시 읽힙니다.
  - 서버: collectible `AssemblyLoadContext`로 읽고, 이전 세대는 언로드합니다. `subscriptions/listen`으로 구독한 클라이언트에 `notifications/tools/list_changed`가 갑니다.
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

서버는 최신 MCP 프로토콜(`2026-07-28`)로 세션 없이 동작합니다. 서버 프로세스를 다시 실행한 뒤 모듈을 게시해도 도구 목록이 바뀌지 않으면 클라이언트에서 MCP를 다시 연결합니다(Claude Code: `/mcp`).

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
| `get_flight` | flight | 활성 기체의 비행 상태: 위치·고도·속도·궤도 요소·자세·조종 상태·시간 가속·질량·추력·TWR·스테이지 Δv(`include`로 스테이지별 표·자원·천체 상수) |
| `debug_launch`, `debug_revert`, `debug_space_center` | debug | 검증용 발사·되돌리기·우주센터 이동 |
| `debug_set_orbit` | debug | 검증용: 활성 기체를 지정 Ap·Pe·경사각 궤도로 옮김(게임의 궤도 설정 치트) |
| `debug_list_vessels` | debug | 검증용: 비행 장면의 기체 목록(id·이름·종류·상황·천체·로드·소유·활성·거리) |
| `debug_switch_vessel` | debug | 검증용: 로드된 소유 기체로 활성 기체 전환(장면 재로드 없음, `force`로 게임의 전환 금지 상태 무시) |
| `debug_save` | debug | 검증용: 이름 있는 저장(생략하면 `quicksave`, `persistent`는 쓰지 않음) |
| `debug_load` | debug | 검증용: 비행 중 저장한 파일을 불러와 저장 때의 활성 기체로 비행 시작 |
| `editor_enter`, `editor_leave` | editor | VAB/SPH 에디터 진입·시설 전환·우주센터로 이탈 |
| `editor_list_parts` | editor | 쓸 수 있는 부품 목록(분류·검색·페이지) |
| `editor_get_ship` | editor | 조립 중인 기체(부품 트리, 부착, 스테이지, 대칭, 질량) |
| `editor_save`, `editor_load`, `editor_new`, `editor_list_crafts` | editor | craft 저장·불러오기·새로 시작·목록 |
| `editor_place_root`, `editor_set_root` | editor | 빈 에디터에 루트 부품 놓기, 다른 부품을 루트로 |
| `editor_attach_node`, `editor_attach_surface` | editor | 노드 부착, 표면 부착(방사 대칭 1~8, 대칭 묶음에 붙이면 묶음 전체에) |
| `editor_remove`, `editor_replace`, `editor_move` | editor | 제거(대칭 짝·아래 부품 포함), 다른 부품으로 교체, 이동·회전 |
| `editor_get_part_settings`, `editor_set_part_settings` | editor | 에디터에서 바꿀 수 있는 자원 양·모듈 설정(추력 제한, 짐벌, 분리 힘 등) |
| `editor_get_stages`, `editor_set_stage`, `editor_swap_stages` | editor | 스테이지 조회, 부품 옮기기, 두 스테이지 바꾸기 |
| `check_ship` | check | 발사 전 조립 점검: 지정 천체 기준 스테이지별 질량·Δv·TWR·연소 시간, 무게중심 어긋남, 구조 경고 8종 |
| `measure_start`, `measure_stop` | measure | 검증용 계측 시작·끝: 계측 중 물리 프레임마다 활성 기체의 값(조종 상태·입력, 질량·추력, 자세·각속도, 스테이지, 고도·속도)을 모음 |
| `get_measurement` | measure | 실행 중이거나 가장 최근에 끝난 계측의 항목별 요약(경과 시간 범위 지정 가능)과 표본 목록 |

`get_plan`의 실행 중 계획(`current`)에는 진행 값이 붙는다. 계획 경과 시간(`elapsed_s`), 단계 경과 시간(`step_elapsed_s`), timeout까지 남은 시간(`step_timeout_remaining_s`), 단계가 보고한 목표까지 남은 양(`step_progress`, 예: `wait`의 `target_s`·`remaining_s`)이다. 단계가 아직 시작되지 않았으면 단계 값은 `null`이다. 같은 계획 요약을 돌려주는 `submit_plan`·`submit_wait` 응답에도 같은 값이 붙는다.

기본 제어는 별도 도구 없이 계획 단계로 쓴다. 인자는 `list_step_types`에 나온다.

| 단계 | 하는 일 |
|---|---|
| `throttle` | 스로틀을 바꾼다. 값은 뒤 단계와 계획 완료 뒤에도 남고, 계획이 중단·단계 실패·timeout·reload·부품 손실·추력 없음으로 끝나면 0으로 내린다. 조종 불가인 기체(승무원·전기·통신 등이 없음)에서는 단계가 실패한다 |
| `attitude` | 피치·헤딩(·롤)이나 기준 방향으로 기수를 돌리고, 정렬되면 완료하며, 뒤 단계 동안 자세를 계속 유지한다. SAS를 끈다. 조종 불가인 기체이거나 정렬을 기다리다 조종 불가가 되면 단계가 실패한다 |
| `attitude_off` | 유지 중인 자세를 푼다 |
| `stage` | 다음 스테이지를 작동한다. 게임이 거절하면(작동 직후 약 0.6초) 단계가 실패하므로 두 번 잇는 사이에 `wait`를 둔다 |
| `action_group` | 사용자 그룹 1~10, 기어·조명·브레이크·어보트, SAS·RCS를 켜고 끈다. SAS를 켜면 유지 중인 자세가 풀린다 |

상태 조건까지 기다리는 단계는 `wait_until`이다. `params.all`(모두) 또는 `params.any`(하나라도)에 조건을 적고, 조건이 맞은 물리 프레임에 완료된다. 값 이름은 `get_flight`와 같고(고도·속도·Ap/Pe·자세·질량·추력·상황 등) `elapsed_s`(단계 경과 시간)와 `no_thrust`(켰던 엔진이 모두 꺼짐)를 더 쓸 수 있다. 조종 입력은 넣지 않으므로 앞 단계의 스로틀과 자세가 그대로 이어진다.

```json
{ "type": "wait_until", "params": { "all": [ { "value": "apoapsis_m", "op": ">=", "to": 80000 } ] }, "timeout_s": 300 }
```

시간 가속 단계는 `warp`이다. `params.seconds`(단계 시작부터 게임 시간), `params.ut`(게임 시각), `params.to`(`apoapsis`·`periapsis`, 몇 초 전은 `lead_s`) 가운데 하나를 목표로 받아, 게임이 허용하는 만큼 레일 가속으로 가고 목표 전에 1배로 내려 완료한다. 대기권·추력 중처럼 레일 가속이 막힌 구간은 1배로 진행하며, `physics_rate`(2~4)를 적었을 때만 그 구간에서 물리 가속을 쓴다. 레일 배율의 상한은 `max_rate`이다. 실행되는 동안 배율은 단계가 정하고, 계획이 어떻게 끝나도 1배로 돌아온다. 레일 위에서는 스로틀이 0으로 보이고 단계가 완료될 때 되돌아온다.

```json
{ "type": "warp", "params": { "to": "apoapsis", "lead_s": 30 } }
```

도착 오차, 쓴 배율, 레일이 막혔던 이유는 이벤트 `warp_completed`에, 중간에 끝나 배율을 되돌린 것은 `warp_released`에 남는다. 지금 배율은 `get_flight`의 `time_warp`에 나온다.

계획은 이상이 생기면 스스로 끝난다. `abort_on`은 계획 전체에 하나 둔다(예: `{ "steps": [...], "abort_on": { "no_thrust": true } }`).

| 종료 사유 | 언제 | `abort_on` |
|---|---|---|
| `vessel_destroyed` | 활성 기체가 파괴됨 | 늘 켜짐 |
| `part_loss` | 활성 기체의 부품이 폭발하거나 관절이 부러져 떨어짐(스테이징 분리는 아님) | `part_loss`, 기본 켜짐 |
| `no_thrust` | 스로틀이 0보다 큰데 켰던 엔진이 모두 꺼짐 | `no_thrust`, 기본 꺼짐 |

손실된 부품과 원인은 이벤트 `parts_lost`에, 조건이 맞은 값은 이벤트 `condition_met`에 남는다. 파괴된 활성 기체에는 계획을 제출할 수 없다(`not_ready`).

유지 중인 자세는 `get_plan` `current`의 `hold`에 나오고, 풀리면 이벤트 `hold_released`에 사유가 남는다. 계획이 끝나면 늘 풀린다.

`get_flight`는 기체를 바꾸지 않는다. Δv·연소 시간·스테이지 TWR은 게임 Δv 계산기의 값이며, 계산기가 아직 값을 내지 않았으면 `delta_v_ready`가 `false`이고 그 값들은 `null`이다.

`check_ship`은 기체를 바꾸지 않는다. 스테이지 값은 게임 Δv 계산기의 값이며, 다른 천체를 지정하면 점검하는 동안만 게임 Δv 창의 천체가 그 천체로 잠깐 바뀌었다가 원래 천체로 돌아온다.

계측은 기체와 계획을 바꾸지 않으며 한 번에 하나만 실행된다. 구간 경계를 물리 프레임에 맞추려면 계획 단계 `measure_start`·`measure_stop`을 넣는다(예: `measure_start` → `wait` → `measure_stop`). 단계는 시작한 프레임에 완료되고, 계획이 끝나도 계측은 계속된다. 표본 상한(`sample_limit`)은 기본 30,000개, 최대 180,000개이며 이르면 계측이 사유 `sample_limit`로 끝난다. 새 비행·장면 전환·활성 기체 변경에서도 사유와 함께 끝나고, Mod를 다시 읽으면 계측 데이터가 사라진다.

조립 편집 도구는 기체를 craft로 다시 만들어 에디터에 다시 불러오는 방식이다. 편집할 때마다 아래 두 파일을 쓴다.

- `GameData/KspBridge/PluginData/editor/edit.craft`: 편집한 기체를 불러오기 위한 임시 craft(KSP는 `PluginData`를 읽지 않는다).
- `Ships/@thumbs/editor/edit.png`: 게임이 임시 craft의 썸네일을 편집마다 그리지 않도록 두는 작은 자리 표시 이미지. 이 파일 하나가 게임 폴더에 남는다.

편집하면 에디터의 카메라·선택이 초기화되고, 게임의 되돌리기(undo) 기록은 남지 않는다. 마우스에 들려 있던 부품은 버려지며 결과의 `dropped_held_part`로 알린다.

## 기능 모듈 추가

기능 하나는 서버 모듈과 Mod 모듈 한 쌍이며, 코어 코드는 고치지 않습니다.

1. **Mod 모듈**: `src/KspBridge.Mod.Modules.<이름>/`
   - `IBridgeModule`을 구현합니다.
   - `Register`에서 `context.AddStepType(...)`(Coordinator가 판정하고 Controller가 제어할 단계)와 `context.AddHandler(op, ...)`(`POST /api/<이름>/<op>`)를 등록합니다.
   - 단계가 끝난 뒤에도 계획이 끝날 때까지 이어질 입력은 `StepContext.Plan.SetHold(...)`(`IPlanHold`)로 걸고, 계획이 끝날 때 할 일은 `StepContext.Plan.AtEnd(...)`로 등록합니다. 계획이 어떤 사유로 끝나든 코어가 hold를 풀고 등록한 일을 부릅니다.
   - 단계와 상관없이 계획을 스스로 끝내는 판정은 `context.AddGuard(...)`(`IPlanGuard`)로 더합니다. 코어가 매 물리 프레임에 단계 판정보다 먼저 묻고, 감시가 알린 사유로 계획을 끝냅니다. 감시가 알리는 키는 계획의 `abort_on`이 됩니다.
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
