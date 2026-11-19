# MCP·Mod 포트 변경

기능: bridge-connection(기준 spec 없음, 이번에 만듦)

## 무엇을, 왜

MCP 서버의 기본 HTTP 포트를 8651에서 65531로, KSP Mod의 기본 HTTP 포트를 8650에서 65530으로 바꾼다. 사용자 요청이다.

포트 값이 들어 있는 곳은 다음과 같다(2026-11-18 기준점 e14c14f에서 검색).

- `src/Shared/Protocol.cs`: `DefaultModPort`(8650), `DefaultServerPort`(8651). `DefaultModPort`는 Loader가 settings.cfg를 읽지 못했을 때의 대체값이다(`BridgeLoader.ReadPort`). `DefaultServerPort`는 참조하는 코드가 없다.
- `src/KspBridge.Mod.Loader/settings.cfg`: Mod 포트(`port = 8650`)
- `scripts/deploy.ps1`: 서버 설정 `kspbridge.yaml`을 처음 만들 때 쓰는 `Port`(8651)·`ModUrl`(`http://127.0.0.1:8650`)
- `src/KspBridge.Server/AppConfiguration.cs`: `ModUrl` 설명 주석의 예시 주소
- `ReadMe.md`: 구성도, settings.cfg 설명, 클라이언트 등록 예시
- `tests/KspBridge.Server.Tests/ExitCodeTests.cs`: 시험용 설정 문자열의 `ModUrl`(호스트가 example.com이라 실제 Mod 포트와 무관한 값)

서버 포트는 `kspbridge.yaml`의 필수 값(`AppConfiguration.Port`, `required: true`)이라, 서버의 실제 기본 포트는 `deploy.ps1`이 처음 만드는 yaml 값이 정한다. Mod의 실제 기본 포트는 배포되는 `settings.cfg` 값이 정하고, 읽기 실패 때만 `DefaultModPort`를 쓴다. `DefaultServerPort`는 동작에 영향이 없지만 값을 통일하려고 함께 바꾼다.

끝난 작업의 변경 폴더(`docs/specs/골격/` 등)는 기록이므로 고치지 않는다.

이미 배포된 설정 파일(게임 폴더의 `GameData/KspBridge/settings.cfg`, 서버 폴더의 `kspbridge.yaml`)과 MCP 클라이언트 등록(`claude mcp add ...`)은 workspace 밖이고 배포 스크립트가 덮어쓰지 않으므로 이 작업에서 바꾸지 않는다. 사용자가 직접 맞추도록 완료 보고에서 안내한다.

## 완료 조건

- C1. `Protocol.DefaultModPort`(settings.cfg 읽기 실패 때의 Mod 대체 포트)가 65530이고, 값 통일을 위해 `Protocol.DefaultServerPort`(참조 없음)가 65531이다.
- C2. 저장소의 Mod 기본 설정 `src/KspBridge.Mod.Loader/settings.cfg`의 `port`가 65530이다.
- C3. `scripts/deploy.ps1`이 `kspbridge.yaml`을 새로 만들 때 `Port: 65531`, `ModUrl: http://127.0.0.1:65530`을 쓴다.
- C4. workspace의 파일(bin·obj 제외)에 현재 포트로서의 8650·8651 표기가 남지 않는다(`ReadMe.md`, `AppConfiguration.cs` 주석 포함). 예외: 끝난 변경 폴더(`docs/specs/` 아래 이번 작업 외 폴더), 변경 전 값을 적는 이번 변경 폴더(`docs/specs/포트-변경/`)와 기준 spec 이력 줄, 포트와 무관한 시험 코드의 example.com 주소, 이 작업과 무관하게 스테이징돼 있던 `docs/specs/조립-편집/`.
- C5. 솔루션이 빌드되고 기존 테스트가 모두 통과한다.
- C6. 기준 spec `docs/features/bridge-connection.md`에 두 기본 포트 요구(무엇이 그 값을 정하는지 포함)와 이번 이력이 있다.
- C7. 완료 보고에 사용자가 직접 맞출 곳 세 가지와 새 값을 안내한다: 게임 폴더 `GameData/KspBridge/settings.cfg`의 `port = 65530`, 서버 폴더 `kspbridge.yaml`의 `Port: 65531`·`ModUrl: http://127.0.0.1:65530`, MCP 클라이언트 등록 주소 `http://127.0.0.1:65531/mcp`.
