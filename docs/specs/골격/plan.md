# 골격: P-C-C 구조와 MCP·Mod 핫리로드 기반 계획

## 요약

MCP 쪽과 KSP 쪽을 같은 모양으로 만든다. **고정 호스트**는 다시 읽지 않는 얇은 부분이고, 핫리로드되는 **모듈 세대**가 기능을 담는다. MCP 서버는 클라이언트가 띄우는 stdio 프로세스가 아니라, 사용자가 따로 실행해 두는 **상주 HTTP 서버**(MCP Streamable HTTP)다.

```
MCP 클라이언트(LLM = Planner) ── http://127.0.0.1:8651/mcp
KspBridge.Server (고정 호스트, ASP.NET Core, net10.0, 수동 실행)
   ├ 모듈 세대 N (collectible AssemblyLoadContext): Server.Modules.*  ← 도구
   │ HTTP http://127.0.0.1:8650 (POST /api/{module}/{op})
KspBridge.Mod.Loader (고정 호스트, KSPAddon, net472)
   └ 모듈 세대 M (Assembly.Load(byte[])): Mod.Core(Coordinator·Controller·라우터), Mod.Modules.*  ← 단계 종류
```

- 배포 스크립트가 모듈 DLL을 복사한 뒤 `manifest.json`을 **마지막에** 쓴다. 각 호스트는 이 파일만 감시하다가 바뀌면 새 세대를 통째로 읽고, 이전 세대를 정리한다. 명시적으로 다시 읽는 `reload` 도구도 둔다.
- 기능 하나는 "Server 모듈(도구) + Mod 모듈(단계 종류·요청 처리)" 쌍이다. 코어는 등록 지점만 제공한다(C9).
- Coordinator와 Controller는 Mod.Core에 있다. 단계 종류는 모듈이 `IStepType`으로 등록한다. Coordinator는 물리 프레임마다 단계를 판정하고(`Judge`), Controller는 기체의 `OnFlyByWire`에서 제어를 호출한다(`Control`).
- 서버 호스트 자체를 바꿨을 때는 서버 프로세스만 다시 실행하면 된다. 클라이언트는 다시 시작하지 않는다.

## 바꿀 파일·구조

- `KspBridge.slnx`, `Directory.Build.props`, `Directory.Packages.props`: 솔루션과 공통 설정(CPM). 기본 TFM은 net10.0이고, Mod 계열만 net472를 쓴다.
- `KspBridge.user.props.example`: 로컬 경로(`KspDir`, `ServerDir`)의 예시. 실제 `KspBridge.user.props`는 커밋하지 않는다.
- `.gitignore`: `KspBridge.user.props`를 추가한다.
- `src/Shared/Protocol.cs`: 통신 규약 버전, 경로 상수, 오류 코드. Server와 Mod 양쪽 프로젝트에 링크로 넣는다.
- `src/KspBridge.Server/`: MCP HTTP 호스트. 서버 템플릿의 기동 흐름(`Program.cs`, `AppConfiguration`, `ConfigurationValidator`), MCP 엔드포인트 `/mcp`, `/health`, 모듈 세대 로더, 매니페스트 감시, `BridgeClient`(Mod HTTP), 호스트 도구 `reload`.
- `src/KspBridge.Server.Core/`: 서버 템플릿의 구성 요소(`ExitCode`, `AddServerSerilog`, `ConfigureServerKestrel`). KMS와 TLS 구성 요소는 넣지 않는다(원칙 점검 참조).
- `src/KspBridge.Server.Contracts/`: 서버 모듈 계약(`IBridgeClient`, 모듈 표식 attribute). 고정이며 기본 ALC에서 공유한다.
- `src/KspBridge.Server.Modules.Core/`: 코어 도구 모듈. `get_status`, `submit_plan`, `get_plan`, `abort_plan`, `get_flight_events`, `list_step_types`.
- `src/KspBridge.Server.Modules.Wait/`: 대기 기능의 도구 쪽(`submit_wait`, 단계 설명).
- `src/KspBridge.Server.Modules.Debug/`: 검증용 도구(`debug_launch`, `debug_revert`, `debug_space_center`).
- `src/KspBridge.Mod.Loader/`: KSPAddon 고정 호스트. HTTP 서버, 요청을 메인 스레드로 넘기는 큐, 세대 로더·감시, `Update`/`FixedUpdate` 전달, reload 사이 상태 이월. Mod 계약 인터페이스(`ILogic`, `ILoaderHost`)도 여기 둔다.
- `src/KspBridge.Mod.Core/`: 로직 진입점, JSON, 라우터, 모듈 레지스트리, Coordinator, Controller, 이벤트 버퍼, 비행 세션 추적, 구독 정리기, 상태 조회.
- `src/KspBridge.Mod.Modules.Wait/`: `wait` 단계 종류.
- `src/KspBridge.Mod.Modules.Debug/`: 발사·되돌리기·우주센터 이동 요청 처리.
- `tests/KspBridge.Server.Tests/` (MSTest, net10.0, TestServer): 모듈 세대 교체와 언로드, 도구 목록 변경 알림, Mod가 없을 때 시간 초과 오류, 종료 코드.
- `tests/KspBridge.Server.Core.Tests/` (MSTest, net10.0): Kestrel 바인딩이 루프백 주소로만 열리는지 확인한다.
- `tests/KspBridge.Mod.Core.Tests/` (MSTest, net472): KSP에 의존하지 않는 부분. JSON, 계획 파싱, Coordinator 상태 기계, 이벤트 버퍼, 상태 이월.
- `scripts/deploy.ps1`: 빌드하고 산출물을 KSP `GameData/KspBridge`와 `ServerDir`에 배포한다. 매니페스트는 마지막에 쓴다. 서버를 띄우거나 끄지 않는다.
- `ReadMe.md`: 구조, 빌드·배포, 서버 실행 명령, MCP 등록(URL), 모듈 추가 방법.

배포 배치:

```
GameData/KspBridge/Plugins/KspBridge.Mod.Loader.dll         ← KSP가 시작할 때 읽음(고정)
GameData/KspBridge/settings.cfg                             ← Mod HTTP 포트
GameData/KspBridge/PluginData/modules/*.dll, manifest.json  ← KSP 로더가 무시하는 폴더, 브리지가 직접 읽음
<ServerDir>/KspBridge.Server.exe (+ 의존 DLL), kspbridge.yaml, logs/
<ServerDir>/modules/*.dll, manifest.json
```

## 결정

- **MCP 전송은 상주 HTTP 서버(Streamable HTTP), 사용자가 수동으로 실행**: 서버의 수명이 클라이언트와 분리된다. 그래서 호스트 자체를 바꿀 때도 클라이언트를 다시 시작하지 않는다. 여러 클라이언트가 서버 하나를 같이 쓰고, stdout이 프로토콜 채널이 아니어서 로그가 섞이지 않는다.
  검토한 대안: (a) stdio. 클라이언트가 프로세스를 띄워 편하지만, 호스트를 바꾸면 클라이언트를 다시 시작해야 한다. (b) KSP Mod 안에서 직접 MCP를 제공하는 방식. net472용 SDK가 없어 프로토콜을 직접 구현해야 하고, KSP가 꺼지면 원인을 알려 줄 주체도 사라진다(C6).
  기동 방식은 사용자 선택에 따라 수동 실행으로 한다. 작업 스케줄러나 배포 스크립트의 자동 기동은 하지 않는다.
- **루프백 바인딩만**: 서버(8651)와 Mod(8650) 모두 `127.0.0.1`에만 연다. 인증 없이 기체를 조종할 수 있는 엔드포인트를 바깥에 노출하지 않는다.
- **고정 호스트 + 모듈 세대 (양쪽 같은 모양)**: 다시 읽지 않는 부분을 HTTP 리스너, MCP 엔드포인트, 로더로 줄여 거의 바꿀 일이 없게 한다. 포트와 MCP 세션이 reload 중에도 유지된다.
  검토한 대안: KSP 쪽에 HTTP까지 reload하는 방식. reload마다 포트를 다시 열어야 하고, reload 도중 들어온 요청을 잃는다.
- **모듈 단위가 아니라 세대 단위로 reload**: 모듈이 Core 타입을 참조하므로 Core를 바꾸면 모듈도 다시 읽어야 한다. 세대 전체를 원자적으로 바꾸면 버전이 섞이지 않는다.
  검토한 대안: 모듈별 개별 reload. 의존 그래프를 관리해야 하고, 섞인 세대에서 타입이 어긋날 위험이 있다.
- **Mod: `Assembly.Load(byte[])` + 세대마다 다른 어셈블리 이름**: Mono(.NET Framework 4.7.2)는 어셈블리를 내릴 수 없다. 바이트로 읽으면 파일이 잠기지 않는다. 이전 세대는 메모리에 남는데, 개발 중 수십 번 reload하는 정도는 감수할 수 있다.
  KSP의 Unity Mono는 바이트로 읽을 때 같은 단순 이름의 어셈블리가 이미 있으면 버전을 무시하고 기존 어셈블리를 돌려준다(첫 KSP 시험에서 확인). 그래서 배포 스크립트는 Mod 로직 어셈블리(Core와 Mod 모듈)를 배포마다 이름 뒤에 세대 표식을 붙여 빌드한다(예: `KspBridge.Mod.Core.g320_30297`). 같은 빌드에서 모듈이 그 이름으로 Core를 참조하므로 세대 안의 참조는 같은 세대로 묶인다. `AppDomain.AssemblyResolve`는 현재 세대 어셈블리 이름을 해석하는 보조로 그대로 두며, 고정 호스트(Loader)는 바꾸지 않는다. 표식 없이 빌드한 산출물(테스트, 일반 빌드)은 이름이 그대로다.
  검토한 대안: (a) 버전 리비전만 올리기. Unity Mono가 버전을 무시해 효과가 없었다. (b) 로드 시점에 Mono.Cecil로 이름을 바꾸기. KSP에 없는 의존성을 Loader에 넣어야 하고, 고정 호스트가 커진다.
  검토한 대안: 별도 AppDomain. Unity·KSP 타입을 도메인 경계 너머로 넘길 수 없어 쓸 수 없다.
- **Server: collectible `AssemblyLoadContext` + `LoadFromStream`**: .NET 10은 언로드를 지원하고, 스트림으로 읽으면 파일이 잠기지 않는다. `Server.Contracts`, `ModelContextProtocol`, `Microsoft.Extensions.*`는 기본 ALC로 넘겨 타입을 공유한다.
- **도구 목록 갱신은 MCP SDK의 서버 도구 컬렉션을 바꿔 `notifications/tools/list_changed`로 알린다**: 클라이언트를 다시 시작하지 않고 새 도구를 보인다(C3). HTTP에서는 클라이언트가 연 GET 스트림으로 알림이 간다.
  검토한 대안: 고정된 범용 도구 하나(`call(module, op, args)`)만 두는 방식. Planner에게 도구 이름과 스키마가 보이지 않아, Planner에 분명한 도구를 주라는 원칙에 어긋난다.
- **reload 시점은 `manifest.json` 변경 + 명시 도구**: 복사가 덜 끝난 DLL을 읽지 않도록 배포 스크립트가 매니페스트를 마지막에 쓴다. 감시 이벤트는 500 ms 디바운스한다. Mod 쪽 실제 교체는 메인 스레드의 `Update` 처음에서 한다.
- **Mod 계약(ILogic)은 문자열 JSON 경계**: `Handle(route, json) → json`, `Start(host, carry)`, `Stop() → carry`, `Update()`, `FixedUpdate()`. 계약이 작으면 Loader를 다시 빌드할 일이 거의 없다. 계약을 바꾸면 `LoaderApiVersion`을 올린다. 그러면 KSP 재시작이 필요하고, 세대 로드 때 불일치를 알려 준다.
- **상태 이월**: `Stop()`이 이벤트 버퍼, 마지막 계획 요약, 비행 세션 번호를 JSON으로 돌려주고, 새 세대가 `Start`에서 받는다. reload로 끝난 계획 기록이 reload 뒤에도 보인다(C4).
- **구독 정리기**: Mod.Core는 GameEvents, `OnFlyByWire` 등 모든 외부 구독을 하나의 정리기로 등록하고, `Stop()`에서 한꺼번에 해제한다. 이전 세대가 이벤트에 계속 반응하는 것을 구조로 막는다(C4, C8).
- **Coordinator는 `FixedUpdate`, Controller는 `OnFlyByWire`**: 단계 전환이 물리 프레임 단위로 정확하다. Controller는 계획이 실행 중일 때만 활성 기체에 붙고, 기체가 바뀌면 떼어 낸다.
- **Server↔Mod 통신은 localhost HTTP(`HttpListener`)**: 사람이 `curl`로 확인할 수 있고, KSP의 Mono에서 추가 의존성 없이 쓸 수 있다. 응답 시간 제한은 Server 3 s, Mod 메인 스레드 대기 2 s다. 넘으면 `mod_busy` 오류를 돌려준다.
  검토한 대안: named pipe. 디버깅 도구가 적고, Mono 지원이 불안정하다.
- **JSON은 Mod.Core에 작은 자체 구현**: KSP에는 Newtonsoft가 없고, 다른 모드와 버전이 충돌할 위험이 있다. Server 쪽은 `System.Text.Json`을 쓴다.
- **검증용 Debug 모듈**: 화면 조작 없이 MCP 응답만으로 C4·C8을 확인하려면 발사·되돌리기 수단이 필요하다. 이 모듈은 C9 방식의 두 번째 예이기도 하다.
- **배포는 스크립트 하나**: `scripts/deploy.ps1`이 `dotnet build`와 복사를 함께 한다. 경로는 `dotnet msbuild -getProperty`로 `KspBridge.user.props`에서 읽는다. 고정 호스트 파일(Loader DLL, 서버 exe와 호스트 DLL)은 내용이 바뀌었을 때만 복사한다. 실행 중이라 잠겨 있으면 "KSP 재시작 필요"나 "서버 재실행 필요"를 출력하고, 모듈 배포는 계속한다.

## 인터페이스·데이터

MCP 서버:

| 경로 | 설명 |
|---|---|
| `POST/GET/DELETE /mcp` | MCP Streamable HTTP 엔드포인트 |
| `GET /health` | 서버 상태(템플릿 헬스체크) |

서버 실행: `KspBridge.Server.exe --config kspbridge.yaml --log logs`
클라이언트 등록 예: `claude mcp add --transport http kspbridge http://127.0.0.1:8651/mcp`

`kspbridge.yaml`: `ContentRootPath`, `Port`(8651), `ModUrl`(`http://127.0.0.1:8650`), `RequestTimeoutMs`(3000), `ModulesDir`.

HTTP (Mod):

| 경로 | 처리 | 설명 |
|---|---|---|
| `GET /loader/status` | Loader | 로더 버전, API 버전, 세대 번호, 로드 시각, 마지막 로드 오류 |
| `POST /loader/reload` | Loader | 다음 `Update`에서 세대를 교체하고 결과를 돌려줌 |
| `POST /api/{module}/{op}` | 현재 세대 라우터 | 모듈이 등록한 처리기. 세대가 없으면 `logic_not_loaded` |

응답 본문은 `{"ok":true,"data":{...}}` 또는 `{"ok":false,"error":{"code":"...","message":"..."}}`이다. 요청 헤더 `X-KspBridge-Protocol`가 다르면 `protocol_mismatch`를 돌려준다.

계획:

```json
{ "steps": [ { "type": "wait", "params": { "seconds": 5 }, "timeout_s": 60 } ] }
```

계획 상태는 `pending`, `running`, `completed`, `aborted`다. 끝난 계획에는 `end_reason`(`completed`, `planner_abort`, `step_failed`, `timeout`, `reload`, `vessel_change`, `scene_change`, `new_flight`)이 붙는다.

Mod.Core 모듈 API:

```csharp
interface IBridgeModule { string Name { get; } void Register(ModuleContext ctx); }   // ctx.AddStepType, ctx.AddHandler(op, fn), ctx.Subscriptions
interface IStepType { string Name { get; } string Describe(); IStepRun Create(JsonObject p); }
interface IStepRun { void Begin(StepContext c); StepResult Judge(StepContext c); void Control(FlightCtrlState s, StepContext c); void End(string reason); }
```

Server 모듈: 모듈 어셈블리의 `[McpServerToolType]` 클래스를 찾아 도구로 만든다. 도구 메서드는 DI로 `IBridgeClient`를 받는다.

`get_status` 응답(요지): `scene`, `ready`, `versions{server_host, server_modules, mod_loader, mod_logic, protocol, protocol_match}`, `generations{server{n, loaded_at}, mod{n, loaded_at, last_error}}`, `vessel{name, situation, altitude_m}|null`, `plan{id, state, step_index, step_type}|null`, `controller{attached, bridge_flybywire_handlers}`.

## 오류·위험

- **MCP SDK의 도구 컬렉션 변경 API와 클라이언트의 `list_changed` 처리(HTTP)**: 첫 단계에서 짧게 시험한다. SDK가 지원하지 않으면 멈추고 대안(고정 범용 도구)을 사용자와 다시 정한다.
- **서버 재실행 뒤 세션 복구**: 서버가 다시 시작되면 기존 MCP 세션 ID가 무효가 된다. 클라이언트가 404를 받고 새 세션을 맺는지 첫 단계에서 시험한다. 맺지 못하면 ReadMe에 클라이언트 쪽 재연결 방법(Claude Code의 `/mcp`)을 적는다.
- **서버가 꺼져 있을 때**: 수동 실행이므로 서버가 없으면 클라이언트는 연결 실패만 본다. ReadMe와 도구 오류에서 실행 명령을 안내한다.
- **Mono의 바이트 로드와 이름 해석**: Unity Mono는 같은 단순 이름이면 이전 세대 어셈블리를 돌려준다(첫 KSP 시험에서 확인, 버전 리비전으로는 막히지 않음). 배포마다 어셈블리 이름에 세대 표식을 붙여 구별하고, Loader는 이미 읽은 어셈블리 이름이 다시 오면 로드를 거부해 이전 세대를 유지한다. 배포 스크립트는 매니페스트에 없는 이전 세대 DLL을 모듈 폴더에서 지운다.
- **Mod 메모리 누수**: 이전 세대는 내려가지 않는다. 개발용으로 감수하고, `generations.mod.n`으로 횟수가 보이게 한다.
- **Unity API 호출 스레드**: HTTP 스레드에서 KSP API를 부르면 안 된다. 모든 처리기는 메인 스레드 큐에서만 실행한다.
- **씬 로드 중 요청**: 로드 중에는 `ready=false`로 돌려주고, 단계·도구가 준비되지 않은 상태에서 실행되지 않게 막는다(로드 타이밍 오류는 Mod 결함으로 다룬다).
- **고정 호스트 변경**: Loader 계약을 바꾸면 KSP를, 서버 호스트를 바꾸면 서버 프로세스를 다시 시작해야 한다. 배포 스크립트 출력과 `get_status`의 버전으로 드러낸다.

## 검증 전략

KSP 확인은 화면을 보지 않고 MCP 응답으로만 한다. 장면 전환 전에는 `get_status`로 다른 비행이 진행 중인지 확인한다. 검증할 때는 서버를 백그라운드로 직접 실행하고, MCP 등록(`~/.claude.json` 변경)은 사용자 승인을 받은 뒤 한다.

- C1: 깨끗한 클론에서 `dotnet build KspBridge.slnx`(경고 0), 이어서 `scripts/deploy.ps1`. 두 위치에 파일과 매니페스트가 있는지 확인한다. 빌드된 서버 exe로 종료 코드(인자 오류 1, 설정 오류 2)와 `/health`를 확인한다.
- C2: KSP를 켠 채 Mod.Core의 상태 응답에 표식 필드를 추가하고 배포한다. `get_status`에서 `mod.n`이 늘고 `loaded_at`이 바뀌고 표식이 보이면 통과다.
- C3: 이 세션을 MCP 클라이언트로 쓴다. Server.Modules.Wait에 시험 도구를 추가해 배포하고, 클라이언트를 다시 시작하지 않은 채 새 도구를 호출한다. 기존 도구 설명을 바꾼 것도 반영되는지 확인한다. 그 뒤 시험 도구는 되돌린다. 단위 테스트(TestServer)로 세대 교체, 언로드(WeakReference 수거), `list_changed` 알림도 확인한다.
- C4: `debug_launch`로 발사하고 60 s짜리 `wait` 계획을 제출한다. `controller.attached=true`를 확인한 뒤 `reload`를 호출한다. 그 뒤 `get_plan`에서 `aborted/reload`를, `get_status`에서 `controller.bridge_flybywire_handlers=0`을 확인한다.
- C5: 위 과정의 `get_status` 응답 필드를 대조한다. 규약 불일치는 단위 테스트로 확인한다(헤더를 다르게 보내 `protocol_mismatch`).
- C6: KSP를 끈 상태에서 `get_status`를 호출해 3 s 안에 `mod_unreachable`이 오는지 본다. 단위 테스트로 응답하지 않는 서버에 대한 시간 초과도 확인한다.
- C7: `wait` 2단계(2 s, 3 s) 계획을 제출하고 완료될 때까지 `get_flight_events`를 확인한다. 단계 시작·완료 이벤트와 `plan_completed`가 나와야 한다. `abort_plan`은 따로 확인한다. Coordinator 상태 기계는 단위 테스트로도 확인한다.
- C8: 계획을 실행하는 중에 `debug_revert`를 하면 `end_reason=new_flight`(또는 `scene_change`)가 되고, 그 뒤 `get_status.plan=null`이어야 한다. `debug_space_center`도 같은 방식으로 확인한다.
- C9: `wait`와 `debug` 기능이 모듈 프로젝트에만 있고 코어 파일에 이 기능 이름이 없음을 `git grep`으로 확인한다. `list_step_types`에 `wait`가 나와야 한다.

## 원칙 점검

- 적용 규칙
  - `workflow/spec-driven.md`, `git/github-issue.md`(#30)
  - `dotnet/project-creation.md`: net10.0, .slnx, src/tests, Directory.*.props, MSTest, `{Solution}.{역할}` 이름
  - `dotnet/server-api-template.md`: Server/Server.Core 구성, `--config`/`--log`, YAML `AppConfiguration`과 검증, Kestrel `Port`, Serilog Console과 일별 File, ProblemDetails, `/health`, 종료 코드 0/1/2/3, MSTest와 TestServer
  - `dotnet/nuget-package-selection.md`: 패키지 버전은 구현할 때 확인
  - `git/commit-message.md`, `shell/powershell-windows.md`
  - 메모리 규칙: P-C-C 유지와 보정은 별도 모듈, 화면 없이 MCP로 검증, 장면 전환 전 상태 확인
- 충돌
  - net472(Mod 계열): KSP 1.12.5가 Unity 2019.4 Mono에서 돌기 때문에 net10.0을 쓸 수 없다. Mod 프로젝트와 그 테스트만 예외로 둔다.
  - 서버 템플릿의 `OsKms` 필수 섹션과 `KmsSecretProtector`: 이 서버에는 보안값(패스워드, 키)이 없다. 그런데도 OS 자격 증명 저장소에 기록하게 되므로 넣지 않는다. 보안값이 생기면 그때 `dotnet/kms-config-secrets.md`대로 추가한다.
  - 서버 템플릿의 TLS: 루프백 전용 개발 도구라 넣지 않는다. 대신 바인딩을 `127.0.0.1`로 제한하고 테스트로 확인한다(템플릿은 설정 포트에 바인딩).
  - 서버 템플릿의 `MapControllers`/OpenAPI: 컨트롤러 API가 없고 MCP 엔드포인트와 `/health`만 있으므로 넣지 않는다.
  - Mod 쪽에는 Generic Host와 Serilog를 쓰지 않는다. KSP 안에서 도는 플러그인이라 서버·콘솔 템플릿의 대상이 아니다. 로그는 KSP `Debug.Log`를 쓴다.
