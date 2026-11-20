# MCP 서버 서비스 실행과 게시 기반 배포 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 서비스 실행과 시작 오류 기록

- [x] T001 `Microsoft.Extensions.Hosting.WindowsServices` 10.0.15의 취약점·Deprecated 여부를 확인하고 버전을 더한다 (Directory.Packages.props) — C1
- [x] T002 서버 프로젝트에 `Microsoft.Extensions.Hosting.WindowsServices` 참조를 더한다 (src/KspBridge.Server/KspBridge.Server.csproj) — C1
- [x] T003 [P] 시작 오류를 `<log>/KspBridge.Server.startup.log`에 시각·종류·메시지·예외와 함께 덧붙여 쓰고 실패는 무시하는 `StartupErrorLog`를 만든다 (src/KspBridge.Server.Core/StartupErrorLog.cs) — C2
- [x] T004 `CreateWebApplication`에서 `builder.Services.AddWindowsService()`를 등록하고, `RunAsync`의 설정 오류·호스트 생성 오류에서 stderr와 함께 `StartupErrorLog`에 쓰고, `ServerRegistration` 호출과 그 주석을 지운다 (src/KspBridge.Server/Program.cs) — C1, C2, C3, C4
- [x] T005 `ServerRegistration`을 지운다 (src/KspBridge.Server/Modules/ServerRegistration.cs) — C4
- [x] T006 설정 오류로 실행하면 `<log>/KspBridge.Server.startup.log`에 원인 문구가 남는지 보는 테스트를 더한다 (tests/KspBridge.Server.Tests/ExitCodeTests.cs) — C2

체크포인트: `dotnet build KspBridge.slnx`와 `dotnet test tests/KspBridge.Server.Tests`가 통과하고, 새 테스트가 통과한다.

## 단계 2: 게시 기반 배포

- [x] T007 서버 설정 샘플을 만든다(모든 키·설명·기본 포트 `Port: 65531`, `ModUrl: http://127.0.0.1:65530`, 경로는 자리 표시) (src/KspBridge.Server/kspbridge.sample.yaml) — C8
- [x] T008 샘플을 `CopyToPublishDirectory`로 게시 출력에 넣는다 (src/KspBridge.Server/KspBridge.Server.csproj) — C5
- [x] T009 게시 대상을 만든다 (Directory.Build.targets) — C5, C6, C7
  - 인라인 작업 `PublishModuleSet`: 복사, manifest 바꿔치기 쓰기, 목록 밖 dll·pdb 삭제, Core 먼저·이름순, 파일만 생성하는 방식
  - 모듈 세트 빌드: `RemoveProperties`로 게시 전용 속성을 떼고, 사전 `Restore` 뒤 `Build`
  - 서버 모듈 프로젝트 게시: 모든 서버 모듈을 `ServerModulesDirs`마다 둔다. 비어 있으면 경고
  - 서버 프로젝트 게시: `ComputeResolvedFilesToPublishList` 뒤에 모듈 dll·pdb·manifest를 `ResolvedFileToPublish`로 더한다(`modules\...`, `ExcludeFromSingleFile`, `CopyToPublishDirectory=PreserveNewest`)
  - Mod 프로젝트 게시: `ModBuildStamp`를 만들어 Mod 세트를 빌드하고 `$(ModDeployDir)/PluginData/modules`에 둔다. `ModDeployDir` 기본값은 `$(KspDir)\GameData\KspBridge`
  - Loader 게시: `Plugins`에 DLL, 없을 때만 `$(ModDeployDir)/settings.cfg`
- [x] T010 [P] `ServerDir` 대신 `ServerModulesDirs`·`ModDeployDir` 예시와 설명을 적는다 (KspBridge.user.props.example) — C3, C9
- [x] T011 [P] 주석의 로컬 설정 이름을 새 설정으로 바꾼다 (Directory.Build.props) — C3, C9
- [x] T012 배포 스크립트를 지운다 (scripts/deploy.ps1) — C3
- [x] T013 [P] 이전 세대 어셈블리 예외 메시지를 Mod 게시 안내로 바꾼다 (src/KspBridge.Mod.Loader/GenerationLoader.cs) — C3
- [x] T014 [P] `ModBuildStamp` 주석을 게시 기준으로 바꾼다 (src/KspBridge.Mod.Core/KspBridge.Mod.Core.csproj) — C3
- [x] T015 [P] manifest를 쓰는 주체 주석을 게시 기준으로 바꾼다 (src/KspBridge.Server/Modules/ModuleManifest.cs) — C3
- [x] T016 [P] 복사 순서 주석을 게시 기준으로 바꾼다 (tests/KspBridge.Server.Tests/FixtureModule.cs) — C3

체크포인트: 스크래치 폴더에 시험 게시한다.
- 서버 단일 파일 게시와 프로필 게시: `modules`에 dll·manifest, 샘플 파일이 있고 미리 둔 설정 파일은 그대로다.
- 서버 모듈 게시를 두 폴더로: 두 폴더 모두 전체 모듈·manifest가 있고, 목록 밖 dll은 지워진다.
- Mod 게시를 `ModDeployDir`로 두 번: 같은 표식의 세트가 있고 이전 표식은 지워진다.
- Loader 게시: Loader DLL이 생기고 기존 `settings.cfg`는 그대로다.
- 일반 빌드: 아무 데도 복사하지 않는다.

## 단계 3: 안내와 기준 spec

- [x] T017 실행(콘솔·서비스, 절대 경로 안내, 등록 명령은 적지 않음), 게시 배포 방법, 서비스 중 호스트 갱신(서비스 중지 → 게시 → 시작), 게시 프로필의 기존 파일 삭제 설정 경고, 구성도·서술 정리를 적는다 (ReadMe.md) — C3, C10
- [x] T018 [P] 기준 spec을 만든다: 콘솔·서비스 실행, 시작 단계 오류 기록 요구와 이력(이력 표는 규칙 틀의 4열: 날짜·작업·바꾼 요구·변경 폴더) (docs/features/server-hosting.md) — C13
- [x] T019 [P] 기준 spec을 만든다: 게시 기반 배포(C5~C9) 요구와 이력(이력 표 4열) (docs/features/deployment.md) — C13
- [x] T020 [P] R2를 샘플 설정 기준으로 고치고, `파일:` 줄에서 스크립트를 빼고 샘플 파일을 넣고, 이력을 더한다. spec-driven 규칙("작업 문서 칸이 있는 기존 기준 spec은 다음 반영 때 그 칸을 뺀다")에 따라 이력 표의 "작업 문서" 열을 기존 행까지 통째로 빼 4열로 만든다 (docs/features/bridge-connection.md) — C13

체크포인트: 세 기준 spec과 ReadMe를 읽어 C10·C13 항목을 하나씩 대조한다. 세 이력 표가 모두 4열인지 본다.

## 단계 4: 검증

- [x] T021 `dotnet build KspBridge.slnx`, `dotnet test KspBridge.slnx`를 실행하고, 다음을 파일로 확인한다 — C3, C4, C8, C9, C10, C11
  - C3·C4·C9: plan 검증 전략의 파일 목록
  - C8: 샘플의 키가 `AppConfiguration` 속성과 같고 개인화 경로가 없다
  - C10: ReadMe를 항목별로 읽고, 수동 실행만 전제한 서술과 실행 중 서버 등록 서술이 남지 않았는지 검색해 본다
- [x] T022 스크래치 폴더에 단일 파일로 게시한 서버를, 샘플의 자리 표시만 실제 경로로 바꾼 설정으로 콘솔 실행한다. `/health`가 `Healthy`이고 Ctrl+C로 끝나는지 본다 — C1, C8
- [x] T023 로컬 설정을 맞춘다(커밋 안 함): `KspBridge.user.props`에 `ServerModulesDirs`=게시 폴더의 `modules`를 적고 `ServerDir`를 뺀다. 게시 프로필의 `PublishUrl`을 게시 폴더로, `DeleteExistingFiles`를 `false`로 바꾼다. 게시 폴더 설정 파일의 `ContentRootPath`·`ModulesDir`를 확인한다 — C12
- [x] T024 게시 폴더에 서버를 게시하고, 등록 명령을 사용자에게 알려 사용자가 직접 서비스를 등록·시작한다. `Get-Service`, `/health`, MCP 연결을 확인한다 — C1, C12
- [x] T025 서비스가 도는 동안 서버 모듈을 게시하고 manifest·`Loaded server module generation` 로그 줄·서비스 상태를 확인한다. `%LOCALAPPDATA%\KspBridge\servers`에 새 파일이 없는지 본다 — C4, C6, C12
- [x] T026 KSP 상태를 MCP로 확인한 뒤 Mod를 게시하고 manifest와 새 세대 로드를 MCP 응답으로 확인한다(KSP가 꺼져 있으면 파일까지만 확인하고 적는다) — C7, C12
- [x] T027 서비스를 중지·시작해 정상 종료·재시작을 서버 로그로 확인한다. C2의 서비스 쪽은 시작 오류 기록이 콘솔과 같은 코드 경로(`RunAsync`에서 호스트를 시작하기 전, 설정 오류와 `AddWindowsService`를 등록하는 `CreateWebApplication` 중의 호스트 생성 오류 두 경로 모두)임을 `Program.cs`로 확인하고, 확인 범위(서비스로 일부러 기동 실패를 내 보지는 않음)를 보고에 적는다 — C1, C2

체크포인트: 완료 조건별 확인 결과(명령과 출력)를 정리한다.

## 의존 관계

- 단계 1의 T001 → T002 → T004(참조가 있어야 `AddWindowsService` 빌드), T003 → T004(호출 대상), T004 → T005(호출을 지운 뒤 파일 삭제), T003·T004 → T006
- 단계 2의 T009는 T008(샘플 항목)과 같은 게시 흐름을 쓰므로 T008 뒤. T012~T016은 서로 독립
- 단계 3은 단계 2의 설정 이름·게시 방식이 정해진 뒤(ReadMe와 기준 spec에 그 이름을 적음)
- 단계 4는 단계 1~3이 모두 끝난 뒤. T021 → T022 → T023 → T024 → T025·T026 → T027. T024의 서비스 등록은 사용자가 직접 한다(패스워드를 에이전트가 다루지 않음)
