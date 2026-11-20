# deployment

파일: Directory.Build.targets, Directory.Build.props, KspBridge.user.props.example, src/KspBridge.Server/KspBridge.Server.csproj, src/KspBridge.Server/kspbridge.sample.yaml, src/KspBridge.Server/Modules/ModuleManifest.cs, src/KspBridge.Mod.Core/KspBridge.Mod.Core.csproj, src/KspBridge.Mod.Loader/GenerationLoader.cs, tests/KspBridge.Server.Tests/FixtureModule.cs, ReadMe.md

## 요구(완료 조건)

- R1. 배포는 게시(publish)로 한다. 일반 빌드(`dotnet build`, VS 빌드)는 아무 데도 복사하지 않는다.
- R2. 서버 프로젝트(`KspBridge.Server`)를 게시하면 게시 출력에 서버 호스트, 모든 서버 모듈과 `modules/manifest.json`(R3과 같은 규칙), 설정 샘플 `kspbridge.sample.yaml`이 들어간다. 단일 파일 게시에서도 모듈은 `modules/`에 파일로 남는다. 게시 출력에는 실제 설정 파일 이름의 파일이 없어 게시 폴더의 설정 파일을 덮어쓰지 않는다.
- R3. 서버 모듈 프로젝트(`KspBridge.Server.Modules.*`)를 하나 게시하면, `KspBridge.user.props`의 `ServerModulesDirs`(`;`로 여러 개)에 적은 폴더마다 모든 서버 모듈 파일을 두고 `manifest.json`을 마지막에 바꿔치기로 쓴다. manifest의 모듈 목록은 저장소의 서버 모듈 프로젝트 목록이며(Core 먼저, 나머지 이름순), 그 목록에 없는 dll·pdb는 그 폴더에서 지운다. 설정이 비어 있으면 경고하고 건너뛴다. 실행 중인 서버(서비스 포함)는 멈추지 않고 그 세대를 읽는다.
- R4. Mod 쪽 프로젝트(`KspBridge.Mod.Core`, `KspBridge.Mod.Modules.*`) 가운데 어느 것을 게시하든, 게시마다 새 세대 표식(`ModBuildStamp`)으로 `KspBridge.Mod.Core`와 모든 `KspBridge.Mod.Modules.*`를 한 세트로 빌드해 `$(ModDeployDir)/PluginData/modules`(기본 `<KspDir>/GameData/KspBridge`)에 두고 `manifest.json`을 마지막에 쓰며, 이전 세대 파일을 지운다. 실행 중인 KSP는 그 세대를 읽는다.
- R5. Loader(`KspBridge.Mod.Loader`)를 게시하면 Loader DLL을 `$(ModDeployDir)/Plugins`에 두고, `settings.cfg`는 `$(ModDeployDir)/settings.cfg`에 없을 때만 둔다.
- R6. 모듈 세트를 빌드할 때 게시 전용 전역 속성(RID, 단일 파일 등)을 떼고, 게시한 프로젝트의 참조 그래프 밖인 모듈도 먼저 복원한다. 그래서 게시 명령 하나만으로 동작한다.
- R7. 저장소에 서버 설정 샘플 `src/KspBridge.Server/kspbridge.sample.yaml`이 있다. 모든 키와 설명, 기본 포트(`Port: 65531`, `ModUrl: http://127.0.0.1:65530`)를 담고 경로는 자리 표시로 쓴다.
- R8. `KspBridge.user.props.example`에 게시 설정(`KspDir`, `ServerModulesDirs`, `ModDeployDir`)의 예시와 설명이 있다.
- R9. `ReadMe.md`가 게시 방법, 고정 호스트를 바꿀 때 실행 중인 서버·KSP를 멈춰야 한다는 것, VS 게시 프로필의 기존 파일 삭제 설정을 켜지 말아야 한다는 것을 설명한다.

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-20 | 서비스-실행 | R1~R9 추가(기준 spec 처음 만듦, 배포 스크립트를 없애고 게시 기반 배포로 바꿈) | docs/specs/서비스-실행/ |

표에 없는 이전 변경: 배포 스크립트 기반 배포(기준 spec 없음, `docs/specs/골격/` 등 이전 변경 폴더 참고)
