# server-hosting

파일: src/KspBridge.Server/Program.cs, src/KspBridge.Server/ToolListSubscriptions.cs, src/KspBridge.Server/Modules/ModuleHost.cs, src/KspBridge.Server/KspBridge.Server.csproj, src/KspBridge.Server.Core/StartupErrorLog.cs, Directory.Packages.props, tests/KspBridge.Server.Tests/ExitCodeTests.cs, tests/KspBridge.Server.Tests/McpEndpointTests.cs, ReadMe.md

## 요구(완료 조건)

- R1. 같은 `KspBridge.Server.exe`가 콘솔에서 실행하면 콘솔 수명 관리로 동작하고(Ctrl+C로 종료), Windows 서비스로 시작하면 서비스 제어 관리자(SCM)에 시작 완료를 알려 상태가 `Running`이 되며, 서비스 중지 요청에 정상 종료한다. 서비스 수명 관리는 서비스로 시작했을 때만 쓰며 `ContentRootPath`를 바꾸지 않는다.
- R2. 시작 단계 오류(설정 오류, 호스트 생성 오류)는 stderr와 함께 `--log` 폴더의 `KspBridge.Server.startup.log`에 시각·종류·원인과 함께 덧붙여 남는다. 콘솔·서비스 실행 모두 같으며, 기록에 실패해도 서버의 종료 코드는 바뀌지 않는다.
- R3. 서비스 등록은 사용자가 수동으로 하며, 저장소는 등록 스크립트나 등록 명령을 두지 않는다. 서비스 실행 계정은 사용자가 정하고, 서버는 특정 계정을 전제하지 않는다. 서비스의 현재 폴더는 `C:\Windows\System32`이므로 `--config`·`--log`와 설정 파일의 `ContentRootPath`는 사용자가 절대 경로로 적으며, 서버는 경로를 검사하거나 바꾸지 않는다. `ReadMe.md`가 이것을 안내한다.
- R4. 서버는 실행 중임을 다른 곳(프로필 폴더 등)에 등록하지 않는다. 실행 중인 서버에 모듈을 반영하려면 그 서버의 모듈 폴더에 게시한다(기준 spec `deployment`).
- R5. 서버 호스트가 계속 실행 중인 동안 서버 모듈을 게시하면, 실행 중인 Claude Code(MCP 클라이언트)를 다시 시작하거나 MCP를 다시 연결하지 않고 새 도구가 그 Claude Code의 도구 목록에 나타나 호출된다. 그 Claude Code의 기존 도구 호출은 그대로 동작한다.

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-20 | 서비스-실행 | R1·R2·R3·R4 추가(기준 spec 처음 만듦, Windows 서비스 실행·시작 오류 기록, 실행 중 서버 등록 제거) | docs/specs/서비스-실행/ |
| 2026-11-21 | 조립-점검 | R5 추가(최신 프로토콜 Stateless 전송과 subscriptions/listen 처리기로 실행 중 Claude Code에 서버 모듈 핫리로드가 닿음) | docs/specs/조립-점검/ |

표에 없는 이전 변경: 기록 없음
