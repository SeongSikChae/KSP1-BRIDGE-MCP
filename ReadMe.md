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
Mod와 Logic 사이 인터페이스(`ILogic`)나 로더 동작이 바뀌면 Mod의 `LoaderVersion`이 올라가며, 이때는 KSP를 끈 상태에서 Mod를 배포하고 KSP를 한 번 재시작해야 합니다(인터페이스가 바뀌면 Logic도 함께 배포. 기체 설계 기능 추가 때 `ILogic.Status`가 생겨 `LoaderVersion` 0.0.2). 그 뒤 Logic만 바뀌면 `reload_mod`로 충분합니다.
KSP 실행 중에는 `Plugins\KspBridge.Mod.dll`이 잠겨 있을 수 있습니다. 그때 Mod를 바꾸려면 KSP를 끄고 빌드합니다.

### Mod 설정 (`settings.cfg`)

```
KSPBRIDGE
{
	port = 8085
}
```

- `port`: HTTP 서버 포트

## KSP 실행 (모니터·해상도)

모니터는 실행 인자로, 해상도·전체 화면은 KSP 설정 파일로 정합니다.

### 모니터: 실행 인자 `-monitor N`

`N`은 Unity가 매기는 1부터 시작하는 모니터 번호입니다. Windows 디스플레이 설정의 번호와 다를 수 있으니 한 번 띄워 확인합니다. Steam은 라이브러리 → KSP 속성 → 일반 → 시작 옵션에 적고, 직접 실행할 때는 인자로 넘깁니다.

```powershell
& "<KspDir>\KSP_x64.exe" -monitor 2
```

### 해상도·전체 화면: `<KspDir>\settings.cfg`

```
SCREEN_RESOLUTION_WIDTH = 2560
SCREEN_RESOLUTION_HEIGHT = 1440
FULLSCREEN = True
```

- `FULLSCREEN = True`면 `-monitor`로 고른 모니터를 채우고, `False`면 창 모드입니다.
- KSP를 끈 상태에서 고칩니다.
- 이 파일은 Mod 설정(`GameData\KspBridge\settings.cfg`)과 다른 파일입니다.

### 실행 인자 해상도가 유지되지 않는 이유

Unity 실행 인자 `-screen-width`, `-screen-height`, `-screen-fullscreen`은 첫 화면에만 쓰입니다. KSP 1.12.5(Unity 2019.4.18)는 시작 직후(`GameSettings.Start()`에서 10프레임 뒤) `settings.cfg` 값으로 `Screen.SetResolution(SCREEN_RESOLUTION_WIDTH, SCREEN_RESOLUTION_HEIGHT, FULLSCREEN)`을 다시 호출합니다.

Unity는 마지막 실행의 모니터·크기를 레지스트리 `HKCU\Software\Squad\Kerbal Space Program`(`UnitySelectMonitor_h…`, `Screenmanager Resolution Width_h…` 등)에 저장해 다음 첫 화면에 씁니다. 그래서 보통은 `-monitor`만 넘기면 됩니다. `settings.cfg`를 손으로 고친 직후 첫 화면부터 같은 크기로 띄우려면 같은 값을 실행 인자로 함께 넘깁니다. `-screen-fullscreen`은 `FULLSCREEN = True`면 `1`, `False`면 `0`입니다.

```
-monitor 2 -screen-width 2560 -screen-height 1440 -screen-fullscreen 1
```

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
| 상태 | `get_status` (씬, 활성 기체, 에디터 상태 `editor.ready`, 비행 요약 `flight`, 입력 잠금, 리셋·회수 상태 `reset`, 마지막 병진 펄스 `translate`) | Tools |
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
| 리셋·회수 | `revert_flight`, `leave_flight`, `recover_vessel` | Tools |
| SAS·RCS | `set_sas`, `set_rcs`, `rcs_translate` | Tools |

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
| `land` | 하강 중인 기체를 엔진 감속으로 착륙(동력 착륙). 지면 고도 = radar 고도 − 기체 바닥까지 거리. 최대 추력 × `margin`(0.8)으로 멈출 거리가 지면에 닿고 자세가 목표와 15° 이내면 연소 시작(여유가 없거나 `final_altitude`(20 m) 아래면 자세와 관계없이 시작). `final_altitude` 아래에서는 `final_speed`(2 m/s)로 하강. 자세는 수평 속도 1 m/s당 5°까지 수직에서 surface retrograde 쪽으로 기움. `gear_altitude`(500 m, `false`면 끔) 아래에서 Gear group 켬. 시간 가속은 하지 않으므로 긴 낙하는 앞 단계 `coast`로 | 접지 뒤 스로틀 0, 지표 속도 0.5 m/s·각속도 0.05 rad/s 미만이 2초 이어지면 완료 |

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

준궤도로 올라갔다가 내려와 착륙하는 예입니다. KSC 시설은 발사대 서쪽에 있어 북쪽으로 띄웠습니다(radar 고도는 지형 기준이라 건물 높이를 모릅니다).

```json
{
  "steps": [
    { "name": "상승", "mode": "attitude", "pitch": 75, "heading": 0, "throttle": 1, "on_start": ["stage"],
      "until": { "var": "apoapsis", "op": ">=", "value": 3000 } },
    { "name": "낙하", "mode": "coast", "reference": "none", "warp": false,
      "until": { "var": "vertical_speed", "op": "<", "value": -5 } },
    { "name": "착륙", "mode": "land" }
  ]
}
```

- **착륙 이벤트**(`land`): `landing_infeasible`(`reason` `accel`: 최대 추력으로도 못 멈춤, `dv`: 남은 Δv 부족. `a_max_basis` `best`는 최선 자세 기준, `actual`은 실제 기체 축 기준. 단계마다 이유별 1회. 판정이 참인 연소 중 틱은 스로틀 1), `touchdown`(처음 접지한 순간의 `vertical_speed`·`horizontal_speed`), `landed`(완료 때 `tilt_deg`, 45° 넘으면 `tipped: true`). 부품 손상은 아래 부품 진단 이벤트로 확인합니다.
- 착륙 다리가 엔진 노즐보다 짧으면 노즐로 접지해 넘어질 수 있습니다. 다리를 노즐보다 아래까지 닿게 붙이세요(실측 기체는 LT-2를 연료통 맨 아래에 붙임).

- 조건(`until`, `abort`)은 `{var, op, value, for_s?}`, `{all: [...]}`, `{any: [...]}`, 배열 `[...]`(= `all`)입니다. `op`는 `<`, `<=`, `>`, `>=`, `==`, `!=`이고, 문자열 변수(`situation`, `staging`)는 `==`, `!=`만 됩니다. 값을 모르는(null) 변수는 거짓입니다. 예: `{"var": "apoapsis", "op": ">=", "value": 150000}`.
- **자동 스테이징**(`auto_stage: true`): 연소 종료 → 잔여 추력 감쇠(tailoff) → 분리 → 이격(settle) → 점화를 스스로 합니다. 연료가 남은 엔진의 flameout(공기 부족 포함)은 연소 종료로 보지 않고 분리하지 않습니다. 작동 엔진이 없으면 스테이징 상태 `paused`(`flameout_pause`)로 스로틀을 유지한 채 KSP가 엔진을 다시 켜길 기다리고(`flameout_resumed`), 추력을 원하는 동안 `max_flameout_pause_s`가 지나면 `flameout_timeout`을 남기고 `no_thrust`로 중단합니다. 병렬 부스터는 코어를 계속 태우고, 동압이 `max_separate_q` 이하가 될 때까지 분리를 미룹니다(`separation_held`, 대기가 `max_separate_q_wait_s`를 넘기면 그대로 분리하고 `separation_forced`). 높은 동압에서 떼어 낸 부스터가 코어와 부딪히는 것을 막습니다. 직렬 단은 스로틀을 0으로 두고 분리한 뒤 이격 거리만큼 떨어지면 점화합니다. 다 쓴 엔진을 떼어낼 수 없거나 남는 쪽에 엔진이 없으면 스테이지를 쓰지 않고 `out_of_stages`를 남깁니다. 점화할 다음 스테이지에 엔진이 없으면(페어링·낙하산 등) 그 스테이지를 쓰지 않고 `ignite_blocked`, 스테이지 작동이 확인되지 않으면 `stage_failed`입니다. 이 셋 뒤에 Mode가 추력을 원하면 `no_thrust`로 중단하고, 다시 시작하려면 `on_start: ["stage"]`가 필요합니다. 분리 뒤 조종 부품이 떨어져 나갈 구성은 제출 때 400 `invalid_plan`(메시지에 `control_part_jettisoned`)으로 거부합니다.
- **스테이징 설정**(`staging_options`): `tailoff_ratio` 0.05, `max_tailoff_s` 5, `settle_distance` 2 m, `max_settle_s` 3, `max_separate_q` 25 kPa, `max_separate_q_wait_s` 30, `max_flameout_pause_s` 3(연료가 남은 엔진의 flameout을 기다리는 상한).
- **추력 없음**: Mode가 스로틀 > 0을 원하는데 작동 중인 엔진이 없고 기다릴 스테이징도 없을 때입니다(엔진 없는 `coast`는 해당 없음). 제출과 `replace_flight_steps`(현재 단계 포함)는 409 `no_thrust`, 실행 중이면 `plan_aborted {reason: no_thrust}`입니다.
- **RCS 사용**(모든 단계 공통 키 `rcs`): `true`·`false`면 그 단계가 시작될 때 RCS 그룹을 그 값으로 맞춥니다. 켜 두면 RCS 토크가 자세 제어에 더해집니다(진공에서 휠 토크가 작은 기체에 유용). 계획이 RCS를 바꾼 뒤 `rcs`를 생략한 단계가 시작되면 계획 전 상태로 되돌리고, 계획이 끝날 때도 되돌립니다. 단계 중에는 강제하지 않으므로 그 사이 키로 바꾼 RCS는 다음 단계 시작이나 계획 종료 때 다시 맞춰집니다. 스로틀 연동(`useThrottle`)이 켜진 RCS 부품은 RCS가 켜져 있으면 추진 중 스로틀에 따라 분사합니다(스톡 동작).
- 실행 중인 계획의 뒤 단계(또는 현재 단계부터)는 `replace_flight_steps`로 바꿉니다. `abort_flight`는 스로틀 0, 시간 가속 해제(사용자가 건 가속 포함), SAS를 계획 전 상태와 모드로, RCS를 계획이 바꿨으면 계획 전 상태로 되돌리고 제어를 놓습니다(`plan_aborted {reason: user}`, 계획이 없으면 409 `no_plan`).
- **중단 reason**: `condition`(abort 조건), `timeout`, `user`, `no_thrust`, `error`, `scene`(씬 전환), `vessel_changed`(활성 기체 변경), `vessel_lost`(기체 파괴), `reload_incompatible`, `revert`(`revert_flight`), `recover`(`recover_vessel`), `leave`(`leave_flight`).
- **부품 진단 이벤트**: 계획 실행 여부와 관계없이, 로드된 기체(떨어져 나간 잔해 포함) 부품의 소멸과 원인을 `get_flight_events`와 KSP.log(`[KspBridge] part_...` 줄)에 같은 내용으로 남깁니다. `active`는 활성 기체 소속인지이고, `step`은 계획이 실행 중일 때 그 단계 번호, 아니면 -1입니다. 부품 이벤트는 계획 이벤트와 따로 보관(각 500건)하고 같은 `seq`로 이어 붙여 돌려주므로, 기체가 크게 부서져도 계획 이벤트가 밀려나지 않습니다. 오래된 이벤트가 밀려나면 `truncated`가 true입니다.
  - `part_lost`: 부품이 사라지기 직전 상태(기체 동압 `q_kpa`, `g`, 온도·한계). 회수·잔해 정리·EVA 탑승처럼 기체째 없어지는 경우는 남지 않습니다.
  - `part_collision`: 충돌(`kind` `part`·`crash`·`splashdown`, 상대 `other`, 상대 속도 `speed` m/s, `crash_tolerance`). 충돌로 부서진 부품은 `part_lost` 바로 뒤에 옵니다(KSP가 부품을 터뜨린 뒤 충돌 이벤트를 보냄).
  - `part_limit`: 과열·과압·과G(`kind` `overheat`·`pressure`·`g`, `msg` "현재 / 한계").
  - `part_joint_break`: 하중으로 접합부가 끊김(`parent`, 끊은 힘 `force`). 디커플러·도킹 해제 같은 정상 분리는 남지 않습니다.
  - KSP 자체 로그는 충돌 내용을 `VERBOSE_DEBUG_LOG`가 켜져 있을 때만 남기므로, 원인 확인은 이 이벤트로 합니다.

### 리셋·회수

비행을 되돌리거나 기체를 치워 "발사 → 실패 → 되돌리기 → 재시도"를 MCP 도구만으로 반복합니다. 모두 게임 메뉴·버튼과 같은 조건을 따르고, 실행 중인 계획은 허용될 때만 먼저 중단합니다(reason `revert`·`leave`·`recover`).

| 도구 | 하는 일 | 씬 |
| --- | --- | --- |
| `revert_flight` | `target: launch`는 발사 직후로, `editor`는 기체를 만든 VAB/SPH로 되돌림(일시정지 메뉴의 Revert) | 비행 |
| `leave_flight` | 기체를 그 자리에 두고 우주센터로 나감(Space Center 버튼) | 비행 |
| `recover_vessel` | 비행 씬: 착지·착수한 활성 기체를 우주센터로 옮겨 회수(회수 버튼). 우주센터·조립동: `site`에 정확히 착지한 기체 회수 | 비행, 우주센터, 조립동 |
| `launch_craft` `clear_site: true` | 발사 지점의 기체를 먼저 회수하고 발사 | 조립동 |

- 회수는 게임의 회수 결과 창을 띄우지 않습니다. 결과(`vessel`, `id`, `crew`, 자금·과학·평판의 회수 전후 차이, 샌드박스는 null)는 응답 `recovered`나, 비행 씬 회수면 `get_status.reset.recovery`(`pending` → `done`/`failed`)에 있습니다.
- 씬을 바꾸는 도구는 바로 `{accepted}`를 돌려줍니다. `get_status.reset.requested_scene`이 목적 씬이고 `reset.scene_ready`가 true가 될 때까지 기다립니다(`scene`은 로드 요청 시점에 바뀌고, 비행 씬은 첫 저장 뒤에 준비됩니다). 로드 중에는 409 `scene_loading`, 비행 씬 회수를 기다리는 중에는 409 `recovery_pending`입니다.
- 미리 확인: `reset.can_revert_launch`, `can_revert_editor`(`revert_editor_facility`), `revert_unavailable`(이유), `can_recover`. 발사 직후처럼 기체가 움직이는 동안은 `leave_unavailable`이 날 수 있습니다.
- 설계는 [비행리셋 설계 문서](docs/ksp-bridge/0011-비행리셋-설계.md), 단위별 구현과 실측은 [비행리셋 개발 문서](docs/ksp-bridge/0012-비행리셋-개발.md)에 있습니다.

### SAS·RCS

비행 계획 밖에서 스톡 SAS·RCS를 다룹니다. 계획이 실행 중이면 세 도구 모두 409 `plan_running`입니다(계획은 SAS를 끄고, RCS는 단계 키 `rcs`로 씁니다). 상태는 `get_flight.controls`(`sas`, `rcs`, `translate`)와 `get_status.flight`(`sas`, `rcs`, `translating`)에 있습니다.

| 도구 | 하는 일 |
| --- | --- |
| `set_sas` | SAS 켜기·끄기와 모드(`stability`, `prograde`, `retrograde`, `normal`, `antinormal`, `radial_in`, `radial_out`, `target`, `anti_target`, `maneuver`). `reference`(`orbit`, `surface`, `target`)로 navball 속도 표시를 먼저 맞춥니다 |
| `set_rcs` | RCS 액션 그룹 켜기·끄기. 응답에 RCS 부품 수와 MonoPropellant 잔량 |
| `rcs_translate` | 조종 부품 기준 병진 펄스: `x` 오른쪽, `y` 위(등 쪽), `z` 기수 방향, 각각 -1~1(0이 아니면 절댓값 0.05 이상), `seconds` 0 초과 30 이하(게임 시간) |

- **SAS 모드**: 사용 가능 여부는 게임의 판정(`CanSetMode`)만 따릅니다. 쓸 수 없으면 409 `sas_mode_unavailable`(메시지에 지금 고를 수 있는 모드), SAS 자체가 없으면 `sas_unavailable`입니다. 모드의 기준 프레임은 navball 속도 표시 모드(`speed_mode`)를 따르고, 게임이 고도·목표 접근에 따라 표시 모드를 스스로 바꿀 수 있으므로 `reference`는 유지가 보장되지 않습니다. 오류가 나면 SAS와 속도 표시는 바뀌지 않습니다.
- **액션 그룹**: 그룹 쿨다운이나 추가 액션 그룹 오버라이드로 바꿀 수 없으면 409 `group_locked`입니다. EVA는 `eva_not_supported`, 조종할 수 없는 기체는 `not_controllable`입니다.
- **병진 펄스**: RCS가 꺼져 있으면 409 `rcs_off`(먼저 `set_rcs`), 레일 가속 중이면 `on_rails`입니다. 새 펄스는 진행 중인 펄스를 대신하고, 펄스 중 `submit_flight_plan`이 오면 펄스를 멈춥니다. `controls.translate.dv_local`은 펄스 동안의 비중력 속도 변화(m/s, 같은 축)이고, 착지·대기·프레임 변경이 섞이면 `dv_note`(`landed`, `atmosphere`, `frame_changed`)로 알립니다. 종료 사유 `ended`: `done`, `replaced`, `plan`, `scene`, `vessel_changed`, `vessel_lost`, `revert`, `leave`, `recover`, `on_rails`, `rcs_off`, `not_controllable`, `docked`, `error`, `logic_stop`. 우주센터로 간 뒤에도 `get_status.translate`로 확인합니다. 펄스는 `reload_mod`에서 넘기지 않고 멈춥니다(`released`에 "rcs pulse stopped").
- **알려진 제약**: 계획 중 `reload_mod`의 새 로직과 이전 로직이 모두 시작하지 못하면, 로더는 스로틀을 0으로 두고, 계획 기체가 로드돼 있을 때만 그 기체의 SAS 켜짐 여부와 계획이 바꾼 RCS를 복원합니다. SAS 모드는 복원하지 않습니다. 계획 중 KSP를 종료하면 스로틀·SAS·RCS를 복원하지 않습니다. 이 Logic보다 이전 Logic으로 되돌리면 단계 키 `rcs`가 있는 계획은 이어받지 못하고(`reload_incompatible`), 이때 계획이 바꾼 RCS는 복원되지 않습니다. 추가 액션 그룹 오버라이드가 RCS를 잡고 있으면 계획은 RCS를 바꾸거나 복원하지 않습니다. `set_sas` 오류 뒤 SAS 그룹을 되돌리지 못한 드문 경우(쿨다운)는 오류 메시지에 "SAS group left ..."로 알립니다.
- 설계는 [SASRCS 설계 문서](docs/ksp-bridge/0014-SASRCS-설계.md), 단위별 구현과 실측은 [SASRCS 개발 문서](docs/ksp-bridge/0015-SASRCS-개발.md)에 있습니다.

**reload 동작**: 계획 실행 중 `reload_mod`를 해도 계획이 끊기지 않습니다. 이전 Logic이 계획·이벤트·Mode 상태·스테이징 진행·직전 명령·SAS(상태·모드)·RCS(계획 전 상태와 변경 여부)·자세 제어 적분값을 넘기고, 새 Logic이 계획을 다시 검증해 같은 단계에서 이어받습니다(`logic_reloaded {resumed: true}`, `plan.id`·`epoch` 유지, `seq` 이어짐). 계획이 건 시간 가속은 넘기기 전에 풀고(`warp_stopped`), 단계가 계속 가속을 원하면 새 Logic이 다시 겁니다(사용자가 취소한 가속은 그 단계에서 다시 걸지 않음). 이어받을 수 없으면(새 검증 규칙에서 실패, 활성 기체가 다름 등) `plan_aborted {reason: reload_incompatible}`로 중단합니다. 활성 기체가 계획의 기체면 스로틀 0과 SAS 복원을 하고, 다른 기체면 입력 스로틀만 0으로 둡니다. 이어받았는데 추력만 없으면 `no_thrust`로 중단합니다. 계획이 없으면 사용자의 스로틀·SAS·가속을 건드리지 않습니다. 비행 제어 설계는 [비행제어 설계 문서](docs/ksp-bridge/0005-비행제어-설계.md), 단위별 구현 결정은 [비행제어 개발 문서](docs/ksp-bridge/0006-비행제어-개발.md)에 있습니다.

### 디버그 도구

평소에는 재현하기 어려운 경로(스테이징 지연, 엔진 정지, add_part 대기 초과, 입력 잠금, reload 실패 등)를 검증하려고 일부러 고장을 넣는 도구입니다. 기본은 꺼져 있고, 꺼져 있으면 모든 디버그 도구가 409 `debug_disabled`이며 다른 도구 동작은 바뀌지 않습니다.

켜는 법: `<KspDir>\GameData\KspBridge\debug.cfg`를 아래 내용으로 만들고 `reload_mod`를 합니다. 빌드는 이 파일을 배포하지 않으므로 다시 빌드해도 유지됩니다. 끄려면 파일을 지우거나 `enabled = false`로 바꾸고 `reload_mod`를 합니다.

```
KSPBRIDGE_DEBUG
{
    enabled = true
}
```

상태는 `get_status.debug`에 있습니다(꺼져 있으면 `null`). `debug_clear`, 씬 전환, `reload_mod`가 디버그 상태를 모두 풉니다. 결과 로그는 KSP.log가 몇 분씩 늦게 기록되므로 즉시 기록되는 `%USERPROFILE%\AppData\LocalLow\Squad\Kerbal Space Program\Player.log`에서 확인합니다.

| 도구 | 하는 일 |
| --- | --- |
| `debug_clear` | 디버그 상태를 모두 풀고 `get_status.debug`를 돌려줌 |
| `debug_input_lock` | 게임 입력 잠금 `kspbridge_debug`를 `mask`(hex 문자열, 예 `"0x8"`, `"0x0"` 허용)로 걸거나, `mask` null로 풂. `get_status.input_locks`에 그대로 보임 |
| `debug_input_locks_error` | `on` true면 입력 잠금 목록 읽기를 실패시킴: `get_status.input_locks` null과 `input_locks_error` 문자열, 다른 항목은 그대로 |
| `debug_stage_delay` | 비행 계획의 스테이지 확인(`staged`)을 첫 요청 뒤 `seconds`초(게임 시간)까지 미룸(0~60). 이미 작동한 스테이지는 다시 요청하지 않고, 실패 판정 기한도 그만큼 늘어남. `0`이면 해제. `reload_mod`로 넘기지 않음 |
| `debug_engine_stop` | 활성 기체의 엔진 하나를 바로 정지: `mode` `shutdown`(점화 꺼짐) 또는 `flameout`(점화 상태로 연소 정지). 응답 `{part_id, ignited, flameout}`. `debug_clear`로 되돌리지 않음 |
| `debug_throttle_hold` | 엔진 하나의 자체 스로틀(`currentThrottle`)을 물리 틱마다 `throttle`(0~1)로 고정해 계획의 스로틀을 무시하게 함. 실행 중인 비행 계획이 있어야 하고(없으면 409 `no_plan`), 계획이 끝나거나 중단되면 해제. `throttle` null이면 해제. 부품이 활성 기체를 떠나도 해제 |
| `debug_reload_fault` | 다음 `reload_mod` 한 번을 실패시킴. `start_failures` 1: 새 Logic 시작 실패(로더가 이전 Logic 재시작), 2: 이전 Logic 재시작도 실패(이중 실패, `get_status.logic_error`). `handover_failures` 1~3: 핸드오버 직렬화를 그 단계까지 실패(1: 계획 중단·이벤트 유지, 2: 이벤트 커서만, 3: 아무것도 넘기지 않음·epoch 변경). `start_failures` ≥ 1과 `handover_failures` 3은 함께 쓸 수 없음. 둘 다 0이면 해제 |
| `debug_add_part_timeout` | `on` true면 부모에 붙이는 `add_part`가 메인 스레드 무응답처럼 `timeout`으로 실패. 만들어진 부품은 나중에 치워지고 Player.log에 `discarded N loose part(s) after a timeout`. 루트 배치는 영향 없음 |

- 설계는 [디버그도구 설계 문서](docs/ksp-bridge/0019-디버그도구-설계.md), 단위별 구현과 실측은 [디버그도구 개발 문서](docs/ksp-bridge/0020-디버그도구-개발.md)에 있습니다.

## 테스트

```bash
dotnet test tests/KspBridge.App.Tests
```

KSP 경로가 필요 없습니다. stdio 통합 테스트는 빌드된 App 실행 파일을 임시 폴더에 복사해 실행합니다.
게임 동작은 KSP를 실행한 상태에서 MCP 도구로 검증합니다.

## 문서

기능별 방안·설계·개발 문서는 [docs/ksp-bridge](docs/ksp-bridge)에 있습니다.
