# 조립 점검: 스테이지별 Δv·TWR과 구조 점검 계획

## 요약

새 모듈 쌍 `check`(Mod `KspBridge.Mod.Modules.Check`, MCP 서버 `KspBridge.Server.Modules.Check`)를 더하고, MCP 도구 `check_ship` 하나로 에디터에서 조립 중인 기체를 점검한다(D001·D002). 아래 줄 번호는 KSP 1.12.5 `Assembly-CSharp.dll`을 `ilspycmd -t <형식>`으로 디컴파일한 파일 기준이다.

점검은 게임 내장 Δv 계산기(`VesselDeltaV`)가 계산을 마친 뒤의 값을 읽어 만든다(D003). 계산이 여러 프레임에 걸치므로 #40 편집 도구와 같은 "시작 → 결과 되묻기" 방식을 쓴다.

1. **시작(`check/start`)**: 준비 상태를 확인한다(C10, 결정 "준비 판정"). 지정 천체를 찾는다(C1). 그 천체가 게임 Δv 창의 천체(`DeltaVGlobals.DeltaVAppValues.body`)와 다르면 그 값을 지정 천체로 바꾼다(D023). 그다음 계산기 스테이지 정보 두 벌의 구간 기록을 비우고(D074, 필드를 찾지 못하면 `not_ready`) `ship.vesselDeltaV.SetCalcsDirty(resetPartCaches: false)`로 다시 계산시키고, 진행 중 점검(번호, 시작 시각, 원래 천체)을 남긴 뒤 `{check, pending: true}`를 돌려준다. 같은 천체여도 다시 계산시켜, 앞서 끝난 계산의 값을 읽지 않게 한다.
2. **완료 감지**: 모듈이 `GameEvents.onDeltaVCalcsCompleted`를 구독한다. 이 이벤트가 오면 진행 중 점검이 있고 그 시점에 계산기의 private `calcsDirty`가 false일 때 그 자리에서 결과를 만든다(D024). 결과를 만드는 일은 아래 "스냅숏"이다. 엔진이 있는 정상 경로는 결과 필드를 모두 채운 뒤 이벤트를 쏘고 그다음에 `simulation = null`을 하므로(`VesselDeltaV.cs` 3837~3856행), 이벤트 시점의 `SimulationRunning`은 늘 true라 조건에 쓰지 않는다. 계산 도중 기체가 바뀌거나 다시 계산하라는 표시가 들어와 `calcsDirty`가 true인 이벤트(엔진 없는 경로 3700~3704행의 `syncListInstances` 등)는 건너뛰고 다음 이벤트를 기다린다.
3. **천체 되돌리기**: 스냅숏을 만든 직후 `DeltaVAppValues.body`를 원래 천체로 되돌리고 다시 `SetCalcsDirty(false)`를 부른다. 그래서 게임 Δv 창은 다시 원래 천체의 값을 보인다. 시간 초과(10초), 장면 전환, 모듈 해제 때도 되돌린다.
4. **결과(`check/result`)**: MCP 서버 도구가 50 ms 간격으로 되묻는다. 아직이면 `pending`을 돌려주고, 끝났으면 결과를 돌려준 뒤 진행 중 점검을 지운다. 시작 뒤 10초가 지나도 끝나지 않았으면 천체를 되돌리고 `not_ready`를 돌려준다(C10). 되묻기가 끊겨 남은 점검(끝났지만 받아 가지 않았거나 10초가 지난 점검)은 다음 `start`가 정리한다(결정 "진행 중 점검은 하나만").
5. **스냅숏 → 판정**: 스냅숏은 계산기와 기체에서 KSP에 의존하지 않는 순수 자료(`ShipData`)를 만든다. 스냅숏은 계산기 값을 옮겨 담은 뒤 순수 판정 `BurnSignal`로 엔진의 연소 스테이지를 채우고(D067), 판정(`Checks`)은 그 자료만으로 스테이지 값, 무게중심 어긋남, 경고를 만든다. 판정은 KSP 없이 단위 테스트한다.
6. **서버 호스트 MCP 전송(C12, D049·D050·D061)**: 서버를 최신 프로토콜(`2026-07-28`) 기준의 Stateless 모드(SDK 기본값)로 두고, `subscriptions/listen` 처리기를 직접 두어 서버 모듈 세대가 바뀔 때 열린 listen 스트림으로 `notifications/tools/list_changed`를 보낸다. 이전 프로토콜 버전과의 호환은 두지 않는다.

## 바꿀 파일·구조

- `src/KspBridge.Mod.Modules.Check/KspBridge.Mod.Modules.Check.csproj`(새 파일): Wait 모듈 csproj와 같은 구성(net472, `Assembly-CSharp`·`UnityEngine.CoreModule` 참조, Core·Loader 프로젝트 참조).
- `src/KspBridge.Mod.Modules.Check/CheckModule.cs`(새 파일): `IBridgeModule` 구현(`Name = "check"`). 처리기 `start`·`result` 등록, 이벤트 구독(`onDeltaVCalcsCompleted`, `onEditorRestart`, `onEditorLoad`, `onGameSceneLoadRequested`), 준비 판정.
- `src/KspBridge.Mod.Modules.Check/CheckRunner.cs`(새 파일): 진행 중 점검 하나를 관리한다(KSP 쪽). 천체 바꾸기·되돌리기, 다시 계산시키기 직전 구간 기록 비우기(`stageInfoEntries` 리플렉션, D074), 완료 판정(`calcsDirty` 리플렉션 읽기), 스냅숏 호출, 시간 초과.
- `src/KspBridge.Mod.Modules.Check/PendingCheck.cs`(새 파일): 진행 중 점검의 상태 기계(대기 → 완료/시간 초과/취소)를 시계를 주입받는 순수 클래스로 둔다. 시간 초과 경로를 단위 테스트하기 위함이다.
- `src/KspBridge.Mod.Modules.Check/ShipSnapshot.cs`(새 파일): `ShipConstruct`·`VesselDeltaV`·부품 모듈에서 `ShipData`를 만든다(KSP 쪽).
- `src/KspBridge.Mod.Modules.Check/ShipData.cs`(새 파일): 순수 자료 모델. 부품(식별자, 부모, 질량, 무게중심 위치, 분리기 여부와 작동 스테이지·떼어 내는 쪽, 스테이지별 연료 질량 시작·끝, 실린 자원 이름, 조종·승무원·낙하산 여부), 분리기가 떼어 내는 쪽을 고르는 순수 규칙(omni 여부, 자기 식별자, 부모, 분리 노드에 붙은 부품, 자식들 → 떼어 내는 루트 목록. `ShipSnapshot`이 게임 값을 넘겨 부른다, D085), 엔진(작동 스테이지, 스테이지별 연소 여부(D065), 추진제, 추력 작용점·방향·진공 추력(추력 제한 반영), 유효 짐벌 각), 계산기 스테이지 값(구간 기록마다 작동 엔진 식별자·시작 질량·끝 질량 포함, D067), 시설.
- `src/KspBridge.Mod.Modules.Check/BurnSignal.cs`(새 파일): 순수 판정. 계산기 스테이지 값의 구간 기록으로 엔진마다 연소 스테이지 집합을 채운다(결정 "엔진·추력 정보"의 연소 여부, D065·D067). 스냅숏이 `ShipData`를 다 만든 뒤 부른다.
- `src/KspBridge.Mod.Modules.Check/Checks.cs`(새 파일): 순수 판정. 스테이지 표(C2), 무게중심 어긋남(C6), 경고 8종(C5·C6·C7), 응답 사전 만들기(C4).
- `src/KspBridge.Mod.Modules.Check/Readiness.cs`(새 파일): 순수 준비 판정(D042). 결정 "준비 판정"의 조건 값(게임 로드, 코어 로드 표시 사유, 장면, `fetch.ship` 있음, `EditorDriver.fetch` 있음·`restartingEditor`, 마지막 이벤트 뒤 프레임 수, 부품 시작, `sortRoutine` null)을 입력으로 받아 준비 여부와 `not_ready` 사유를 돌려준다. 게임 값은 `CheckModule`이 읽어 넘긴다.
- `src/KspBridge.Mod.Modules.Check/Vec.cs`(새 파일): 판정에 쓰는 순수 3차원 벡터(더하기, 내적, 길이, 단위 벡터). 단위 벡터는 길이가 0일 때만 영벡터다(D088). editor 모듈의 `Geo.cs`는 다른 어셈블리이고 C11로 바꾸지 않으므로 필요한 만큼만 따로 둔다.
- `src/KspBridge.Server.Modules.Check/KspBridge.Server.Modules.Check.csproj`(새 파일): Wait 서버 모듈 csproj와 같은 구성.
- `src/KspBridge.Server.Modules.Check/CheckTools.cs`(새 파일): `[assembly: KspBridgeModule("check")]`, MCP 도구 `check_ship`. Mod `check/start`를 부른 뒤 `check/result`를 50 ms 간격으로 되묻는다. 서버 쪽 상한은 Mod 시간 초과(10초)보다 넉넉한 15초로 두고, 그 상한을 넘기면 `not_ready`를 돌려준다.
- `tests/KspBridge.Mod.Modules.Check.Tests/KspBridge.Mod.Modules.Check.Tests.csproj`(새 파일): editor 테스트 프로젝트와 같은 구성(MSTest, net472).
- `tests/KspBridge.Mod.Modules.Check.Tests/ChecksTests.cs`(새 파일): `ShipData`를 직접 만들어 경고 8종의 나옴·안 나옴, 배타 규칙, 스테이지 표, 무게중심·짐벌 계산을 확인한다.
- `tests/KspBridge.Mod.Modules.Check.Tests/BurnSignalTests.cs`(새 파일): 계산기 스테이지 값을 직접 만들어 연소 스테이지 집합을 확인한다(D067).
- `tests/KspBridge.Mod.Modules.Check.Tests/PendingCheckTests.cs`(새 파일): 시간 초과·완료·취소 상태 전이.
- `tests/KspBridge.Mod.Modules.Check.Tests/ReadinessTests.cs`(새 파일): 조건마다 하나만 어긋난 입력이 준비 안 됨이고 모두 맞으면 준비됨(특히 `restartingEditor` true, `EditorDriver.fetch` 없음; D042).
- `KspBridge.slnx`: 새 프로젝트 셋 추가.
- `ReadMe.md`: 도구 목록에 `check_ship` 추가, 점검 동안 게임 Δv 창의 천체가 잠깐 바뀐다는 설명. 서버 구조와 서버 실행 절의 MCP 문장 세 곳을 최신 프로토콜 기준으로 고쳐 쓴다(D054·D061): 고정 호스트는 포트를 유지하고, 모듈 세대 교체 알림은 `subscriptions/listen`으로 구독한 클라이언트에 가며, 서버 호스트를 다시 실행한 뒤 모듈 게시 알림이 오지 않으면 Claude Code에서 MCP를 다시 연결한다.
- `src/KspBridge.Server/Program.cs`: MCP 전송 설정만 바꾼다(C11·C12). `SessionMode`를 Stateless로, `ConfigureSessionOptions`에서 공유 도구 컬렉션·`Tools.ListChanged`는 그대로 두고 `Handlers.SubscriptionsListenHandler`에 아래 처리기를 넣는다.
- `src/KspBridge.Server/ToolListSubscriptions.cs`(새 파일): `subscriptions/listen` 처리기와 열린 listen 스트림 목록. 모듈 호스트 공유 도구 컬렉션의 변경(`Changed`)마다 열린 스트림 모두에 `notifications/tools/list_changed`를 보낸다. 열린 스트림마다 30초 간격으로 유지 신호 `notifications/kspbridge/keepalive`를 보낸다(D051). 스트림이 열리고 닫힐 때마다 정보 로그를 남긴다(D058). 확인 응답 전송 직후 불리는 테스트용 internal 이음매를 둔다(비어 있으면 동작 그대로, D091). 이 이음매는 등록을 확인 응답 바로 뒤로 옮기는 되돌림을 잡지 못하는 한계가 있으나 보류한다(D097).
- `src/KspBridge.Server/Modules/ModuleHost.cs`: 주석만 고친다. "모든 MCP 세션이 공유"를 세션 없는 지금에 맞게 "모든 MCP 요청이 공유"로 바꾼다(C11·D079). 동작은 바뀌지 않는다.
- `tests/KspBridge.Server.Tests/McpEndpointTests.cs`: 도구 목록 변경 알림 시험을, 최신 프로토콜 클라이언트가 `subscriptions/listen`으로 구독한 뒤 게시하면 그 스트림으로 `list_changed`가 오고 새 도구를 부를 수 있는지로 바꾼다.
- `docs/features/server-hosting.md`: 마지막 구현 단계에서 R5(서버 호스트가 실행 중인 동안 게시한 서버 모듈이 실행 중인 Claude Code에 닿음, 이번 C12)를 더하고 이력 한 줄, `파일:` 줄에 새 파일을 적는다. `파일:` 줄에 주석을 고친 `src/KspBridge.Server/Modules/ModuleHost.cs`도 더한다(D079).
- `docs/features/assembly-check.md`(새 파일): 새 기능 assembly-check의 기준 spec. 마지막 구현 단계에서 이번 spec C1~C11을 R1~R11로 쓰고, 이력 한 줄과 `파일:` 줄을 적는다.

## 결정

- **새 모듈 쌍 `check`와 도구 `check_ship`**: 조립 편집과 구분된 점검 도구를 Planner에게 준다(D001). 서버 모듈은 `KspBridge.Server.Modules.*` 와일드카드로, Mod 모듈은 `KspBridge.Mod.Modules.*` 와일드카드로 게시 대상에 자동으로 들어간다(`Directory.Build.targets` 25~26행). 그래서 빌드 설정은 바꾸지 않는다. / 검토한 대안: 도구 이름 `editor_check`. 에디터 장면에서만 쓰지만 editor 모듈 도구와 헷갈리지 않게 모듈 이름을 접두어로 쓰는 기존 규칙(`submit_wait`, `editor_*`)에 맞춰 `check_ship`으로 했다.
- **지정 천체 계산**: 게임 Δv 창의 천체를 점검 동안만 바꿔 게임 계산기로 해면 값을 얻고 되돌린다(D023). 근거:
  - 에디터에서 해면 Isp는 `DeltaVGlobals.DeltaVAppValues.body`의 해면 기압으로 계산된다(`DeltaVEngineInfo.cs` 1720~1796행).
  - 해면 추력도 같은 천체로 계산된다(`DeltaVEngineInfo.cs` 2089~2100행 `CalculateThrustVector`).
  - TWR은 같은 천체의 `GeeASL`로 계산된다(`DeltaVStageInfo.cs` 3363~3470행 `CalculateTWR`: `TWRVac = thrustVac / (startMass × g0 × GeeASL)`).
  - 게임 Δv 창도 천체를 바꿀 때 같은 값을 쓰고 `SetCalcsDirty(false)`를 부른다(`DeltaVAppSituation.cs` 695~806행). 이 창은 `Update`가 없어 주기적으로 덮어쓰지 않는다.
  - 천체 이름은 `FlightGlobals.Bodies`의 `bodyName`과 대소문자 무시로 비교하고, 기본값은 `FlightGlobals.GetHomeBody()`다(C1).
- **완료 판정(D024)**: 계산이 끝났는지는 계산 완료 이벤트 `GameEvents.onDeltaVCalcsCompleted`(3704·3854행)가 왔고 그 순간 private `calcsDirty == false`(77행, `RunCalculations` 시작에서 false가 됨, 3663행)인 것으로 판정한다. `IsReady`는 처음 한 번 true가 되면 다시 false로 바뀌지 않아(139·3853행), `SimulationRunning`(114~137행)은 정상 경로에서 이벤트 뒤에야 false가 되어(3837~3856행) 쓰지 않는다. 우리가 `SetCalcsDirty`를 부른 뒤의 이벤트만 받는다(시작 때 진행 중이던 계산의 이벤트는 그 계산이 시작될 때 `calcsDirty`를 내렸지만 우리 요청으로 다시 true가 되었으므로 걸러진다).

  `calcsDirty`는 리플렉션으로 읽기만 한다. 필드를 찾지 못하면(다른 게임 버전) `start`가 `not_ready`와 이유를 돌려주고 아무것도 바꾸지 않는다. #40 `StageManager.sortRoutine`과 같은 처리다.
- **계산기 없음·꺼짐**: 다음 경우에는 `not_ready`와 이유를 돌려준다.
  - `ship.vesselDeltaV`가 null인 경우(`ShipConstruct.cs` 43행. 에디터 기체에 부품이 생길 때 만들어짐, 2674~2689행)
  - `DeltaVGlobals.DoStockSimulations`가 false인 경우(게임 설정에서 Δv 계산을 끈 경우)

  부품이 없는 기체는 계산기 없이 스테이지 없음·경고 없음으로 바로 답한다(C9).
- **준비 판정(C10)**: 다음을 모두 만족해야 준비된 것으로 본다. 이 신호는 editor 모듈(`ShipEditor.IsSettled`)과 같은 판정에 게임 안 시설 전환 조건(D041)을 더한 것을 게임에서 보이는 신호만으로 다시 구현한 것이다. editor 모듈의 상태를 보지 않으므로 C11과 맞는다(D006).
  - 게임이 로드되어 있고 코어 장면 로드 표시가 없다(`HighLogic.CurrentGame != null`, `context.Session.BlockReason() == null`; editor 모듈 `RequireGame`과 같음, 코어는 읽기만 함; D040).
  - 에디터 장면이고 `EditorLogic.fetch.ship`이 있다.
  - 게임 안 시설 전환 중이 아니다(`EditorDriver.fetch != null && !EditorDriver.fetch.restartingEditor`; public 필드. `SwitchEditor` 요청 때 true, 새 기체의 `onEditorStarted`에서 false: `EditorDriver.cs` 25·555·604행; D041).
  - 마지막 `onEditorRestart`·`onEditorLoad` 이벤트(craft 다시 불러오기·새 기체·게임 안 시설 전환 때 남. MCP `editor_enter`의 장면 다시 불러오기는 `StartEditor(isRestart: false)`라 나지 않음(`EditorLogic.cs` 1067·1142행). `EditorLogic.cs` 14130행대 `ShipToLoadSelected`가 `StartEditor(isRestart: true)` 뒤 `onEditorLoad`)로부터 3프레임 이상 지났다.
  - 모든 부품이 `Part.editorStarted`다.
  - `StageManager`의 private `sortRoutine`이 null이다.

  #40 편집은 craft를 다시 불러오는 방식이라, 편집 도중에는 이 판정이 준비되지 않음으로 나온다. 시설 전환은 두 경로다. 게임 안 버튼·단축키는 같은 장면 안 `EditorDriver.SwitchEditor` 코루틴이라 여러 프레임 동안 이전 기체가 그대로 있고, 위 `restartingEditor` 조건이 막는다(D041). MCP `editor_enter`는 장면 다시 불러오기다. 이 경우 게임은 요청 시점에 `LoadedScene`과 `EditorDriver.editorFacility`를 바꾸고 이전 `EditorLogic.fetch`는 이전 장면이 내려갈 때(`OnDestroy`)에야 없어지므로, 그 사이 구간은 위 장면·`fetch` 조건으로는 막히지 않는다. 요청 시점에 `onGameSceneLoadRequested`가 와서 코어 장면 로드 표시(`FlightSession.Loading`)가 켜지므로 첫 조건이 막는다(`EditorDriver.StartEditor`, `HighLogic.SetLoadSceneEventsAndFlags`·`setLevelFlags`; D040).
- **진행 중 점검은 하나만**: `start`는 먼저 남은 점검을 정리한다. 끝났지만 결과를 받아 가지 않은 점검과 시작 뒤 10초가 지난 대기 점검은 천체를 되돌리고(대기 점검만 해당) 지운 뒤 새 점검을 시작한다. 10초 안의 대기 점검이 있으면 `not_ready`("다른 점검이 진행 중")를 돌려준다. 서버 도구는 한 호출 안에서 시작부터 결과까지 마치므로, 거절은 도구를 동시에 부를 때만 생기고, MCP 호출 취소·서버 재시작으로 되묻기가 끊겨도 다음 호출이 막히지 않는다(C10).
- **스테이지 값(C2)**: 스테이지마다 계산기 `vesselDeltaV.GetStage(n)`(`VesselDeltaV.cs` 4754행)의 `DeltaVStageInfo` 필드를 쓴다. 쓰는 필드는 다음과 같다(`DeltaVStageInfo.cs` 29~71행).
  - 질량: `startMass`, `endMass`. 연료·건조 질량은 계산기 `fuelMass`·`dryMass`를 쓰지 않는다. `fuelMass`는 비연료 자원과 그 스테이지 시작 때 떨어지는 부품의 연료까지 더하기 때문이다(`DeltaVStageInfo.cs` 598~621·3018~3029행, `DeltaVPartInfo.cs` 1021행). 대신 두 경로(엔진 있음·없음) 모두 `fuel_mass_t` = 스테이지 시작 때 남아 있는 부품의 연료 질량(결정 "부품의 연료 질량", D027·D028) 합, `dry_mass_t` = `start_mass_t − fuel_mass_t`(비연료 자원을 포함한 연료가 아닌 질량)로 구한다(D031). 엔진이 있는 경로의 "남아 있는 부품"은 `startMass`와 같은 집합, 곧 계산기 `DeltaVPartInfo.decoupleStage < s`인 부품이다(`startMass`는 `decoupleStage <= s`인 부품을 넣고 `== s`인 부품의 건조·자원 질량과, 스테이지 s에서 투하된 모듈 질량을 뺀다: `DeltaVStageInfo.cs` 465·611~620·3030~3062행). "스테이지 s의 부품 건조 질량"은 `part.mass`에서, 그 스테이지에서 이미 투하된 질량을 뺀 값이다(D033). 투하 질량은 질량 변경 시점이 `ModifierChangeWhen.STAGED`인 `IPartMassModifier` 모듈의 작동 전 질량 합이다(계산기 `DeltaVPartInfo.jettisonMass`, `DeltaVPartInfo.cs` 641~676행). 에디터에서 0이 아닌 값을 내는 것은 페어링(`ModuleProceduralFairing`, 작동 전 `panelMass`)이다. 엔진 덮개(`ModuleJettison`)는 이미 투하된 뒤에만 값을 내므로 에디터에서는 0이다. 투하 여부는 모든 경로에서 계산기 부품 값으로 판정한다(D035·D036). 계산기는 엔진 유무를 따지기 전에 부품 정보(`activationStage`, `decoupleStage`, `isJettison`, `jettisonMass`)를 채우므로(`VesselDeltaV.cs` 3663~3666행 `UpdateStageInfo` → 2121행 `ResetPartInfo`) 엔진이 없는 기체에도 이 값이 있다. 엔진이 없는 기체에서 비는 것은 스테이지 값과 `stageFuelMass`뿐이다.
    - 엔진 있는 스테이지, 무게중심: 계산기 `DeltaVPartInfo.JettisonInStage(s)`(`isJettison && decoupleStage < s && activationStage >= s`, 975~1018행)를 그대로 쓴다. 그래서 무게중심 질량 합이 `start_mass_t`와 같다.
    - 엔진 없는 경로: `isJettison && activationStage >= s`로 판정한다. 부품 집합은 결정 "분리 단위·남은 부품"의 집합이므로 `decoupleStage < s` 조건은 집합 소속이 대신한다.
    - 엔진이 없는 경로의 "남아 있는 부품"은 결정 "분리 단위·남은 부품"의 집합이다(D032). 그래서 두 경로 모두 `start = dry + fuel`이 늘 성립하고, 엔진이 있는 경로의 `dry_mass_t`는 Δv·TWR에 쓰인 시작 질량과 같은 부품 집합에서 나온다.
  - Δv: `deltaVatASL`, `deltaVinVac`
  - TWR: `TWRASL`, `TWRVac`
  - 연소 시간: `stageBurnTime`

  스테이지 번호 집합은 `editor_get_stages`와 같은 규칙(스테이지 대상 부품의 `inverseStage`)으로 만든다. 계산기에 그 번호의 스테이지 정보가 없으면 질량은 이전 스테이지 끝 질량으로 채우고 Δv·TWR·연소 시간은 0으로 둔다. 계산기의 작동 엔진이 하나도 없는 기체는 계산기가 스테이지 시뮬레이션을 건너뛰고 바로 완료 이벤트를 쏘므로(`VesselDeltaV.cs` 3666~3704행) 스테이지 값과 `stageFuelMass`가 채워지지 않는다(0 또는 앞선 계산의 값). 이 경우 계산기 스테이지 값·`stageFuelMass`를 쓰지 않고, 스테이지마다 그 스테이지에 남아 있는 부품(결정 "분리 단위·남은 부품")의 스테이지 s의 부품 건조 질량(D033)과 자원 질량으로 시작 질량 = 끝 질량, 건조 질량, 연료 질량(결정 "부품의 연료 질량"의 자원 구분)을 구한다. 소모가 없으므로 정확하다. Δv·TWR·연소 시간은 0이다. 엔진 스테이지(추력 엔진이 하나 이상인 스테이지)가 아닌 스테이지는 Δv·TWR·연소 시간을 계산기 값과 상관없이 0으로 둔다(C2·D045). 계산기는 이런 스테이지에도 연소 시간(낙하산·분리 스테이지 5.0 s)과 TWR(연료가 바닥난 스테이지)을 낸다(T006 진단). 질량은 계산기 값 그대로다. 합계는 반올림한 스테이지 값의 합이다(D078. 응답의 스테이지 값을 더하면 합계와 같다). 값은 Δv·연소 시간 소수 첫째 자리, 질량 소수 셋째 자리, TWR 소수 둘째 자리로 반올림한다. 같은 기체면 같은 결과가 나와야 하므로(C9) 반올림 뒤 값을 돌려준다.
- **엔진·추력 정보**: 엔진마다 계산기 `DeltaVEngineInfo`(엔진 정보 목록은 `VesselDeltaV.OperatingEngineInfo`, 88행)에서 다음을 읽는다.
  - 진공 추력 `thrustVac`: `MaxThrustOutputVac(useThrustLimiter: true)`로 계산되어 추력 제한을 반영한다(D013, `ModuleEngines.cs` 4551행). 이 함수는 추진제에 `IntakeAir`가 있는 공기 흡입 엔진에 0을 돌려준다(4551~4570행).
  - 해면 추력 `thrustASL`: 지정 천체 해면 조건의 `MaxThrustOutputAtm(useThrustLimiter: true)`(`DeltaVEngineInfo.cs` 2089~2100행). C6의 엔진 추력은 `thrustVac`를 쓰고, `thrustVac`가 0인 공기 흡입 엔진만 `thrustASL`을 쓴다(D029). 그래도 스테이지의 엔진 추력 합이 0이면(해면 추력도 0인 경우) 작용점을 정할 수 없으므로 그 스테이지는 어긋남 값을 주지 않고 경고도 내지 않는다(0/0을 만들지 않음).
  - 스테이지별 연소 여부(D065·D074): 엔진이 스테이지 s에서 연소한다는 것은, 계산기 스테이지 s의 구간 기록 `DeltaVStageInfo.deltaVCalcs` 가운데 그 엔진이 `activeEngines`에 있고 `endMass < startMass`인 구간이 있다는 것이다. 엔진은 부품 `craftID`로 맞춘다. 구간 기록은 점검이 다시 계산시키기 직전에 비우므로(결정 "구간 기록 비우기") 늘 점검이 받는 계산의 것이다.
    - 질량 비교의 근거: 구간 Δv는 `log(startMass / 현재 질량)`이므로(`DeltaVStageInfo.cs` 624~651행), `endMass < startMass`는 계산기가 그 구간에 Δv > 0을 냈다는 것과 같은 판정이다. 계산기는 연료를 다 태운 다음 스테이지에도 엔진을 작동 엔진에 남긴다(`PropellantStarved`는 `amountAvailable <= 0`일 때만 참, `DeltaVEngineInfo.cs` 246~285행). 그 스테이지에서 부동소수점 찌꺼기(연소 시간 약 1e-7 s)를 태우지만, 그 질량(약 2e-9 t)은 float 시작 질량에서 연소량을 빼는 계산(`GetCurrentStageMass`, `DeltaVStageInfo.cs` 3075~3108행)에 흡수되어 시작 질량 = 끝 질량, Δv 0이 된다(시험 2단 로켓·ComSat Lx 진단). 그래서 `stageBurnTotals[s].maxTotalBurnTime`(그 시점 연결된 연료로 탈 수 있는 시간, `DeltaVEngineInfo.cs` 1259·1300~1315행)과 `enginesActiveInStage`(찌꺼기 스테이지에도 엔진이 남음)는 연소 신호로 쓰지 않는다.
    - 구간은 사건이 생길 때 기록되고(엔진이나 탱크가 바닥남, 시간 단계가 줄어듦: 2040~2070행), 기록 때 그때의 작동 엔진 목록을 복사한다(`DeltaVCalc.cs` 63행). 바닥난 엔진은 기록 뒤에 목록에서 빠지므로(2072~2074행) 한 엔진이 먼저 바닥나도 그 엔진은 자신이 바닥난 구간까지만 들어간다.
    - 판정은 KSP에 의존하지 않는 `BurnSignal`이 `ShipData`의 계산기 스테이지 값만으로 한다(D067). 스냅숏은 계산기 값을 옮겨 담기만 한다.
- **구간 기록 비우기(D074)**: `CheckRunner`는 `SetCalcsDirty`를 부르기 직전에 계산기 스테이지 정보 두 벌 모두의 `deltaVCalcs`를 비운다. 근거:
  - 계산기는 작동 엔진이 있는 스테이지에서만 구간 기록을 비우고 다시 채운다(`DeltaVStageInfo.cs` 1210~1257행). 클램프만 든 스테이지 등 건너뛰는 스테이지는 비우지 않고(1232~1254행 → 2373행 `IL_0095`), 스테이지 정보 객체를 다시 쓰는 `Reset`도 구간 목록이 없을 때만 새로 만든다(265~276행). 그래서 스테이지를 바꾸거나 엔진을 지운 뒤에는 지난 계산의 구간이 남을 수 있고, 건너뛰는 경로는 게임 안에 여럿이다(분리기·도킹 포트 부품의 `activationStage` 조정 등). 경로마다 거르지 않고 남는 원인을 없앤다.
  - 스테이지 정보는 두 벌(`stageInfo_InstanceOne`·`stageInfo_InstanceTwo`)이고, `RunCalculations`는 보이는 쪽(`operatingIndex`)이 아닌 벌을 골라(`VesselDeltaV.cs` 3605~3639행, `useMultipleInfoLists` 기본값 true 37행) 준비하고(3664~3665행) 스테이지마다 `SimulateDeltaV`를 돌린 뒤(3714~3812행, 스테이지마다 3811행에서 한 프레임 쉼) 보이는 쪽을 그 벌로 바꾼다(3834행). 계산이 끝나면 두 색인이 같아져 공개 속성 `WorkingStageInfo`·`OperatingStageInfo`는 같은 벌을 돌려주므로(`DeltaVEngineStageSet.cs` 124~202·204~435행), 다음 계산이 쓸 벌까지 비우려면 private `stageInfoEntries`(104행)를 리플렉션으로 읽어 두 벌을 모두 비운다. 항목의 한쪽 벌은 없을 수 있으므로(`AddStageInfo`는 한쪽 벌만 채움: 계산 중이면 working, 아니면 operating 쪽, 594~646행. null 대입 1159·1185·1222·1248행) 없는 벌은 건너뛴다.
  - 게임에서 `deltaVCalcs`를 쓰는 곳은 `DeltaVStageInfo` 안(생성자, `Reset`, 시뮬레이션의 기록 2070행·합산 2238~2279행)뿐이다(`Assembly-CSharp` 전체 디컴파일에서 확인). Δv 창은 합산 결과 필드(`deltaVinVac` 등)를 보므로 비워도 표시는 바뀌지 않는다. 작동 엔진이 있는 스테이지는 어차피 스스로 비우고 다시 채우고, 건너뛰는 스테이지는 빈 목록이 맞는 값이다. 점검 시작 때 진행 중이던 계산이 비운 뒤에 쓴 구간은 그 계산의 벌에 남지만 읽지 않는다. 그 계산이 끝나면 그 벌이 보이는 쪽이 되고(3834행) 그 완료 이벤트는 `calcsDirty`로 걸러지며(결정 "완료 판정"), 점검이 받는 다음 계산은 비운 다른 벌을 써서(3622~3639행) 끝나면 그 벌이 보이는 쪽이 되고, 스냅숏은 `GetStage`(4754행)로 보이는 쪽 벌만 읽는다. 같은 기체라면 그 계산은 작동 스테이지를 스스로 다시 채운다. 한계: 점검 도중 사용자나 다른 도구가 기체를 편집해 작동 스테이지 구성이 바뀌고, 받아들여지는 계산 전에 계산이 하나 더 돌면, 앞 계산이 쓴 벌이 읽혀 그 벌의 건너뛴 스테이지에 구간이 남을 수 있다(점검 중 편집하는 경우만, D074).
  - `stageInfoEntries`·그 항목의 두 필드를 찾지 못하면(다른 게임 버전) `start`가 `not_ready`와 이유를 돌려주고 아무것도 바꾸지 않는다(`calcsDirty`와 같은 처리).

  추력 작용점과 방향은 게임 에디터 추력 중심 표시와 같은 `ModuleEngines.OnCenterOfThrustQuery`로 얻는다(5029행, `pos` = 추력 변환 위치 가중합, `dir` = 배기 방향). 힘의 방향은 `-dir`이다(`ModuleEngines.cs` 3605행 `AddForceAtPosition(-transform.forward × …)`).

  추력 엔진(D010·D014)은 그 스테이지에서 위 연소 여부가 참인 엔진이다(D065).
- **유효 짐벌 각(C6)**: `ModuleGimbal`이 있고 `gimbalLock`이 false인 엔진만 짐벌 회전력을 낸다. 유효 각은 켜진 축(`enablePitch`·`enableYaw`)의 범위(`gimbalRangeXP`·`XN`·`YP`·`YN`. 에디터 부품은 `OnLoad`가 끝난 상태라 필드 값을 그대로 쓴다. `OnLoad`는 음수를 XP·YP→`gimbalRange`, XN→XP, YN→YP로 채운다. `ModuleGimbal.cs` 17~36·693~730행) 가운데 가장 작은 값 × `gimbalLimiter` / 100이다. 축이 하나라도 꺼져 있으면 0이다. 어긋남 방향과 상관없이 막을 수 있는 회전력의 하한으로 판정해, 상쇄할 수 없는데 경고를 놓치는 일이 없게 한다.
- **스테이지별 무게중심(C6)**: 게임 에디터 무게중심 표시(`EditorMarker_CoM.findCenterOfMass`)와 같은 규칙을 쓴다. 부품 위치는 `transform.position + rotation × CoMOffset`이고, 물리 비중요 부품(`physicalSignificance != FULL`)은 부모 위치를 쓴다. 질량은 스테이지 s의 부품 건조 질량(결정 "스테이지 값", D033) + 그 스테이지 시작 때의 자원 질량(계산기 `DeltaVPartInfo.stageFuelMass[stage].startMass`, `DeltaVPartInfo.cs` 1049~1062행)이다. 넣는 부품은 `start_mass_t`와 같은 계산기 집합, 곧 `DeltaVPartInfo.decoupleStage < s`인 부품이다(D034). 계산기는 `decoupleStage <= s`인 부품에만 시작 자원 질량을 기록하므로(`DeltaVStageInfo.cs` 465·997~1001행) 이 집합의 값은 늘 있다. 무게중심은 추력 합이 0보다 큰 엔진 스테이지에서만 구하므로 계산기 값이 늘 있다. 그래서 무게중심 질량 합이 `start_mass_t`와 같다.
- **분리 단위·남은 부품(D022·D026)**: 분리 단위는 부품 트리에서 직접 구한다. 스테이지 대상 분리 부품(`ModuleDecouple`, `ModuleAnchoredDecoupler`, 스테이지에 놓인 분리 가능한 `ModuleDockingNode`)마다, 그 분리기가 작동 스테이지(`inverseStage`)에 떼어 내는 쪽 가운데 루트 반대쪽 부품 묶음이 그 스테이지에 떨어진다. 게임이 실제로 떼는 쪽만 따른다(D077): 분리 노드(`ModuleDecouple`·`ModuleAnchoredDecoupler`의 `explosiveNode`, 도킹 포트의 기준 노드)에 붙은 부품이 없으면 아무것도 떼지 않고(`ModuleDecouple.cs` 249행, `ModuleAnchoredDecoupler.cs` 120행, `ModuleDockingNode.Decouple`), `isOmniDecoupler`인 분리기는 자식마다 떼고 부모가 있으면 자기 자신도 부모에서 뗀다(`ModuleDecouple.cs` 163~224행). 떼어 내는 쪽을 고르는 이 규칙은 `ShipData.cs`의 순수 함수로 두고, `ShipSnapshot`은 게임 값(omni 여부, 부모, 분리 노드에 붙은 부품, 자식들)만 넘긴다(D085). 부품마다 "떨어지는 스테이지"는 루트까지 가는 길에서 그 부품 쪽을 떼어 내는 분리기들 가운데 `inverseStage`가 가장 큰(가장 먼저 작동하는) 분리기의 스테이지다. 길 위의 분리기 하나라도 작동하면 그 부품은 떨어지기 때문이다(예: 엔진 → 분리기 X(스테이지 0) → 탱크 → 분리기 Y(스테이지 4) → 루트이면 엔진도 스테이지 4에 떨어짐). 그런 분리기가 없으면 끝까지 남는다. 분리기의 "스테이지에 놓임"은 모든 분리기 종류에 `editor_get_stages`와 같은 부품 스테이지 대상 판정식(`(hasStagingIcon || stackIcon.StageIcon != null) && stagingOn`)으로 정한다(D047). 그래서 스테이징이 꺼진 도킹 포트는 실제 스테이징으로 분리되지 않으므로 분리기로 보지 않는다(`IsStageable()`은 모듈 필드 `staged`만 돌려줘 스테이징을 꺼도 참이라 쓰지 않는다: `ModuleDockingNode.cs` 5677행. 실제 분리는 모듈 필드 `staged`와 `stagingEnabled`가 모두 참일 때만 일어난다: 5441행. T006 덤프에서 꺼진 포트는 `IsStageable()` 참, `stagingEnabled` 거짓, 부품 스테이지 대상 판정식 값 거짓). 게임 계산기는 이런 포트도 분리기로 봐 그 아래 부품을 그 스테이지 시작 질량에서 빼지만(T006 진단: `dockingPort2` 아래 `decoupleStage` 1), 구조 경고는 실제 스테이징을 따르고 Δv·질량은 계산기 값을 따른다(D046). 엔진이 있는 경로의 연료 질량과 무게중심은 계산기 집합(D032·D034)이라 시작 질량과 서로 맞는다. 발사 클램프(`Part.isLaunchClamp()`)는 계산기처럼 질량·남은 부품 집합 어디에도 넣지 않는다(계산기 `ResetPartInfo`가 클램프를 건너뛰어 부품 정보를 만들지 않음: `VesselDeltaV.cs` 2367행; D037). 계산기 부품 정보가 없는 부품은 투하 대상이 아니다. 떨어지는 스테이지와 그 원인 분리기가 같은 부품들이 한 분리 단위이고, 끝까지 남는 부품 묶음이 하나의 분리 단위다. "스테이지 s 동안 기체에 남아 있음"은 떨어지는 스테이지 d에 대해 `d < s`(끝까지 남는 부품은 늘 남음)로 판정한다. 큰 번호가 먼저 작동하고, 스테이지 d가 작동할 때 떨어지는 부품은 그 뒤 스테이지에는 없다. 계산기의 `DeltaVPartInfo.decoupleStage`는 분리 구간 번호라 "떨어지는 스테이지"와 다르므로 분리 단위 판정에는 쓰지 않는다(519~575·796~880행: 끝까지 남는 부품이 -1인 것은 스테이지 0 엔진 예외뿐). 엔진이 있는 스테이지의 연료 질량만 계산기 시작 질량과 맞추려고 `decoupleStage`를 쓴다(결정 "스테이지 값", D032). 분리기 종류별로 떼어 내는 쪽(`ModuleDecouple`의 분리 노드·`isOmniDecoupler`, `ModuleAnchoredDecoupler`의 부모 쪽 분리)과 `stageFuelMass` 키의 의미는 tasks 단계 1(T006) 진단으로 확인했다(오류·위험).
- **연료 자원(D028)**: 연료는 기체 엔진의 추진제 자원 가운데 게임 Δv 계산기의 무시 목록 `DeltaVGlobals.PropellantsToIgnore`(기본값 `IntakeAir`·`ElectricCharge`, `DeltaVGlobals.cs` 128~184행)에 없는 자원이다. 계산기도 이 목록의 추진제를 소모 계산에서 건너뛴다(`DeltaVEngineInfo.cs` 254·442·893행). 목록은 점검마다 그때의 값을 읽는다.
- **부품의 연료 질량(D027)**: 계산기의 `stageFuelMass[s].startMass`·`endMass`는 그 부품의 모든 시뮬레이션 자원 질량 합이다(`DeltaVPartInfo.cs` 8~16·1021행). 그래서 부품의 스테이지 s 시작·끝 연료 질량은 이 값에서 연료가 아닌 자원(결정 "연료 자원"(D028) 기준으로 연료가 아닌 모든 자원. 무시 목록 추진제 `IntakeAir`·`ElectricCharge` 포함. 계산기는 무시 목록 추진제를 실제로 꺼내 쓰지 않음, `DeltaVEngineInfo.cs` 893~913행)의 질량, 곧 현재량 × `PartResourceDefinition.density`를 뺀 값으로 구한다. Δv 계산은 엔진 추진제만 소모하므로 연료가 아닌 자원은 계산 동안 양이 그대로다(`DeltaVEngineInfo`의 소모는 `propellantInfo`의 추진제만 요청함, 912행). 이 전제는 tasks 단계 1(T006) 진단으로 확인했다. `no_engine_fuel`의 "연료가 줄지 않음"과 `leftover_fuel`의 남는 질량은 이 연료 질량으로 판정한다. 부품에 스테이지 s의 `stageFuelMass` 키가 없으면(계산기는 `decoupleStage <= s`인 부품에만 기록하므로, D046으로 트리에서는 남지만 계산기에서는 분리된 부품) s보다 큰 번호 가운데 가장 가까운 키의 `endMass`를 s의 시작·끝 값으로 이어 본다(계산기가 그 뒤로 더 태우지 않은 것으로 봄). 키가 하나도 없으면 현재 자원 질량을 쓴다(D048). 무게중심(C6)에는 모든 자원 질량(빼지 않은 값)을 쓴다.
- **경고 판정(C5~C7)**: 모두 `ShipData`만으로 판정한다. 경고 코드는 다음과 같다.
  - `no_separation`: 엔진 E가 마지막으로 소모한 스테이지 s 다음의 엔진 스테이지 t(s보다 작은 번호 가운데 가장 큰 것) 동안 E가 남아 있다(E의 떨어지는 스테이지 d가 `d < t`이거나 끝까지 남음).
  - `fuel_blocked`: 스테이지 대상 엔진이 어느 스테이지에서도 연소 여부(결정 "엔진·추력 정보", D065)가 참이 아닌데, 그 엔진의 추진제 가운데 하나라도 그 엔진의 작동 스테이지 s(`inverseStage`) 동안 기체에 남아 있는 부품(결정 "분리 단위·남은 부품")에 실려 있다(D075). "실려 있음"은 그 엔진의 연료 추진제(결정 "연료 자원", 무시 목록 제외)를 에디터 현재량 0보다 크게 가진 부품의 스테이지 s 시작 연료 질량(결정 "부품의 연료 질량")이 0.001 t 반올림으로 0보다 큰 것이다(D080·D082, 결정 "판정 정밀도"). 부품의 연료가 모두 비었으면(앞 스테이지 엔진이 먼저 비운 탱크, float32 잔량) 세지 않는다. 그 스테이지 전에 떨어지는 부품의 연료는 세지 않는다. 그 스테이지에 받을 연료가 아예 없는 엔진은 경고하지 않고 스테이지 표의 Δv 0으로만 드러난다.
  - `no_engine_fuel`(엔진 없는 부분, D021): 연료가 실린 탱크가 남아 있는 마지막 스테이지까지 그 탱크의 연료 질량(결정 "부품의 연료 질량")이 전혀 줄지 않는다(첫 스테이지 시작과 떨어지기 직전 스테이지 끝 비교). 같은 분리 단위에 그 자원을 추진제로 쓰는 `fuel_blocked` 엔진이 있으면 그 탱크는 빼고, 그 엔진의 경고에 그 탱크 식별자를 함께 넣는다.
  - `com_offset`: "합성 추력 × 어긋남 거리"가 "짐벌 최대 회전력"보다 크다(용어 절 정의 그대로).
  - `no_control`: `ModuleCommand` 부품이 없거나, 끝까지 남는 분리 단위(결정 "분리 단위·남은 부품")에 `ModuleCommand` 부품이 없다.
  - `low_launch_twr`: 점검한 기체의 시설(`ship.shipFacility`)이 VAB이고 맨 먼저 작동하는 엔진 스테이지의 `TWRASL`(반올림 전)이 1보다 작다. 경고의 메시지와 `values.twr_asl`은 셋째 자리까지 보인다(D096)
  - `leftover_fuel`: 끝까지 남는 분리 단위의 탱크(연료(D028)가 실린 부품)에 마지막 스테이지 끝에도 연료 질량이 남아 있다. 연료 질량은 결정 "부품의 연료 질량"대로 구한다. 탱크별로 남는 질량(t)과 그 탱크에 실린 엔진 추진제 자원 이름을 준다(D025). 이미 `no_engine_fuel`·`fuel_blocked`로 알린 탱크는 뺀다.
  - `no_parachute`: `CrewCapacity > 0`인 부품이 있는데 `ModuleParachute` 부품이 없다.

  경고 응답은 `{code, stage?, parts[], message, values{}}`다.
- **판정 정밀도(D076)**: 경고 판정은 응답과 같은 반올림 값으로 한다. 남는 연료·어긋남·감소량처럼 값이 있어야 결함인 항목은 응답에 0으로 나오면 경고하지 않는다. `low_launch_twr`는 값이 작을수록 결함이라 0이어도 경고한다(D089). `com_offset`의 어긋남은 응답처럼 0.001 m로, `no_engine_fuel`의 연료 감소와 시작 연료(응답 `fuel_mass_t`가 0이면 알리지 않음, D087), `leftover_fuel`의 남는 연료, `fuel_blocked`의 "실려 있음"(스테이지 시작 연료 질량, D080)은 응답처럼 0.001 t로 반올림한 값으로 판정한다. `low_launch_twr`의 해면 TWR은 반올림 전 값으로 판정한다. 0.995~0.999도 실제로 이륙하지 못하는 값이기 때문이다(D096). 근거: 대칭 기체의 어긋남은 1.1e-7~3.5e-7 m로 게임 위치(float32 `Vector3`, 좌표 4~16 m에서 해상도 4.8e-7~9.5e-7 m)의 잡음이고, 다 타는 탱크에 남는 연료는 7e-9~1.3e-7 t로 계산기 질량(float32 `stageFuelMass`)의 약 1 ULP다(시험 2단 로켓·ComSat Lx 진단). 계산기도 남은 양과 요구량 차이가 1e-5 단위 아래면 바닥으로 본다(`DeltaVEngineInfo.cs` 54·560행). 잡음은 응답 반올림 경계(0.0005)보다 천 배 이상 작아(어긋남 약 1,400배, 연료 약 3,800배), 판정과 Planner가 보는 값이 어긋나지 않게 응답 정밀도로 맞춘다.
- **결과 응답 형식(C2·C4)**: `{body, facility, stages:[{stage, start_mass_t, end_mass_t, dry_mass_t, fuel_mass_t, dv_asl_ms, dv_vac_ms, twr_asl, twr_vac, burn_s, com_offset_m?, com_offset_deg?}], total:{dv_asl_ms, dv_vac_ms, burn_s}, warnings:[…]}`. `stages`는 스테이지 번호 내림차순(작동 순서, C2)이다(D041). `com_offset_*`는 엔진 추력 합이 0보다 큰 엔진 스테이지에만 있다(C6·D029). `dry_mass_t`는 `start_mass_t − fuel_mass_t`(비연료 자원 포함)다(D031). `facility`는 점검한 기체의 시설(`ship.shipFacility`)이다(D040). 시설 전환 중 이전 기체를 읽으면 C10 검증이 이 값으로 가려낸다.

- **서버 MCP 전송(C12, D049·D050·D061)**: 최신 프로토콜(`2026-07-28`)만 대상으로 한다. 근거는 MCP SDK 2.4.0(`ModelContextProtocol.AspNetCore`·`Core`) 디컴파일과 Claude Code 2.1.328 실행 파일의 MCP 클라이언트 코드다.
  - 전송: `SessionMode = HttpServerSessionMode.Stateless`(SDK 기본값). `ConfigureSessionOptions`는 요청마다 불리므로 공유 도구 컬렉션은 그대로 쓴다. Stateless의 기본 `subscriptions/listen` 처리기는 알림을 허가하지 않으므로(`McpServerImpl.GetAdvertisedCapabilities` 주석) `Handlers.SubscriptionsListenHandler`를 직접 둔다. 그러면 SDK가 `server/discover` 응답에 `tools.listChanged`를 광고한다(`McpServerImpl.cs` 764행).
  - 처리기(`ToolListSubscriptions`): 요청의 `notifications.toolsListChanged`가 참이면 허가 목록을 `{toolsListChanged: true}`로, 아니면 빈 목록으로 한다. 기본 처리기와 같은 형식으로 `notifications/subscriptions/acknowledged`(`params.notifications` = 허가 목록, `_meta["io.modelcontextprotocol/subscriptionId"]` = listen 요청 id)를 listen 요청의 `RelatedTransport`로 보낸다(`McpServerImpl.SendSubscriptionAckAsync`·`SendSubscriptionNotificationAsync`). 도구를 허가했으면 확인 응답을 보내기 전에 스트림을 목록에 넣어, 확인 응답을 받은 클라이언트가 그 뒤의 `list_changed`를 놓치지 않게 한다(D086). 확인 응답 전송이 실패하거나 기다림이 끝나면 목록에서 뺀다. 그 사이 바뀐 도구 목록의 `list_changed`가 확인 응답보다 먼저 갈 수 있는데, SDK 2.4.0 기본 처리기도 같은 순서(구독 등록 뒤 확인 응답, `McpServerImpl.cs` 853~856행)이고 Claude Code 클라이언트(`_onnotification`)는 확인 응답 여부와 상관없이 `tools/list` 캐시를 비우고 처리기에 넘기므로 버려지지 않는다(D090). 공유 도구 컬렉션의 `Changed`(모듈 세대 교체는 `DeferChangedEvents`로 한 번)마다 목록의 스트림 모두에 같은 `_meta`를 붙인 `notifications/tools/list_changed`를 보낸다. 보내기(`list_changed`·유지 신호)는 SDK 기본 구현(`McpServerImpl.SendListChangedNotificationAsync`, `McpServerImpl.cs` 932~944행)처럼 스트림마다 `CancellationToken.None`으로 보내고, 스트림마다 예외를 잡아 경고 로그만 남긴다(D063). 그래서 한 스트림의 실패가 다른 스트림의 알림이나 서버 프로세스에 번지지 않는다. 스트림은 기다림이 끝날 때 목록에서 빠진다.
  - 유지 신호(D051·D058): 허가한 스트림마다 30초 간격으로 같은 `_meta`를 붙인 `notifications/kspbridge/keepalive`(params는 `_meta`뿐)를 보낸다. 클라이언트의 알림 분기(`_onnotification`)는 처리기가 없는 알림을 오류 없이 버린다(Claude Code 2.1.328 코드 대조).
  - 로그(D058): 스트림마다 열림(listen 요청 id, 허가 목록)과 닫힘(같은 요청 id, 닫힌 이유: 요청 취소(연결 끊김 포함)·서버 종료)을 정보 로그로 남긴다.
  - 서버 종료(D059): 기다림은 요청 취소 토큰과 `IHostApplicationLifetime.ApplicationStopping`을 묶은 토큰으로 한다. SDK의 POST 경로(listen도 POST)는 처리기 토큰에 요청 끊김만 묶으므로(`StreamableHttpHandler.cs` 220행), 묶지 않으면 서비스를 멈춰도 listen이 종료 제한 시간까지 남는다.
  - 클라이언트(Claude Code 2.1.328): 서버가 `tools.listChanged`를 광고하면 연결하자마자 `subscriptions/listen`을 연다. 스트림이 끊기면 다시 구독하고 도구 목록을 새로 받으며, `notifications/tools/list_changed`를 받으면 도구 목록을 다시 받는다.
  - 배포: 서버 호스트 변경은 서비스를 멈추고 게시한 뒤 다시 시작한다(ReadMe 서버 실행). 이번 배포 뒤에는 Claude Code에서 MCP를 한 번 다시 연결한다(D061).

## 인터페이스·데이터

| MCP 도구 | 인자 | Mod 연산 | 결과 |
|---|---|---|---|
| `check_ship` | `body`(선택, 천체 내부 이름) | `check/start` → `check/result` 반복 | 위 결과 형식 |

| Mod 연산 | 인자 | 결과 |
|---|---|---|
| `check/start` | `body`(선택) | 부품 없음: 바로 결과. 그 밖: `{check: <번호>, pending: true}` |
| `check/result` | `check` | `{pending: true}` 또는 결과, 또는 `not_ready`(시간 초과·취소) |

## 오류·위험

- **게임 내부 의미(T006 진단으로 확인)**: tasks 단계 1의 진단 덤프로 다음을 확인했다. `stageFuelMass` 키는 스테이지 번호이고 값은 그 스테이지 연소 전후의 부품 자원 질량이다. 비연료 자원 질량은 스테이지 사이에 그대로다. `OnCenterOfThrustQuery`의 `pos`는 추력 변환의 월드 위치, `dir`는 배기 방향이다. `ModuleDecouple`(분리 노드 `top`)은 부모 쪽을 떼고 분리기는 자식 쪽과 함께 떨어지며, `ModuleAnchoredDecoupler`는 부모를 떼고 부착된 쪽이 떨어진다. 2단 로켓·방사형 부스터·발사 클램프 기체에서 계산기 `decoupleStage < s` 집합과 트리 `d < s` 집합이 같다(클램프는 양쪽에서 빠짐). 페어링 기체에서 무게중심 질량 합 = 계산기 `startMass`다(D033·D036). 다른 점(연소 신호 D065, 엔진 스테이지가 아닌 스테이지의 값 D045, 스테이징이 꺼진 도킹 포트 D046)은 이 plan에 반영했다. 구현 뒤에도 다른 분리기 배치에서 어긋나면 이 plan의 해당 결정을 고친다(3절 3번 의도 변경).
- **천체를 되돌리지 못할 위험**: 정상 완료, 시간 초과, 장면 전환(`onGameSceneLoadRequested`), 모듈 해제(Subscriptions 해제 시점)의 모든 경로에서 되돌린다. 되돌리기는 원래 천체를 진행 중 점검에 저장해 두고 한 함수에서만 한다.
- **계산이 끝나지 않을 위험**: Mod 10초 시간 초과(C10)와 서버 15초 상한으로 도구가 멈추지 않게 한다.
- **게임 예외**: 처리기와 이벤트 처리기를 try/catch로 감싼다. 예외가 나면 천체를 되돌리고 오류로 답하며, 게임에 예외를 남기지 않는다(C10).
- **서버 전송 바꿈의 위험(C12)**: 이번 배포 뒤 Claude Code에서 MCP를 다시 연결하기 전까지 이 세션의 `kspbridge` 도구는 쓸 수 없다. 배포 전에 사용자에게 알리고, 다시 연결한 다음 검증을 이어 간다. listen 구독이 열리지 않거나 게시한 도구가 나타나지 않으면 멈춰 묻는다.
- **구간 기록이 없는 아주 긴 연소(한계)**: 계산기는 시뮬레이션 시간이 100000 s를 넘으면 마지막 구간을 기록하지 않고 끝낸다(`DeltaVStageInfo.cs` 2167~2203행). 사건 없이 그보다 오래 타는 스테이지(이온 엔진과 큰 제논 탱크 등)는 연소 안 함으로 보여 `fuel_blocked`가 날 수 있다. 이때 계산기 Δv도 0이라 스테이지 표와는 어긋나지 않는다. 드문 구성이라 고치지 않는다(D069).
- **연료를 여러 가지 실은 부품의 `fuel_blocked`(한계)**: 계산기의 스테이지별 연료 질량은 부품 단위 합계라 자원별 값이 없다(결정 "부품의 연료 질량"). 그래서 그 엔진의 추진제는 앞 스테이지가 다 쓰고 다른 엔진의 연료만 남은 부품도 "실려 있음"으로 세어 `fuel_blocked`가 날 수 있다(예: LiquidFuel만 쓰는 엔진과 LF/Ox 탱크에서 LF만 바닥남). 드문 구성이라 고치지 않는다(D081).
- **점검 중 사용자가 편집**: 계산 도중 기체가 바뀌면 계산기가 스스로 다시 계산 표시를 하므로(`onEditorShipModified` → `SetCalcsDirty`), 완료 판정의 `calcsDirty` 조건이 다시 계산된 값만 받게 한다. 한계: 사용자나 다른 도구의 편집으로 작동 스테이지 구성이 바뀌면, 점검이 구간 기록을 비운 뒤에 돈 계산의 구간이 그 뒤 계산에서 건너뛰는 스테이지에 남을 수 있다(D074).

## 검증 전략

| 완료 조건 | 방법 |
|---|---|
| C1~C10 공통 | 모든 MCP 확인은 이 세션의 `kspbridge` 도구로 한다. 서버 MCP 엔드포인트를 직접 부르지 않는다. 도구가 없거나 이상하면 그 자체를 결함으로 분석한다(원칙 점검 "실제 클라이언트로 검증"). C12를 고친 뒤 이 세션에 `check_ship`이 나타난 다음 C1~C10을 다시 확인한다. |
| C1 | MCP: `check_ship`을 기본값으로 부르면 `body=Kerbin`이고, `body=mun`이면 `Mun`, `body=없는이름`이면 `bad_request`. 점검 뒤 다시 기본값으로 불렀을 때 Kerbin 해면 값이 처음과 같아 천체가 되돌려졌는지 확인한다. |
| C2 | MCP: 2단 로켓에서 스테이지 번호 집합이 `editor_get_stages`와 같은지, `stages` 번호가 내림차순인지, 분리기만 있는 스테이지의 Δv가 0인지, Mun 지정 때 해면 값이 진공 값과 같은지 확인한다. 연료를 다 태운 엔진만 남은 스테이지(시험 2단 로켓 스테이지 0, ComSat Lx 스테이지 2)의 Δv·TWR·연소 시간이 0이고 어긋남 값이 없는지, 한 스테이지에 연소 시간이 다른 엔진 둘을 둔 기체에서 먼저 바닥나는 엔진도 그 스테이지의 추력 엔진인지 확인한다(D068). `editor_set_stage`로 엔진을 다른 스테이지로 옮긴 뒤 점검해, 엔진이 없어진 스테이지가 엔진 스테이지로 잡히지 않는지 확인한다(D074). 단위 테스트: 스테이지 표 만들기와 순서(내림차순). 연소 신호(`BurnSignal`): 실제 연소 구간, 찌꺼기 구간(시작 질량 = 끝 질량), 먼저 바닥나는 엔진, 구간이 없는 스테이지(건너뛴 스테이지), 계산기 스테이지 정보가 없는 스테이지(D067). 스테이지 대상 부품이 없는 기체(포드 하나): MCP로 스테이지 없음, 단위 테스트(부품은 있고 `StagedNumbers`가 빈 자료)로 스테이지 표가 빈 것. 합계 = 반올림한 스테이지 값의 합(D078) 단위 테스트. |
| C3 | MCP: 액체 연료 엔진 2단 로켓에서 `editor_list_parts`·`editor_get_part_settings`로 손 계산한 진공 Δv·TWR과 1% 안에서 같은지, 첫 스테이지 시작 질량이 `mass_t`와 0.001 t 안인지 확인한다(계산 과정을 보고에 적음). 시험 기체에는 발사 클램프를 달지 않는다. 계산기 시작 질량에는 클램프가 없지만(D037) `mass_t`(`ShipConstruct.GetShipMass`)에는 들어가기 때문이다(D038). |
| C4 | 단위 테스트: 경고 응답 필드. MCP: 결함 기체에서 응답 형식 확인. |
| C5~C7 | 단위 테스트: 경고 8종 각각에 대해 그 결함만 가진 `ShipData`에서 그 경고만 나고, 고친 자료에서는 안 나는지. 배타 규칙(`fuel_blocked`·`no_engine_fuel`). 분리기가 떼어 내는 쪽의 순수 규칙(D085): 분리 노드가 빔 → 없음, 노드에 부모 → 자기 자신, 노드에 자식 → 그 자식, omni → 자식들과 부모가 있으면 자기 자신(D077). 떼어 낸 루트로 분리 단위: 빈 노드 분리기는 아무것도 떼지 않음, 자기 자신이 들어 있으면 함께 떨어짐. `low_launch_twr`: 해면 TWR 0.995와 0은 경고(값은 셋째 자리), 1.0은 경고 없음(D089·D096). `no_engine_fuel`: 줄지 않는 탱크의 시작 연료 0.0004 t는 경고 없음·0.001 t는 경고(D087). `fuel_blocked`는 그 엔진 스테이지 전에 떨어지는 부품의 연료를 세지 않음, 남아 있지만 스테이지 s 시작 연료 질량이 0(앞 스테이지가 비움)이면 경고 없음, 0.0004 t면 없음·0.001 t면 있음, 그 엔진의 추진제가 없는 연료 부품만 남으면 경고 없음, 무시 목록 추진제(이온 엔진의 ElectricCharge)와 그 엔진이 쓰지 않는 다른 연료(MonoPropellant를 쓰는 다른 엔진이 있어 연료가 됨)를 함께 가진 부품만 남으면 경고 없음(D075·D076·D080·D082). 판정 정밀도: 0.001 m·0.001 t 아래 잡음은 경고하지 않고 그 이상은 경고(D076). |
| C8 | MCP: 경고 8종마다 그 결함만 가진 기체를 `editor_*` 도구로 만들어 그 경고만 나는지, 고친 뒤 안 나는지 확인한다. 결함 없는 2단 로켓은 경고 없음. `fuel_blocked`는 연료가 실린 탱크가 그 엔진 스테이지에도 남아 있지만 흐름 규칙상 받지 못하는 구성으로 시험한다(D075). |
| C9 | MCP: 점검 전후 `editor_get_ship`·`editor_get_stages` 결과가 같은지, 두 번 점검 결과가 같은지, 빈 기체 결과가 스테이지 없음·경고 없음인지 확인한다. |
| C10 | MCP: 우주센터에서 부르면 `not_ready`, `editor_load` 직후 바로 부르면 `not_ready` 또는 정상 결과(준비된 뒤), `editor_enter`로 시설을 바꾼 직후 바로 부르면 `not_ready` 또는 정상 결과(준비된 뒤)이고 결과의 `facility`가 바뀐 시설인지(D039), 잘못된 인자면 `bad_request`. 단위 테스트: `PendingCheck` 시간 초과(10초) 전이, `Readiness` 조건별 준비 안 됨(게임 안 시설 전환의 `restartingEditor`는 MCP로 일으킬 수 없어 이 단위 테스트로 확인; D042). Player.log에 예외가 없는지 확인한다. `editor_remove`로 엔진을 지운 바로 뒤 `check_ship`이 오류 없이 끝나는지 확인한다(D074). |
| C11 | `git status --porcelain`(미추적 새 파일 포함)과 `git diff --stat <기준점>`을 함께 보고, 나온 경로가 모두 새 모듈 폴더, 새 테스트 폴더, `KspBridge.slnx`, `ReadMe.md`, `docs/`, 서버 호스트의 `src/KspBridge.Server/Program.cs`·`src/KspBridge.Server/ToolListSubscriptions.cs`·`src/KspBridge.Server/Modules/ModuleHost.cs`, `tests/KspBridge.Server.Tests/McpEndpointTests.cs` 안인지 확인한다(D039·D049·D079). 핫리로드는 두 모듈을 따로 본다. 서버 모듈은 C12의 실제 세션 확인으로, Mod 모듈은 이 세션의 `get_status`로 Mod 세대 번호(`mod.data.generation.n`)를 적은 뒤 KSP를 끄지 않고 Mod 모듈을 게시하고, `get_status`를 세대 번호가 오를 때까지 몇 초 동안 되물어 `mod.data.generation.n`이 1 오르고 `mod.data.versions.modules`에 `KspBridge.Mod.Modules.Check.g…`가 있으며 `check_ship`이 동작하는지로 본다. |
| C12 | 서버 호스트를 게시하고 서비스를 다시 시작한 뒤, 사용자가 Claude Code에서 MCP를 한 번 다시 연결하고(D061) 이 세션의 `kspbridge` 도구가 동작하는지 본다. check 서버 모듈에 임시 도구 `check_c12_probe`(고정 문자열을 돌려줌)를 더해 서버 모듈을 게시하고, 이 세션의 도구 목록에 그 도구가 새로 나타나고(`ToolSearch`) 불리는지 본다(D064). 확인 뒤 임시 도구를 지워 다시 게시한다. 통과 기준: 임시 도구가 나타나고 불리며, 그동안 기존 도구(`get_status`)가 계속 동작한다. 처리기 로그는 함께 남겨 둔다. 단위 테스트: `McpEndpointTests`(최신 프로토콜 클라이언트가 listen으로 구독 → 게시 → `list_changed` 수신 → 새 도구 호출), 기존 서버 테스트 통과. 허가하지 않은 listen이 확인 응답 뒤 바로 끝나는지, 짧은 간격으로 유지 신호가 오는지, 서버 종료(`ApplicationStopping`) 때 스트림이 닫히는지도 단위 테스트한다. 확인 응답 전송 직후 불리는 테스트용 이음매 안에서 그 스트림이 이미 목록에 있는지(D086·D091)도 본다. 등록을 확인 응답 바로 뒤로 옮기는 되돌림은 이 테스트로 잡지 못하며 보류한다(D097). D086은 서버 호스트 변경이라, 서비스를 멈추고 게시해 다시 시작한 뒤 MCP를 다시 연결하고 이 세션의 `kspbridge` 도구 동작과 임시 도구 `check_c12_probe`의 나타남·호출로 다시 확인한다(D061·D064). |

## 원칙 점검

- 적용 규칙:
  - 전역: 언어(한국어), 포터블 Python(쓰지 않음), GitHub 접근(Bizadm MCP), 스펙 주도
  - Bizadm MCP rule: `workflow/spec-driven.md`, `git/github-issue.md`, `workflow/repository-content.md`(저장소 문서에 기계 경로 없음)
  - 프로젝트 메모
    - P-C-C 구조: Planner에게 구분된 도구를 주고, 보정은 별도 도구로 둔다.
    - 게임 내부는 디컴파일·실행으로 확인한 뒤 설계한다.
    - 검증은 게임 화면 없이 MCP 응답으로만 한다.
    - 앱 포커스 입력 잠금으로 준비 여부를 판정하지 않는다.
    - 장면을 바꾸기 전에 `get_status`를 확인한다.
    - 불러오기 직후 실패는 Mod의 준비 판정 결함으로 다룬다.
    - 실제 클라이언트로 검증: 새 도구·핫리로드는 이 세션의 Claude Code 도구 목록에 나타나야 통과다. 서버 MCP 엔드포인트를 직접 불러 우회하지 않고, 검증이 실패하면 멈추고 묻는다.
- 충돌:
  - 없음.
  - 점검은 Planner가 쓰는 조회 도구다. Coordinator·Controller 층을 건드리지 않고, 새 모듈 쌍으로 분리한다(D001).
  - 게임 내부 가정은 디컴파일 줄 번호로 근거를 댔다. 실행해야만 알 수 있는 의미(분리 스테이지 번호 규칙 등)는 tasks 단계 1(T006)의 진단으로 확인했고, 어긋난 점은 D045·D046으로 반영했고, 연소 신호는 구현 검증에서 드러난 오판에 따라 D065·D067·D074로 정했다.
  - 준비 판정은 앱 포커스 입력 잠금을 보지 않는다.
  - 저장소 문서에는 기계별 경로를 쓰지 않는다(디컴파일 근거는 형식 이름과 줄 번호로만 적음).
