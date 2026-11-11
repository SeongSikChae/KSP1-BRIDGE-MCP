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
| 상태 | `get_status` (씬, 활성 기체, 에디터 상태 `editor.ready`) | Tools |
| 리로드 | `reload_mod` | Tools |
| 리로드 | `reload_app` | 셸 |
| 에디터 | `open_editor`, `new_craft`, `save_craft` | Tools |
| 조회 | `list_parts`, `get_part`, `list_crafts`, `get_craft` | Tools |
| 편집 | `add_part`, `remove_part`, `offset_part`, `rotate_part`, `set_part_field`, `set_resource` | Tools |
| 스테이징 | `set_stage` | Tools |
| 검증 | `check_craft`, `render_craft` | Tools |
| 발사 | `launch_craft` | Tools |

기체 설계 도구는 VAB·SPH 에디터에서 동작합니다(`list_parts`·`get_part`·`list_crafts`는 세이브만 불러와 있으면 어느 씬에서나 됩니다).
`open_editor`·`new_craft`·`launch_craft`는 바로 `{accepted}`를 돌려주므로 `get_status`로 완료(`editor.ready` 또는 `scene=FLIGHT`)를 확인합니다.
부품은 `persistentId`(`get_craft`의 `id`)로 가리키고, 좌표는 루트 부품 기준(+Y = 로켓 위·비행기 기수)입니다. 편집 도구는 기체를 바꾼 호출마다 에디터 Undo 한 단계를 남기고 응답에 관련 구조 경고를 담습니다.
설계 결정은 [기체 설계 설계 문서](docs/ksp-bridge/0002-기체설계-설계.md), 단위별 구현 결정은 [기체 설계 개발 문서](docs/ksp-bridge/0003-기체설계-개발.md)에 있습니다.

## 테스트

```bash
dotnet test tests/KspBridge.App.Tests
```

KSP 경로가 필요 없습니다. stdio 통합 테스트는 빌드된 App 실행 파일을 임시 폴더에 복사해 실행합니다.
게임 동작은 KSP를 실행한 상태에서 MCP 도구로 검증합니다.

## 문서

기능별 방안·설계·개발 문서는 [docs/ksp-bridge](docs/ksp-bridge)에 있습니다.
