# KspBridge

Kerbal Space Program 1(KSP 1.12.5)과 MCP(Model Context Protocol) 클라이언트를 잇는 브리지의 빈 골격입니다.
지금은 Logic DLL을 KSP 재시작 없이 교체하는 기능만 있고, 게임 기능(읽기·조작·발사 중계)은 모두 [backup](backup)으로 옮겼습니다.

## 구성

```
MCP 클라이언트 ──stdio──▶ KspBridge.App ──HTTP──▶ KspBridge.Mod (KSP 플러그인) ──▶ KspBridge.Logic
                         (MCP 서버, net10.0)   127.0.0.1:8085   (로더·HTTP 서버, net472)     (핸들러, net472)
```

| 프로젝트 | 대상 | 역할 |
| --- | --- | --- |
| `src/KspBridge.App` | net10.0 | MCP stdio 서버. 도구 호출을 Mod의 HTTP API로 전달 |
| `src/KspBridge.Mod` | net472 | KSP 애드온. HTTP 서버, 메인 스레드 디스패처, Logic DLL 로더 |
| `src/KspBridge.Logic` | net472 | 교체 가능한 로직(현재 라우트 없음). `reload_mod`로 KSP 재시작 없이 교체 |
| `tests/KspBridge.App.Tests` | net10.0 | App 도구 → HTTP 매핑 단위 테스트(MSTest) |

KSP는 Unity 2019.4 Mono에서 동작하므로 Mod·Logic은 .NET Framework 4.7.2를 대상으로 합니다.
Logic은 같은 이름의 어셈블리를 다시 로드하지 못하는 Mono 특성 때문에 빌드마다 고유한 어셈블리 이름(`KspBridge.Logic.yyyyMMddHHmmss`)을 쓰고, 배포 파일명은 `KspBridge.Logic.dll`로 고정합니다.

## 요구 사항

- KSP 1.12.5 (기본 경로: `F:\SteamLibrary\steamapps\common\Kerbal Space Program`)
- .NET 10 SDK

## 빌드와 배포

```bash
dotnet build KspBridge.slnx
```

KSP 경로가 기본값과 다르면 `KspDir`를 지정합니다.

```bash
dotnet build KspBridge.slnx -p:KspDir="D:\Games\Kerbal Space Program"
```

`KspDir\GameData`가 있으면 빌드 후 자동으로 배포됩니다.

| 파일 | 배포 위치 | 반영 방법 |
| --- | --- | --- |
| `KspBridge.Mod.dll` | `GameData\KspBridge\Plugins` | KSP 재시작 |
| `settings.cfg` | `GameData\KspBridge` | KSP 재시작 |
| `KspBridge.Logic.dll` | `GameData\KspBridge\PluginData` | `reload_mod` 도구 호출(KSP 재시작 불필요) |

`PluginData`에 두기 때문에 KSP가 Logic을 자동 로드하지 않고, Mod의 로더가 로드합니다.

### Mod 설정 (`settings.cfg`)

```
KSPBRIDGE
{
	port = 8085
}
```

- `port`: HTTP 서버 포트

## MCP 서버 실행

### 설정 파일 (YAML)

```yaml
ContentRootPath: D:\Local\KSP\KSP1
BaseUrl: http://127.0.0.1:8085
TimeoutSeconds: 30
```

| 키 | 설명 |
| --- | --- |
| `ContentRootPath` | 호스트 콘텐츠 루트 경로 |
| `BaseUrl` | KspBridge Mod HTTP 주소 |
| `TimeoutSeconds` | 요청 기본 제한 시간(초) |

### 실행

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

### Claude Code 연결

`tools/start-mcp.cmd`가 `.mcp-bin\KspBridge.App.exe`를 실행합니다. 실행 중인 서버가 파일을 잠그므로, 새 빌드는 `.mcp-bin-next`에 publish하고 다음 서버 시작 때 스크립트가 `.mcp-bin`과 교체합니다(이전 빌드는 `.mcp-bin-old`).

```bash
dotnet publish src/KspBridge.App -o .mcp-bin-next
```

`.mcp.json` 예시(로컬 경로는 환경에 맞게 변경, 저장소에는 커밋하지 않음):

```json
{
  "mcpServers": {
    "kspbridge": {
      "command": "<저장소>\\tools\\start-mcp.cmd",
      "args": ["--config", "<설정 파일>", "--log", "<로그 디렉터리>"]
    }
  }
}
```

## MCP 도구

런타임 교체 도구만 있습니다. 게임 기능 도구는 모두 제거했고, 소스는 [backup](backup)에 남겨 두었습니다.

| 분류 | 도구 |
| --- | --- |
| 상태·리로드 | `get_status`, `reload_mod` |

## 백업

[backup](backup)에는 기능을 제거하기 직전의 `src`, `tests`, `ReadMe.md` 전체가 원래 경로 구조 그대로 들어 있습니다(발사 중계는 자동 점화가 있던 원래 버전). 솔루션(`KspBridge.slnx`)에 포함되지 않으므로 빌드되지 않습니다.

## 테스트

```bash
dotnet test tests/KspBridge.App.Tests
```

게임 동작은 KSP를 실행한 상태에서 MCP 도구로 검증합니다.

## 문서

기능별 방안·설계·개발 문서는 [docs/ksp-bridge](docs/ksp-bridge)에 있습니다.
