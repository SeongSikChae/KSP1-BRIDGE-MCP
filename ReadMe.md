# KspBridge

Kerbal Space Program 1(KSP 1.12.5)을 MCP(Model Context Protocol)로 조작하는 브리지입니다.
Claude Code 같은 MCP 클라이언트가 커리어 진행, 기체 조립, 발사, 비행 제어, 시간 가속, 과학 실험, 기술 연구 등을 도구 호출로 수행할 수 있습니다.

## 구성

```
MCP 클라이언트 ──stdio──▶ KspBridge.App ──HTTP──▶ KspBridge.Mod (KSP 플러그인) ──▶ KspBridge.Logic
                         (MCP 서버, net10.0)   127.0.0.1:8085   (로더·HTTP 서버, net472)     (핸들러, net472)
```

| 프로젝트 | 대상 | 역할 |
| --- | --- | --- |
| `src/KspBridge.App` | net10.0 | MCP stdio 서버. 도구 호출을 Mod의 HTTP API로 전달 |
| `src/KspBridge.Mod` | net472 | KSP 애드온. HTTP 서버, 메인 스레드 디스패처, Logic DLL 로더 |
| `src/KspBridge.Logic` | net472 | 실제 게임 조작(핸들러, 자세 제어, 기체 조립, 트리거, 비행 기록). `reload_mod`로 KSP 재시작 없이 교체 |
| `tests/KspBridge.App.Tests` | net10.0 | App 도구 → HTTP 매핑 단위 테스트(MSTest) |

KSP는 Unity 2019.4 Mono에서 동작하므로 Mod·Logic은 .NET Framework 4.7.2를 대상으로 합니다.
Logic은 같은 이름의 어셈블리를 다시 로드하지 못하는 Mono 특성 때문에 빌드마다 고유한 어셈블리 이름(`KspBridge.Logic.yyyyMMddHHmmss`)을 쓰고, 배포 파일명은 `KspBridge.Logic.dll`로 고정합니다.

## 요구 사항

- KSP 1.12.5 (기본 경로: `F:\SteamLibrary\steamapps\common\Kerbal Space Program`)
- .NET 10 SDK
- 발사 중계(선택): `gfxcapture` 필터가 있는 ffmpeg(9.0.1에서 확인), Windows 10 1903 이상, Azure Speech 리소스(음성 안내용)

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
	ATTITUDE
	{
		kp = 3
		kd = 1.5
		pitch_sign = -1
		yaw_sign = -1
		roll_sign = -1
	}
}
```

- `port`: HTTP 서버 포트
- `ATTITUDE`: 자세 제어(`set_attitude`) PD 게인과 축 부호

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
| `TimeoutSeconds` | 요청 기본 제한 시간(초). `wait_until`은 별도 제한 시간 사용 |

#### 발사 중계 설정 (선택)

`Broadcast` 섹션이 있을 때만 발사 중계 도구가 동작합니다. 섹션이 없으면 도구가 `not_configured`를 반환합니다.

```yaml
Broadcast:
  FfmpegPath: C:\Program Files\ffmpeg\bin\ffmpeg.exe
  OutputDir: broadcasts
  WindowTitle: Kerbal Space Program
  FrameRate: 30
  Font: Malgun Gothic
  Speech:
    Region: southeastasia
    ApiKey: <Azure Speech 키>
    Voice: ko-KR-SunHiNeural
    Rate: +10%
OsKms:
  CredentialService: kspbridge-app://kek
  CredentialAccount: kek
  Iterations: 600000
  SecretsDekFilePath: secrets/dek.bin
```

| 키 | 설명 |
| --- | --- |
| `Broadcast.FfmpegPath` | ffmpeg 실행 파일 경로. `gfxcapture` 필터와 `-/filter_complex` 옵션이 있어야 합니다(9.0.1에서 확인). |
| `Broadcast.OutputDir` | 중계 폴더를 만들 위치. 상대 경로는 `ContentRootPath` 기준입니다(기본 `broadcasts`). |
| `Broadcast.WindowTitle` | 녹화할 KSP 창 제목(전체 일치). 녹화는 `gfxcapture`(Windows Graphics Capture)로 해서, 다른 창이 KSP를 가려도 KSP 화면만 찍힙니다. |
| `Broadcast.FrameRate` | 녹화 프레임레이트(기본 30). |
| `Broadcast.Font` | 자막 글꼴. 한글을 지원하는 글꼴이어야 합니다(기본 Malgun Gothic). |
| `Broadcast.GameAudio` | KSP 프로세스 소리를 녹음해 영상에 넣습니다(기본 true). 프로세스 루프백(Windows 10 2004 이상)이라 다른 앱 소리는 들어가지 않습니다. 녹음하지 못하면 게임 소리 없이 계속합니다. |
| `Broadcast.GameVolume` | 게임 소리 볼륨 배율(0 이상, 기본 1). 2는 +6 dB, 4는 +12 dB입니다. 상한은 없지만 너무 크게 하면 소리가 깨질 수 있습니다. 안내 음성이 나오는 동안에는 자동으로 낮아집니다(덕킹). |
| `Broadcast.DuckRatio` | 안내 음성이 나오는 동안 게임 소리를 낮추는 세기(1~20, 기본 8). 8이면 게임 소리가 약 20 dB 낮아지고, 값이 작을수록 덜 낮아집니다. 1이면 낮추지 않습니다. |
| `Broadcast.DuckThreshold` | 덕킹을 시작하는 안내 음성 크기(0.001~1, 기본 0.02). 작을수록 작은 음성에도 반응합니다. |
| `Broadcast.DuckAttack` | 안내가 시작된 뒤 게임 소리가 다 낮아질 때까지 걸리는 시간(ms, 0.01~2000, 기본 20). |
| `Broadcast.DuckRelease` | 안내가 끝난 뒤 게임 소리가 원래 크기로 돌아오는 시간(ms, 0.01~9000, 기본 400). |
| `Broadcast.GameCompress` | 폭발음 같은 큰 게임 소리를 압축합니다(기본 true). 압축한 뒤 `GameVolume`을 적용합니다. 최종 소리에는 이 설정과 관계없이 항상 -1 dB 리미터가 걸려서 소리가 잘리지 않습니다. |
| `Broadcast.CompThreshold` | 압축을 시작하는 크기(dB, -60~0, 기본 -18). |
| `Broadcast.CompRatio` | 압축 비율(1~20, 기본 4). 4이면 기준을 넘은 부분이 1/4로 줄어듭니다. |
| `Broadcast.CompAttack` | 큰 소리가 시작된 뒤 압축이 걸리는 시간(ms, 0.01~2000, 기본 5). |
| `Broadcast.CompRelease` | 큰 소리가 끝난 뒤 압축이 풀리는 시간(ms, 0.01~9000, 기본 200). |
| `Broadcast.GameProcess` | 소리를 녹음할 프로세스 이름(기본 `KSP_x64`). |
| `Broadcast.Speech.Region` | Azure Speech 리소스의 리전. 요청은 `https://{Region}.tts.speech.microsoft.com/cognitiveservices/v1`로 보냅니다. MAI 음성을 지원하는 리전이어야 합니다(eastus, eastus2, westus2, canadacentral, westeurope, francecentral, swedencentral, centralindia, southeastasia). koreacentral은 지원하지 않으므로 한국에서 가장 가까운 southeastasia를 권장합니다. |
| `Broadcast.Speech.ApiKey` | Azure Speech 키. 기동할 때 `OsKms`로 `DEKENC:` 암호문으로 바꾸고 YAML을 다시 씁니다. 이때 주석은 사라집니다. |
| `Broadcast.Speech.Voice` | SSML 음성 이름. 기본값은 `ko-KR-SunHiNeural`(일반 Neural 여성 음성)입니다. MAI 음성(`ko-KR-Harper:MAI-Voice-2.1` 등)도 지정할 수 있지만, 짧은 숫자를 읽을 때 반복하거나 오독할 때가 있어 카운트다운이 고르지 않습니다. |
| `Broadcast.Speech.Style` | 음성 스타일(`mstts:express-as`, 예: MAI Harper의 `narrator`). 비우면 쓰지 않습니다. |
| `Broadcast.Speech.Rate` | 말하기 속도(`prosody rate`, 예: `+10%`). 비우면 기본 속도입니다. |
| `OsKms.*` | `Speech`가 있으면 필수입니다. KEK 패스워드는 Windows 자격 증명 관리자에 저장합니다. `CredentialService`는 URI 형식이어야 합니다. `Salt`는 처음 기동할 때 생성해 기록합니다. |

- `Speech`가 없으면 자막만 입힌 영상을 만듭니다.
- 결과는 `{OutputDir}\{시각}-{기체}\final.mp4`에 저장합니다. 원본 녹화 `raw.mkv`와 게임 소리 `game.wav`도 함께 남깁니다.
- KSP 창을 최소화하면 녹화가 멈출 수 있습니다. 다른 창 뒤에 있는 것은 괜찮습니다.

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

| 분류 | 도구 |
| --- | --- |
| 상태·리로드 | `get_status`, `reload_mod` |
| 커리어·계약·시설 | `get_career`, `list_contracts`, `accept_contract`, `decline_contract`, `list_facilities`, `upgrade_facility` |
| 과학·기술 | `get_science`, `run_experiment`, `discard_science`, `list_tech`, `research_tech` |
| 기체·발사 | `list_parts`, `build_craft`, `list_craft`, `list_crew`, `launch_craft`, `get_stages`, `recover_vessel`, `revert_to_launch` |
| 비행 제어 | `get_vessel_state`, `set_throttle`, `activate_next_stage`, `set_sas`, `set_sas_mode`, `set_rcs`, `set_action_group`, `set_attitude`, `release_attitude`, `wait_until` |
| 기동 노드 | `list_maneuver_nodes`, `add_maneuver_node`, `remove_maneuver_node` |
| 시간 가속 | `warp_to`, `set_warp`, `stop_warp` |
| 트리거 | `add_trigger`, `list_triggers`, `remove_trigger` |
| 부품 테스트 | `list_part_tests`, `arm_part_test`, `run_part_test` |
| 대화상자 | `list_dialogs`, `click_dialog_button`, `close_experiment_dialog` |
| 비행 기록 | `list_flights`, `get_flight_log` |
| 발사 중계 | `start_broadcast`, `stop_broadcast`, `get_broadcast_status` |

발사 중계 흐름은 다음과 같습니다.
1. `start_broadcast`: KSP 창 녹화를 시작하고 카운트다운을 진행합니다. 카운트다운은 T-20 안내(20초 이상일 때), 십…일(매초 비프음), T-0 긴 비프음 순서이고, T-0에 다음 단계를 점화합니다. 이어서 "○○가 발사되었습니다."를 안내합니다.
2. 녹화 중: 이륙, 단 분리, 고도 돌파, 궤도 진입·착륙·착수, SOI 변경을 한국어 자막과 음성으로 안내합니다.
3. `stop_broadcast`: 녹화를 멈추고 백그라운드에서 합성을 시작합니다.
4. `get_broadcast_status`: 상태가 `done`이 되면 결과 MP4 경로를 확인합니다.

발사 전에는 `get_stages`로 게임이 계산한 단별 Δv·TWR을 확인하고, 목표 비행에 필요한 Δv보다 여유가 있을 때만 점화하는 것을 권장합니다.

## 테스트

```bash
dotnet test tests/KspBridge.App.Tests
```

게임 동작은 KSP를 실행한 상태에서 MCP 도구로 검증합니다.

## 문서

기능별 방안·설계·개발 문서는 [docs/ksp-bridge](docs/ksp-bridge)에 있습니다.
