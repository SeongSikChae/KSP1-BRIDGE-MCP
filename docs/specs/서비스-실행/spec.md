# MCP 서버 서비스 실행과 게시 기반 배포

기능: server-hosting(새 기능) / deployment(새 기능) / bridge-connection(기준 spec docs/features/bridge-connection.md)
이슈: GitHub #45

## 무엇을, 왜

MCP 서버(`KspBridge.Server.exe`)를 Windows 서비스로 실행할 수 있게 해 로그온하지 않아도 늘 떠 있게 한다. 사용자 요청이다(게시 폴더를 서비스로 등록). 서비스 실행 계정은 등록할 때 사용자가 정하며, 서버는 특정 계정을 전제하지 않는다.

이와 함께 배포 스크립트 `scripts/deploy.ps1`을 없애고 게시(publish)로 배포한다(사용자 결정). 서버와 KSP Mod는 이미 모듈 폴더의 `manifest.json`이 바뀌면 새 모듈 세대를 저절로 읽으므로(서버 `ManifestWatcher`, Mod Loader), 배포는 대상 폴더에 파일을 두기만 하면 된다. 실행 중인 서버를 찾아 배포하려고 둔 등록 기능(`src/KspBridge.Server/Modules/ServerRegistration.cs`, `%LOCALAPPDATA%\KspBridge\servers`)도 필요 없어지므로 없앤다.

지금 상태(2026-11-20 기준점 86dd28d에서 확인):

- `src/KspBridge.Server/Program.cs`는 서비스 제어 관리자(SCM)에 응답하는 수명 관리를 쓰지 않는다. 그래서 `sc.exe`로 등록해 시작하면 SCM이 시작 완료 신호를 받지 못해 1053 오류로 실패한다.
- 설정·호스트 생성 오류는 로거를 만들기 전에 나서 `Console.Error`로만 나간다(`Program.cs`의 `RunAsync`). 서비스에는 콘솔이 없으므로 사용자가 기동 실패 원인을 볼 수 없다.
- `deploy.ps1`은 빌드한 뒤 다음을 한다.
  - KSP Mod: Loader DLL을 `GameData/KspBridge/Plugins`에 복사하고 `settings.cfg`가 없으면 둔다. Mod 로직 어셈블리(`KspBridge.Mod.Core`, `KspBridge.Mod.Modules.*`)는 배포마다 새 세대 표식(`ModBuildStamp`)을 붙여 빌드해 `PluginData/modules`에 두고 `manifest.json`을 마지막에 쓴다. Unity Mono가 같은 이름의 이전 세대 어셈블리를 돌려주므로 이름이 배포마다 달라야 한다. Mod 모듈은 `KspBridge.Mod.Core`를 프로젝트로 참조하므로 같은 세대 표식으로 함께 빌드해야 한다.
  - 서버: `ServerDir`에 서버 호스트 파일을 복사하고 `kspbridge.yaml`이 없으면 만든다. 서버 모듈(`KspBridge.Server.Modules.*`, 고정 호스트의 `KspBridge.Server.Contracts`만 참조)을 `<ServerDir>/modules`와 등록된 실행 중 서버의 모듈 폴더에 두고 `manifest.json`을 쓴다.
- 저장소에는 서버 설정 파일 샘플이 없다. 설정 파일은 `deploy.ps1`이 처음 배포할 때만 만들었다.

서비스 등록은 한 번 하는 수동 작업이라 저장소에 등록 스크립트나 등록 명령 안내를 두지 않는다(사용자 결정). 사용자가 필요할 때 명령을 받아 관리자 PowerShell에서 직접 등록한다. 실행 계정과 그 패스워드는 사용자가 직접 정하고 입력하며 저장소 파일·에이전트가 다루지 않는다. 서비스는 현재 폴더 `C:\Windows\System32`에서 시작하므로 등록 인자(`--config`·`--log`)와 설정 파일의 `ContentRootPath`는 사용자가 절대 경로로 적는다. 서버는 경로를 검사하거나 바꾸지 않는다(사용자 결정).

콘솔 실행(`KspBridge.Server.exe --config ... --log ...`)은 지금처럼 동작해야 한다.

## 완료 조건

서비스 실행

- C1. 같은 `KspBridge.Server.exe`가 콘솔에서 실행하면 지금처럼 동작하고(Ctrl+C로 종료), 서비스로 시작하면 SCM에 시작 완료를 알려 상태가 `Running`이 되며, 서비스 중지 요청에 정상 종료한다.
- C2. 시작 단계 오류(설정 오류, 호스트 생성 오류)는 stderr와 함께 `--log` 폴더의 파일에도 남는다. 콘솔·서비스 실행 모두 같으며, 사용자는 서비스 기동 실패 원인을 그 파일에서 볼 수 있다.

게시 기반 배포

- C3. `scripts/deploy.ps1`이 저장소에서 없어진다. 저장소 파일(문서, 빌드 파일, 주석, 사용자에게 보이는 코드 문자열. 예: `src/KspBridge.Mod.Loader/GenerationLoader.cs`의 예외 메시지)에 `deploy.ps1`을 쓰라는 안내나 `deploy.ps1`을 전제한 설명이 남지 않는다(기록인 `docs/specs/` 아래 모든 변경 폴더(이번 작업 폴더 포함)와 `docs/features/*.md`의 이력 줄은 예외).
- C4. 실행 중 서버 등록 기능이 없어진다. 서버는 `%LOCALAPPDATA%\KspBridge\servers`에 등록 파일을 쓰지 않는다(`ServerRegistration` 삭제).
- C5. 서버 프로젝트(`KspBridge.Server`)를 게시하면 게시 폴더에 서버 호스트, 모든 서버 모듈과 `modules/manifest.json`(C6과 같은 규칙), 서버 설정 샘플 파일(C8, 실제 설정 파일과 다른 이름)이 들어간다. 게시 출력에는 실제 설정 파일 이름의 파일이 없어, 게시 폴더에 있는 설정 파일을 덮어쓰지 않는다. 사용자는 샘플을 복사해 설정 파일을 만든다.
- C6. 서버 모듈 프로젝트(`KspBridge.Server.Modules.*`)를 하나 게시하면, 빌드 설정(`KspBridge.user.props`)에 적은 서버 모듈 폴더(여러 개 가능)마다 게시한 모듈만이 아니라 모든 서버 모듈 파일을 함께 두고 `manifest.json`을 마지막에 갱신한다(manifest에 적힌 파일이 하나라도 없으면 서버가 그 세대 전체를 읽지 않으므로). `manifest.json`의 모듈 목록은 저장소의 서버 모듈 프로젝트 목록이며, 그 목록에 없는 dll·pdb는 그 폴더에서 지운다. 실행 중인 서버(서비스 포함)는 서버를 멈추지 않고 그 세대를 읽는다.
- C7. Mod 쪽 프로젝트(`KspBridge.Mod.Core`, `KspBridge.Mod.Modules.*`) 가운데 어느 것을 게시하든, 빌드 설정의 KSP 폴더 `GameData/KspBridge`에 새 세대 표식의 `KspBridge.Mod.Core`와 모든 `KspBridge.Mod.Modules.*`를 한 세트로(`PluginData/modules`), 마지막에 `manifest.json`을 둔다(Mod 모듈은 세대 이름으로 `KspBridge.Mod.Core`를 참조하므로 따로 게시할 수 없다). 이전 세대 파일은 지운다. 실행 중인 KSP는 그 세대를 읽는다. Loader(`KspBridge.Mod.Loader`)를 게시하면 Loader DLL을 `Plugins`에 두고, `settings.cfg`는 없을 때만 둔다.
- C8. 저장소에 서버 설정 샘플 파일이 있다. 모든 키와 설명, 기본 포트(`Port: 65531`, `ModUrl: http://127.0.0.1:65530`)를 담고, 개인화 경로 대신 자리 표시를 쓴다.
- C9. `KspBridge.user.props.example`에 게시에 쓰는 설정(KSP 폴더, 서버 모듈 폴더)의 예시와 설명이 있고, 쓰지 않게 된 설정(`ServerDir`)은 `KspBridge.user.props.example`과 `Directory.Build.props` 주석에서 없어진다.

안내·기준 spec·검증

- C10. `ReadMe.md`가 이번 변경과 맞다. 콘솔·서비스 실행(서비스 등록 때 경로 인자와 `ContentRootPath`는 절대 경로, 등록 명령은 적지 않음), 게시로 배포하는 방법(C5~C7), 서비스로 실행 중일 때 서버 호스트를 바꾸려면 서비스를 멈추고 게시해야 한다는 것, 게시 프로필에서 기존 파일 삭제 설정(`DeleteExistingFiles`)을 켜면 게시 폴더의 설정 파일·모듈·로그가 지워지므로 켜지 말아야 한다는 것을 설명하고, `deploy.ps1`·실행 중 서버 등록·수동 실행만 전제한 서술이 남지 않는다.
- C11. 솔루션이 빌드되고 기존 테스트가 모두 통과한다.
- C12. `KspBridge.user.props`의 서버 모듈 폴더에 게시 폴더의 `modules`를 적고(설정 파일의 `ContentRootPath`는 게시 폴더의 절대 경로, `ModulesDir`는 `modules`), 게시 폴더에 서버를 게시하고, 사용자가 받은 명령으로 그 폴더의 서버를 사용자가 정한 계정의 자동 시작 서비스로 등록해 시작한 뒤, 다음을 확인한다.
  - 서비스 상태가 `Running`이다.
  - `http://127.0.0.1:65531/health`가 `Healthy`를 돌려주고, MCP 클라이언트(`kspbridge`)가 연결된다.
  - 서비스가 도는 동안 서버 모듈 프로젝트 하나를 게시하면, 게시 폴더의 `modules/manifest.json`이 갱신되고, 서버 로그(`--log` 폴더)에 게시 시각 뒤의 `Loaded server module generation` 줄이 새로 생기며, 서비스는 계속 `Running`이다.
  - Mod를 게시하면 `GameData/KspBridge/PluginData/modules/manifest.json`이 새 세대로 갱신되고, KSP가 실행 중이면 MCP 응답으로 새 세대를 읽었음을 확인한다(KSP가 실행 중이 아니면 파일 갱신까지만 확인하고 그 사실을 적는다).
- C13. 기준 spec이 이번 변경과 맞다.
  - `docs/features/server-hosting.md`(새로 만듦): 콘솔·서비스 실행, 시작 단계 오류 기록 요구와 이번 이력
  - `docs/features/deployment.md`(새로 만듦): 게시 기반 배포(C5~C9) 요구와 이번 이력
  - `docs/features/bridge-connection.md`: `deploy.ps1`이 설정 파일을 만들 때 포트를 쓴다는 R2를 샘플 설정 파일 기준으로 고치고, `파일:` 줄에서 `scripts/deploy.ps1`을 빼며, 이번 이력을 더한다.
