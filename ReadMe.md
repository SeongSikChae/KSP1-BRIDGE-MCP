# KspBridge

Kerbal Space Program 1(KSP 1.12.5)과 MCP(Model Context Protocol) 클라이언트를 잇는 브리지입니다.
MCP 클라이언트가 VAB·SPH 에디터에서 기체를 설계(부품 부착·이동·회전·설정·스테이징), 검사(구조·Δv·TWR·CoM/CoT/CoL), 렌더, 저장, 발사할 수 있습니다.
KSP 쪽 로직(Logic)과 MCP 서버 쪽 도구(Tools)를 각각 KSP·MCP 클라이언트 재시작 없이 교체할 수 있습니다.

## 구성

```
MCP 클라이언트 ──stdio──▶ KspBridge.App (셸) ──HTTP──▶ KspBridge.Mod (KSP 플러그인) ──▶ KspBridge.Logic
                              │   127.0.0.1:8085
                              └─▶ KspBridge.App.Tools (reload_app으로 교체)
```

| 프로젝트 | 대상 | 역할 | 변경 반영 |
| --- | --- | --- | --- |
| `src/KspBridge.App` | net10.0 | MCP stdio 서버 셸. 설정·로그, 도구 DLL 로더, `reload_app` | MCP 서버 재시작 |
| `src/KspBridge.App.Core` | net10.0 | 셸·도구 공유 코드(Mod HTTP 클라이언트) | MCP 서버 재시작 |
| `src/KspBridge.App.Tools` | net10.0 | MCP 도구(상태·리로드, 기체 설계 `EditorTools`). 도구 호출을 Mod의 HTTP API로 전달 | `reload_app` |
| `src/KspBridge.Mod` | net472 | KSP 애드온. HTTP 서버, 메인 스레드 디스패처, Logic DLL 로더, `get_status`의 에디터 상태 연결(`ILogic.Status`) | KSP 재시작 |
| `src/KspBridge.Logic` | net472 | KSP 쪽 로직. `/api/editor`·`/api/parts` 라우트: Editor(에디터·부품 배치·설정·스테이징·저장·발사), Catalog(부품 목록), Check(구조·성능 검사), Render(기체 렌더) | `reload_mod` |
| `tests/KspBridge.App.Tests` | net10.0 | App 단위·stdio 통합 테스트(MSTest) | |

KSP는 Unity 2019.4 Mono에서 동작하므로 Mod·Logic은 .NET Framework 4.7.2를 대상으로 합니다.
Mono는 같은 이름의 어셈블리를 다시 로드하지 못하므로 Logic은 빌드마다 고유한 어셈블리 이름(`KspBridge.Logic.yyyyMMddHHmmss`)을 쓰고, 배포 파일명은 `KspBridge.Logic.dll`로 고정합니다.
Tools는 .NET의 collectible `AssemblyLoadContext`로 로드하므로 이름을 바꾸지 않습니다.

## 요구 사항

- KSP 1.12.5
- .NET 10 SDK

## 로컬 경로 설정

PC마다 다른 경로는 저장소 루트의 `KspBridge.user.props`에 적습니다. 이 파일은 커밋하지 않습니다(`.gitignore`).

```xml
<Project>
  <PropertyGroup>
    <KspDir><KSP 설치 경로></KspDir>
    <AppDir><MCP 서버 배포 경로></AppDir>
  </PropertyGroup>
</Project>
```

| 속성 | 필수 | 용도 |
| --- | --- | --- |
| `KspDir` | Mod·Logic 빌드 시 | KSP 참조 DLL(`KSP_x64_Data\Managed`) 위치이자 `GameData` 배포 위치 |
| `AppDir` | MCP 서버 배포 시 | MCP 서버 고정 배포 경로. `dotnet publish src/KspBridge.App`의 출력 위치이자, Tools 빌드 후 `<AppDir>\Tools` 복사 위치. 없으면 publish는 기본 위치, Tools 복사는 생략 |

명령줄 `-p:KspDir="..."`, `-p:AppDir="..."`가 파일보다 우선합니다.
`KspDir`가 없으면 Mod·Logic 빌드는 오류로 멈춥니다(솔루션 전체 빌드 포함). App·테스트만 필요하면 해당 프로젝트를 직접 빌드합니다.

## KSP 쪽 빌드와 배포

```bash
dotnet build KspBridge.slnx
```

`KspDir\GameData`가 있으면 빌드 후 자동으로 배포됩니다.

| 파일 | 배포 위치 | 반영 방법 |
| --- | --- | --- |
| `KspBridge.Mod.dll` | `GameData\KspBridge\Plugins` | KSP 재시작 |
| `settings.cfg` | `GameData\KspBridge` | KSP 재시작 |
| `KspBridge.Logic.dll` | `GameData\KspBridge\PluginData` | `reload_mod` 도구 호출 |

`PluginData`에 두기 때문에 KSP가 Logic을 자동 로드하지 않고, Mod의 로더가 로드합니다.
Mod와 Logic 사이 인터페이스(`ILogic`)가 바뀌면 Mod의 `LoaderVersion`이 올라가며, 이때는 Mod·Logic을 함께 배포하고 KSP를 한 번 재시작해야 합니다(기체 설계 기능 추가 때 `ILogic.Status`가 생겨 `LoaderVersion` 0.0.2). 그 뒤 Logic만 바뀌면 `reload_mod`로 충분합니다.
KSP 실행 중에는 `Plugins\KspBridge.Mod.dll`이 잠겨 있을 수 있습니다. 그때 Mod를 바꾸려면 KSP를 끄고 빌드합니다.

### Mod 설정 (`settings.cfg`)

```
KSPBRIDGE
{
	port = 8085
}
```

- `port`: HTTP 서버 포트

## MCP 서버

### 배포

```bash
dotnet publish src/KspBridge.App
```

`AppDir`에 `KspBridge.App.exe`와 `Tools\KspBridge.App.Tools.dll`이 배포됩니다. MCP 클라이언트(Cursor, Claude Code)는 모두 이 고정 경로의 exe를 실행합니다.
설정 파일과 로그 디렉터리도 `AppDir` 아래에 둡니다(예: `<AppDir>\kspbridge.yaml`, `<AppDir>\logs`).
실행 중인 서버가 exe를 잠그므로, 셸(App·Core)을 바꿨다면 MCP 클라이언트에서 서버를 끄고 publish한 뒤 다시 켭니다.

### 설정 파일 (YAML)

```yaml
ContentRootPath: <콘텐츠 루트 경로>
BaseUrl: http://127.0.0.1:8085
TimeoutSeconds: 30
```

| 키 | 설명 |
| --- | --- |
| `ContentRootPath` | 호스트 콘텐츠 루트 경로 |
| `BaseUrl` | KspBridge Mod HTTP 주소(`settings.cfg`의 `port`와 맞춤) |
| `TimeoutSeconds` | 요청 기본 제한 시간(초) |

### 실행

MCP 클라이언트가 실행합니다. Cursor는 `~/.cursor/mcp.json`, Claude Code는 `~/.claude.json`의 `mcpServers`에 등록합니다.

```json
{
  "mcpServers": {
    "kspbridge": {
      "command": "<AppDir>\\KspBridge.App.exe",
      "args": ["--config", "<AppDir>\\kspbridge.yaml", "--log", "<AppDir>\\logs"]
    }
  }
}
```

직접 실행할 때:

```bash
KspBridge.App.exe --config <설정 파일> --log <로그 디렉터리>
```

stdout은 MCP 채널이므로 콘솔 로그는 모두 stderr로 나갑니다. 파일 로그는 로그 디렉터리에 `KspBridge.App.log`로 일별 기록됩니다(12개 보관).

| 종료 코드 | 의미 |
| --- | --- |
| 0 | 정상 종료 |
| 1 | 잘못된 인수 |
| 2 | 설정 오류 |
| 3 | 실행 오류 |

### 도구 교체 (`reload_app`)

1. `dotnet build src/KspBridge.App.Tools`를 실행합니다. 빌드 후 `<AppDir>\Tools`로 복사됩니다.
2. MCP 도구 `reload_app`을 호출합니다.

셸은 `Tools\KspBridge.App.Tools.dll`을 파일을 잠그지 않고 읽어 들여 도구를 교체하고, 클라이언트에 `notifications/tools/list_changed`를 한 번 보냅니다.
`initialize` 핸드셰이크를 쓰는 클라이언트(프로토콜 2025-11-25 이하)는 이 알림을 그대로 받고, 2026-07-28 이후 클라이언트는 `subscriptions/listen`으로 구독했을 때만 받습니다.
클라이언트가 알림을 반영하지 않으면 기존 도구의 구현 교체는 바로 적용되지만, 도구 추가·스키마 변경은 클라이언트를 재시작해야 보입니다.

새 DLL을 로드하지 못하면 이전 도구를 유지합니다. 시작할 때 도구 DLL이 없거나 깨졌으면 `reload_app`만 노출됩니다.
실행 중인 셸보다 새 Core API나 셸에 등록되지 않은 의존성을 쓰는 Tools는 로드는 되지만 도구 호출 때 실패합니다. 그때는 셸을 publish하고 MCP 서버를 재시작합니다.
교체할 때마다 이전 도구 어셈블리가 메모리에 조금씩 남을 수 있으므로, 오래 실행했다면 MCP 서버를 재시작합니다.

## MCP 도구

| 분류 | 도구 | 위치 |
| --- | --- | --- |
| 상태 | `get_status` (씬, 활성 기체, 에디터 상태 `editor.ready`, 비행 요약 `flight`, 입력 잠금) | Tools |
| 리로드 | `reload_mod` | Tools |
| 리로드 | `reload_app` | 셸 |
| 에디터 | `open_editor`, `new_craft`, `save_craft` | Tools |
| 조회 | `list_parts`, `get_part`, `list_crafts`, `get_craft` | Tools |
| 편집 | `add_part`, `remove_part`, `offset_part`, `rotate_part`, `set_part_field`, `set_resource` | Tools |
| 스테이징 | `set_stage` | Tools |
| 검증 | `check_craft`, `render_craft` | Tools |
| 발사 | `launch_craft` | Tools |
| 비행 계획 | `submit_flight_plan`, `replace_flight_steps`, `abort_flight` | Tools |
| 비행 조회 | `get_flight`, `get_flight_events` | Tools |
| 시간 가속 | `set_time_warp` | Tools |

기체 설계 도구는 VAB·SPH 에디터에서 동작합니다(`list_parts`·`get_part`·`list_crafts`는 세이브만 불러와 있으면 어느 씬에서나 됩니다).
`open_editor`·`new_craft`·`launch_craft`는 바로 `{accepted}`를 돌려주므로 `get_status`로 완료를 확인합니다. `open_editor`·`new_craft`는 `editor.ready`, `launch_craft`는 `reset.requested_scene`이 `FLIGHT`이고 `reset.scene_ready`가 true일 때 끝난 것입니다(게임이 비행 씬 로드를 한 프레임 늦게 요청하고, `scene`은 로드 요청 시점에 바뀝니다). EDITOR에 머물고 `input_locks`에 `EDITOR_LAUNCH` 잠금이 있으면 발사 전 검사 대화상자가 화면 입력을 기다리는 것입니다. `reset.loading_seconds`가 계속 늘어나면 `reload_mod`로 풉니다. `launch_craft`의 `clear_site: true`는 발사 지점에 착지한 기체를 대화상자 없이 먼저 회수합니다.
부품은 `persistentId`(`get_craft`의 `id`)로 가리키고, 좌표는 루트 부품 기준(+Y = 로켓 위·비행기 기수)입니다. 편집 도구는 기체를 바꾼 호출마다 에디터 Undo 한 단계를 남기고 응답에 관련 구조 경고를 담습니다.
설계 결정은 [기체 설계 설계 문서](docs/ksp-bridge/0002-기체설계-설계.md), 단위별 구현 결정은 [기체 설계 개발 문서](docs/ksp-bridge/0003-기체설계-개발.md)에 있습니다.

### 비행 제어

MCP 클라이언트(Planner)가 단계 목록으로 계획을 세워 `submit_flight_plan`으로 제출하면, Mod(Coordinator)가 물리 틱마다 단계를 실행하고 자세·스로틀을 제어합니다. 진행은 `get_flight`(현재 상태, 필드 이름 = 조건 변수 이름)와 `get_flight_events`(cursor `since`/`next`, `epoch`가 바뀌면 0부터 다시 읽음)로 확인합니다.

| Mode | 하는 일 | 끝나는 때 |
| --- | --- | --- |
| `attitude` | 피치·방위(`pitch`, `heading`, `roll`?) 또는 기준 방향(`reference`)을 유지하고 `throttle`(기본 0)을 냄. 자세 인자가 없으면 스로틀만 | `until` 필수 |
| `gravity_turn` | 고도에 따라 기울이며 상승하고, 예측 원지점이 목표에 가까워지면 스로틀을 줄임. `target_apoapsis` 필수(대기권보다 높게) | 대기권 밖에서 예측 원지점 ≥ 목표, 스로틀 0 |
| `coast` | 스로틀 0, 기준 방향(기본 `prograde`, `none`이면 자세 제어 없음) 유지. 시간 조건이면 대기권 밖에서 레일 가속, 목표 15~10초 전에 해제 | `until` 필수 |
| `circularize` | 원지점 전 시작 시각까지 기다렸다가(가속 포함) 원궤도화 연소. 시작 시각이나 원지점이 이미 지났으면 바로 연소, 자세 오차 5° 넘으면 스로틀 0 | 남은 Δv ≤ 잔여 추력 Δv + `tolerance`(0.5 m/s). 10 m/s 아래에서 방향을 고정한 뒤 Δv가 다시 커지거나 이심률이 5틱 동안 줄지 않아도 완료 |

발사부터 궤도까지 한 계획으로 날리는 예입니다(첫 점화는 `on_start: ["stage"]`가 필요합니다).

```json
{
  "auto_stage": true,
  "steps": [
    { "name": "상승", "mode": "gravity_turn", "target_apoapsis": 80000, "on_start": ["stage"], "timeout_s": 600 },
    { "name": "원궤도화", "mode": "circularize", "timeout_s": 900 }
  ]
}
```

- 조건(`until`, `abort`)은 `{var, op, value, for_s?}`, `{all: [...]}`, `{any: [...]}`, 배열 `[...]`(= `all`)입니다. `op`는 `<`, `<=`, `>`, `>=`, `==`, `!=`이고, 문자열 변수(`situation`, `staging`)는 `==`, `!=`만 됩니다. 값을 모르는(null) 변수는 거짓입니다. 예: `{"var": "apoapsis", "op": ">=", "value": 150000}`.
- **자동 스테이징**(`auto_stage: true`): 연소 종료 → 잔여 추력 감쇠(tailoff) → 분리 → 이격(settle) → 점화를 스스로 합니다. 병렬 부스터는 코어를 계속 태우고, 직렬 단은 스로틀을 0으로 두고 분리한 뒤 이격 거리만큼 떨어지면 점화합니다. 다 쓴 엔진을 떼어낼 수 없거나 남는 쪽에 엔진이 없으면 스테이지를 쓰지 않고 `out_of_stages`를 남깁니다. 점화할 다음 스테이지에 엔진이 없으면(페어링·낙하산 등) 그 스테이지를 쓰지 않고 `ignite_blocked`, 스테이지 작동이 확인되지 않으면 `stage_failed`입니다. 이 셋 뒤에 Mode가 추력을 원하면 `no_thrust`로 중단하고, 다시 시작하려면 `on_start: ["stage"]`가 필요합니다. 분리 뒤 조종 부품이 떨어져 나갈 구성은 제출 때 400 `invalid_plan`(메시지에 `control_part_jettisoned`)으로 거부합니다.
- **스테이징 설정**(`staging_options`): `tailoff_ratio` 0.05, `max_tailoff_s` 5, `settle_distance` 2 m, `max_settle_s` 3.
- **추력 없음**: Mode가 스로틀 > 0을 원하는데 작동 중인 엔진이 없고 기다릴 스테이징도 없을 때입니다(엔진 없는 `coast`는 해당 없음). 제출과 `replace_flight_steps`(현재 단계 포함)는 409 `no_thrust`, 실행 중이면 `plan_aborted {reason: no_thrust}`입니다.
- 실행 중인 계획의 뒤 단계(또는 현재 단계부터)는 `replace_flight_steps`로 바꿉니다. `abort_flight`는 스로틀 0, 시간 가속 해제(사용자가 건 가속 포함), SAS를 계획 전 상태로 되돌리고 제어를 놓습니다(`plan_aborted {reason: user}`, 계획이 없으면 409 `no_plan`).
- **중단 reason**: `condition`(abort 조건), `timeout`, `user`, `no_thrust`, `error`, `scene`(씬 전환), `vessel_changed`(활성 기체 변경), `vessel_lost`(기체 파괴), `reload_incompatible`.

**reload 동작**: 계획 실행 중 `reload_mod`를 해도 계획이 끊기지 않습니다. 이전 Logic이 계획·이벤트·Mode 상태·스테이징 진행·직전 명령·SAS·자세 제어 적분값을 넘기고, 새 Logic이 계획을 다시 검증해 같은 단계에서 이어받습니다(`logic_reloaded {resumed: true}`, `plan.id`·`epoch` 유지, `seq` 이어짐). 계획이 건 시간 가속은 넘기기 전에 풀고(`warp_stopped`), 단계가 계속 가속을 원하면 새 Logic이 다시 겁니다(사용자가 취소한 가속은 그 단계에서 다시 걸지 않음). 이어받을 수 없으면(새 검증 규칙에서 실패, 활성 기체가 다름 등) `plan_aborted {reason: reload_incompatible}`로 중단합니다. 활성 기체가 계획의 기체면 스로틀 0과 SAS 복원을 하고, 다른 기체면 입력 스로틀만 0으로 둡니다. 이어받았는데 추력만 없으면 `no_thrust`로 중단합니다. 계획이 없으면 사용자의 스로틀·SAS·가속을 건드리지 않습니다. 비행 제어 설계는 [비행제어 설계 문서](docs/ksp-bridge/0005-비행제어-설계.md), 단위별 구현 결정은 [비행제어 개발 문서](docs/ksp-bridge/0006-비행제어-개발.md)에 있습니다.

## 테스트

```bash
dotnet test tests/KspBridge.App.Tests
```

KSP 경로가 필요 없습니다. stdio 통합 테스트는 빌드된 App 실행 파일을 임시 폴더에 복사해 실행합니다.
게임 동작은 KSP를 실행한 상태에서 MCP 도구로 검증합니다.

## 문서

기능별 방안·설계·개발 문서는 [docs/ksp-bridge](docs/ksp-bridge)에 있습니다.
