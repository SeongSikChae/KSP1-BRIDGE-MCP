# MCP 서버 서비스 실행과 게시 기반 배포 계획

## 요약

- 서버가 Windows 서비스로 시작되면 SCM 수명 관리(`AddWindowsService`)를 쓰고, 콘솔에서는 지금처럼 동작한다.
- 시작 단계 오류(설정 오류·호스트 생성 오류)를 stderr와 함께 `--log` 폴더의 시작 오류 파일에도 남긴다.
- `scripts/deploy.ps1`과 `ServerRegistration`을 지운다. 배포는 게시(publish)로 한다.
  - 서버를 게시하면 호스트와 모든 서버 모듈, `modules/manifest.json`, 샘플 설정이 게시 출력에 들어간다.
  - 서버 모듈 프로젝트를 게시하면 모든 서버 모듈을 설정의 서버 모듈 폴더들에 둔다.
  - Mod 프로젝트(`Mod.Core`·`Mod.Modules.*`)를 게시하면 새 세대 표식으로 Mod 세트를 빌드해 KSP `GameData`에 둔다.
  - Loader를 게시하면 Loader DLL을 `Plugins`에 둔다.
- 게시 동작은 저장소 루트의 `Directory.Build.targets`에 모은다. 프로젝트 이름으로 대상을 나누고, 모듈 복사·manifest 쓰기·이전 파일 정리는 인라인 MSBuild 작업 하나가 맡는다.

## 바꿀 파일·구조

- `src/KspBridge.Server/KspBridge.Server.csproj`: `Microsoft.Extensions.Hosting.WindowsServices` 참조, 샘플 설정 파일을 게시 출력에 넣음
- `Directory.Packages.props`: `Microsoft.Extensions.Hosting.WindowsServices` 버전(10.0.15)
- `src/KspBridge.Server/Program.cs`: `AddWindowsService` 등록, 시작 오류 파일 기록, `ServerRegistration` 호출 삭제
- `src/KspBridge.Server.Core/StartupErrorLog.cs`(새 파일): `--log` 폴더의 시작 오류 파일에 한 건씩 덧붙여 쓴다
- `src/KspBridge.Server/Modules/ServerRegistration.cs`: 삭제
- `src/KspBridge.Server/kspbridge.sample.yaml`(새 파일): 서버 설정 샘플
- `Directory.Build.targets`(새 파일): 게시 대상(서버 모듈 세트, Mod 세트, Loader, 서버 게시 출력에 모듈 포함)과 인라인 작업 `PublishModuleSet`
- `Directory.Build.props`: 주석의 `ServerDir`를 새 설정 이름으로 바꿈
- `KspBridge.user.props.example`: `ServerDir`를 빼고 `ServerModulesDirs`, `ModDeployDir`(선택) 예시와 설명을 더함
- `scripts/deploy.ps1`: 삭제
- `src/KspBridge.Mod.Loader/GenerationLoader.cs`: 이전 세대 어셈블리 예외 메시지를 Mod 게시 안내로 바꿈(63행)
- `src/KspBridge.Mod.Core/KspBridge.Mod.Core.csproj`: `ModBuildStamp` 주석을 게시 기준으로 바꿈(11행)
- `src/KspBridge.Server/Modules/ModuleManifest.cs`: manifest를 쓰는 주체 주석을 게시 기준으로 바꿈(7·21행)
- `tests/KspBridge.Server.Tests/FixtureModule.cs`: 복사 순서 주석을 게시 기준으로 바꿈(25행)
- `tests/KspBridge.Server.Tests/ExitCodeTests.cs`: 설정 오류가 시작 오류 파일에 남는지 확인하는 테스트 추가
- `ReadMe.md`: 실행(콘솔·서비스), 게시 배포, 서비스 중 호스트 갱신, 기존 파일 삭제 설정 경고로 고침
- `docs/features/server-hosting.md`(새 파일): 기준 spec
- `docs/features/deployment.md`(새 파일): 기준 spec
- `docs/features/bridge-connection.md`: R2 수정, `파일:` 줄 갱신, 이력 추가

## 결정

- 서비스 수명 관리는 `builder.Services.AddWindowsService()`로 등록한다. 서비스로 시작했을 때만 `WindowsServiceLifetime`을 쓰고, 콘솔에서는 아무것도 바꾸지 않는다. `ContentRootPath`도 건드리지 않는다(2026-11-20 스크래치 프로젝트로 10.0.15에서 확인: 콘솔 실행 시 `IHostLifetime`은 `ConsoleLifetime`, `ContentRootPath`는 지정값 그대로). / 검토한 대안: `builder.Host.UseWindowsService()`는 서비스로 실행될 때 콘텐츠 루트를 `AppContext.BaseDirectory`로 바꾸려 해서, 이미 `ContentRootPath`를 정한 `WebApplicationBuilder`와 맞지 않는다.
- 시작 오류는 Serilog를 쓰지 않고 `--log` 폴더의 `KspBridge.Server.startup.log`에 시각과 함께 덧붙여 쓴다. 쓰기에 실패해도 무시한다(stderr는 이미 나감). / 검토한 대안: 본 로그 파일(`KspBridge.Server*.log`)에 Serilog로 쓰기. 호스트 생성 중 실패하면 본 로거가 그 파일을 이미 열었을 수 있어 잠금 충돌 위험이 있다.
- 서비스 실행 계정, 경로 검사, 등록 스크립트는 다루지 않는다(D009·D010·D011·D012).
- 게시 동작은 저장소 루트 `Directory.Build.targets` 한 곳에 둔다. 대상 프로젝트는 이름 접두사로 고른다. 새 모듈 프로젝트를 더해도 csproj를 고치지 않아도 된다. / 검토한 대안: 각 csproj에 target을 넣기. 같은 내용이 여러 파일에 퍼진다.
- 서버 모듈·Mod·Loader 게시는 `AfterTargets="Publish"`로 걸어 대상 폴더에 직접 복사한다. 서버 프로젝트 게시만은 게시 목록 계산 단계(`AfterTargets="ComputeResolvedFilesToPublishList"`, 단일 파일 번들과 복사보다 앞)에서 항목을 더한다. `Publish`가 끝난 뒤에는 최종 폴더 복사가 이미 끝나 있기 때문이다. 일반 빌드(`dotnet build`, VS 빌드)는 배포하지 않는다(D019).
- 모듈 세트(서버 모듈 세트, Mod 세트)를 빌드하는 MSBuild 작업 호출은 다음을 지킨다. (D027)
  - 게시 전용 전역 속성(`RuntimeIdentifier`, `SelfContained`, `PublishSingleFile`, `PublishReadyToRun`, `PublishTrimmed`, `PublishDir`, `PublishProfile`, `_IsPublishing`)을 `RemoveProperties`로 뗀다. 모듈 프로젝트는 RID 없이 복원되므로, 이 속성을 이어받으면 NETSDK1047로 빌드가 실패한다(plan 검토 2회차에서 재현).
  - 빌드 전에 세트의 프로젝트를 `Restore` 대상으로 먼저 부른다. 이때 별도 복원 세션 속성을 줘서 빌드 평가와 분리한다. 모듈 프로젝트는 게시한 프로젝트의 참조 그래프 밖에 있어 자동 복원 대상이 아니다.
  - `Configuration`만 넘기고, Mod 세트는 `ModBuildStamp`도 넘긴다.
- 서버 모듈 프로젝트를 게시하면 `src/KspBridge.Server.Modules.*/*.csproj` 전체를 같은 Configuration으로 빌드한다(위 MSBuild 작업 규칙). 그 결과를 `$(ServerModulesDirs)`(세미콜론 목록)의 폴더마다 둔다. `ServerModulesDirs`가 비어 있으면 경고하고 건너뛴다. (D020·D023·D025)
- 서버 프로젝트(`KspBridge.Server`)를 게시할 때는 빌드한 서버 모듈 dll·pdb와 obj에 만든 `manifest.json`을 `ResolvedFileToPublish` 항목(`RelativePath`=`modules\...`, `ExcludeFromSingleFile`=`true`, `CopyToPublishDirectory`=`PreserveNewest`)으로 더한다. SDK는 `CopyToPublishDirectory` 값으로 나눈 항목만 복사하므로(`_ComputeResolvedFilesToPublishTypes`) 이 값이 없으면 복사되지 않는다. 게시 프로필이 단일 파일 게시(`PublishSingleFile=true`)라서, 이 표시가 없으면 SDK의 `_ComputeFilesToBundle`이 dll을 exe 안에 묶어 `modules`에 파일이 남지 않는다. 그러면 `dotnet publish -o`와 VS 폴더 게시(`PublishDir`에 모았다가 `PublishUrl`로 복사) 모두에서 같은 파일이 게시 폴더에 들어간다. 샘플 설정은 `kspbridge.sample.yaml`을 `CopyToPublishDirectory`로 넣는다. 실제 설정 파일 이름의 파일은 게시 출력에 없다. (D024) / 검토한 대안: 게시 뒤 `PublishUrl`에 직접 복사하기. 게시 방식(CLI·VS)마다 최종 폴더 속성이 달라 갈린다.
- Mod 프로젝트(`KspBridge.Mod.Core`, `KspBridge.Mod.Modules.*`) 가운데 무엇을 게시하든 Mod 세트를 새로 빌드한다. 게시 때 `ModBuildStamp`를 `<2026-01-01부터 일수 % 65535>.<하루 중 2초 단위>`(UTC)로 만들고(게시마다 다르고 어셈블리 버전 범위 안), `Mod.Core`와 모든 `Mod.Modules.*`를 그 표식으로 빌드한다. 그 결과를 `$(ModDeployDir)/PluginData/modules`에 둔다. `ModDeployDir` 기본값은 `$(KspDir)\GameData\KspBridge`다. (D022) / 검토한 대안: 게시한 프로젝트만 빌드하기. 시험 게시에서 Mod 모듈 하나를 게시하면 그 모듈과 `Mod.Core`만 표식으로 빌드되는 것을 확인했다(2026-11-20). 다른 모듈은 이전 세대 이름으로 남아 세대가 어긋난다.
- Loader(`KspBridge.Mod.Loader`)를 게시하면 `KspBridge.Mod.Loader.dll`을 `$(ModDeployDir)/Plugins`에 복사하고, `settings.cfg`는 `$(ModDeployDir)/settings.cfg`(`GameData/KspBridge` 바로 아래, `BridgeLoader`가 포트를 읽는 위치)에 없을 때만 복사한다.
- 인라인 작업 `PublishModuleSet`(RoslynCodeTaskFactory, C#)은 다음 순서로 동작한다.
  - 모듈 dll과 있으면 pdb를 복사한다.
  - `manifest.json`을 임시 파일에 쓴 뒤 바꿔치기로 마지막에 쓴다.
  - manifest에 없는 `*.dll`·`*.pdb`를 지운다.
  - 모듈 순서는 Core(`*.Modules.Core` 또는 `Mod.Core`)를 맨 앞에 두고 나머지는 이름순이다. 서버 `ModuleHost`와 Mod `GenerationLoader`가 적힌 순서대로 읽기 때문이다.
  - 서버 게시 출력용 manifest도 같은 작업이 만든다(복사 없이 파일만 생성).
  - / 검토한 대안: MSBuild 기본 작업(`Copy`, `WriteLinesToFile`, `Delete`)만 쓰기. 바꿔치기 쓰기와 목록 밖 파일 삭제를 표현하기 번거롭고, 순서 보장이 흐려진다.
- 실행 중인 KSP가 잠근 Loader DLL처럼 복사가 잠금으로 실패하면 MSBuild `Copy` 오류를 그대로 낸다(따로 재시작 안내를 만들지 않음).

## 인터페이스·데이터

`KspBridge.user.props`(로컬, 커밋 안 함)

```xml
<KspDir>C:\Games\Kerbal Space Program</KspDir>          <!-- 참조와 Mod 게시 대상 기본값 -->
<ServerModulesDirs>C:\Apps\KspBridge\modules</ServerModulesDirs> <!-- 세미콜론으로 여러 개 -->
<ModDeployDir></ModDeployDir>                            <!-- 비우면 $(KspDir)\GameData\KspBridge -->
```

`manifest.json`(지금과 같은 형식)

```json
{ "build": "<표식>", "modules": ["KspBridge.Server.Modules.Core.dll", "..."] }
```

- 서버 모듈 manifest의 `build`는 게시 시각(UTC `yyyyMMddHHmmss`)이다.
- Mod manifest의 `build`는 `ModBuildStamp`다.

게시 명령 예시(ReadMe에 적음)

```
dotnet publish src/KspBridge.Server.Modules.Wait   # 서버 모듈 세트 → ServerModulesDirs
dotnet publish src/KspBridge.Mod.Core              # Mod 세트 → GameData/KspBridge/PluginData/modules
dotnet publish src/KspBridge.Mod.Loader            # Loader → GameData/KspBridge/Plugins
dotnet publish src/KspBridge.Server -o <게시 폴더>  # 또는 VS 폴더 게시
```

시작 오류 파일: `<--log>/KspBridge.Server.startup.log`. 한 건마다 `yyyy-MM-dd HH:mm:ss.fff zzz [kind] message` 한 줄과 예외 내용을 쓴다.

## 오류·위험

- VS 폴더 게시의 기존 파일 삭제(`DeleteExistingFiles`)가 켜져 있으면 게시 폴더의 설정·로그가 지워진다. ReadMe에 끄라고 안내하고(D021), 로컬 프로필도 구현 때 끈다(커밋 안 함).
- 서버 호스트 게시는 서비스가 돌면 exe 잠금으로 실패한다. ReadMe에 서비스 중지 → 게시 → 시작 순서를 적는다(C10). 서버 게시 출력의 모듈 복사 순서는 보장되지 않는다. 다만 이때 서버는 멈춰 있으므로 manifest가 먼저 놓여도 문제가 없다.
- 서버 모듈·Mod 게시 때 실행 중인 서버와 KSP는 모듈 파일을 잠그지 않는다. 서버 `ModuleLoadContext`는 스트림으로 읽고 바로 닫고, Mod `GenerationLoader`는 바이트로 읽는다. 그래서 덮어쓰기와 삭제가 막히지 않는다. 쓰는 도중에 서버가 읽는 일은 manifest를 마지막에 바꿔치기로 써서 막는다.
- 인라인 작업은 `dotnet` CLI와 VS MSBuild 모두에서 RoslynCodeTaskFactory로 컴파일된다. 첫 게시 때 몇 초 늦어진다.
- Mod 세트 빌드는 `ModBuildStamp` 전역 속성으로 다시 빌드하므로 `bin`에 표식이 붙은 산출물이 쌓인다(게시 대상 폴더에는 manifest에 있는 세대만 남는다).
- 시작 오류 파일 쓰기가 실패하면(폴더 권한 등) 기록되지 않는다. stderr는 남는다.

## 검증 전략

- C1:
  - 콘솔: `dotnet test`의 기존 서버 테스트가 통과한다. 게시본을 콘솔로 실행해 `/health`가 `Healthy`이고 Ctrl+C로 끝나는지 본다.
  - 서비스: C12에서 서비스를 시작·중지해 상태(`Get-Service`)와 서버 로그의 종료 기록을 본다.
- C2: `ExitCodeTests`에 테스트를 추가한다. 설정 오류로 실행했을 때 `<log>/KspBridge.Server.startup.log`에 원인 문구가 있는지 본다. 서비스 쪽은 C12 뒤 잘못된 `--config`로 잠깐 바꿔 보는 대신, 콘솔과 같은 코드 경로임을 코드로 확인한다(확인 범위를 보고에 적는다).
- C3: 스크립트 파일이 지워졌는지, 바꿀 파일 목록의 해당 파일(`GenerationLoader.cs`, `KspBridge.Mod.Core.csproj`, `ModuleManifest.cs`, `FixtureModule.cs`, `Program.cs`, `ReadMe.md`, `Directory.Build.props`, `KspBridge.user.props.example`)이 게시 기준으로 고쳐졌는지 읽어 본다.
- C4: `ServerRegistration` 파일과 참조가 없는지(`git grep ServerRegistration`) 본다. 서버를 실행한 뒤 `%LOCALAPPDATA%\KspBridge\servers`에 새 파일이 생기지 않는지 본다.
- C5: 단일 파일 게시(`dotnet publish src/KspBridge.Server -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o <스크래치>`)로 게시해 `modules/`에 dll 파일·manifest가 있고 샘플 파일이 있는지 본다. 게시 프로필 경로(`-p:PublishProfile=FolderProfile -p:PublishUrl=<스크래치2>`)로도 같은지 본다. 미리 둔 설정 파일이 그대로인지도 본다.
- C6: `dotnet publish src/KspBridge.Server.Modules.Wait -p:ServerModulesDirs="<스크래치A>;<스크래치B>"`로 게시한다.
  - 두 폴더 모두에 모든 서버 모듈과 manifest가 있는지 본다.
  - 미리 넣어 둔 목록 밖 dll이 지워지는지 본다.
  - 실행 중 서버 쪽은 C12에서 본다.
- C7: `dotnet publish src/KspBridge.Mod.Modules.Wait -p:ModDeployDir=<스크래치>`로 게시한다.
  - `PluginData/modules`에 같은 표식의 `Mod.Core`와 모든 Mod 모듈, manifest가 있는지 본다.
  - 한 번 더 게시하면 이전 표식 파일이 지워지는지 본다.
  - Loader 게시로 `Plugins/KspBridge.Mod.Loader.dll`이 생기고, 이미 있는 `settings.cfg`는 그대로인지 본다.
  - 실제 KSP 쪽은 C12에서 본다.
- C8: 샘플 파일의 키가 `AppConfiguration`의 속성과 같은지 본다. 개인화 경로가 없는지 본다. 샘플의 자리 표시만 실제 경로로 바꾼 설정으로 서버가 뜨는지 본다(C5 게시본 콘솔 실행).
- C9: `KspBridge.user.props.example`과 `Directory.Build.props`를 읽어 확인한다.
- C10: ReadMe를 읽어 항목별로 확인한다. `git grep`으로 남은 서술이 없는지 본다.
- C11: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`.
- C12:
  - 로컬 `KspBridge.user.props`에 `ServerModulesDirs`=게시 폴더의 `modules`를 적는다.
  - 로컬 게시 프로필의 `PublishUrl`을 게시 폴더로 바꾸고 `DeleteExistingFiles`를 끈다.
  - 게시 폴더 설정 파일의 `ContentRootPath`·`ModulesDir`를 확인한다.
  - 사용자가 받은 명령으로 서비스를 등록·시작한다.
  - 그 뒤 `Get-Service`, `/health`, MCP 연결을 본다.
  - 서버 모듈을 게시하고 manifest·로그 줄·서비스 상태를 본다.
  - Mod를 게시하기 전에 KSP 상태를 MCP로 확인한다(다른 비행이 돌고 있을 수 있음). 게시 뒤 manifest와 새 세대 로드를 MCP 응답으로 본다.
- C13: 세 기준 spec을 읽어 확인한다.

## 원칙 점검

- 적용 규칙:
  - `workflow/spec-driven.md`(변경 문서·검토·기준 spec), `git/github-issue.md`(이슈 #45 범위 갱신 완료)
  - `workflow/repository-content.md`(저장소 파일에 개인화 경로 없음. 예시 경로는 일반 경로 `C:\Apps\...`, `C:\Games\...`)
  - `dotnet/nuget-package-selection.md`(`Microsoft.Extensions.Hosting.WindowsServices`는 net10.0 호환 10.0.x의 최신 패치 10.0.15, 구현 때 취약점·Deprecated 여부 확인)
  - `dotnet/project-creation.md`(새 프로젝트 없음, `Directory.*` 파일 규칙 따름)
  - `shell/powershell-windows.md`(검증 명령)
  - 메모리: 게임 동작을 추측으로 설계하지 않음(시험 게시로 확인함), KSP 상태 확인 뒤 조작, 확인은 MCP 응답으로
- 충돌: 없음
