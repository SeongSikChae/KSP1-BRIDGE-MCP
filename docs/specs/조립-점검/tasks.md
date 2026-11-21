# 조립 점검: 스테이지별 Δv·TWR과 구조 점검 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 모듈 뼈대와 게임 내부 의미 확인

- [x] T001 Mod 모듈 프로젝트(Wait 모듈 csproj와 같은 구성: net472, `Assembly-CSharp`·`UnityEngine.CoreModule` 참조, Core·Loader 프로젝트 참조)와 솔루션 등록 (src/KspBridge.Mod.Modules.Check/KspBridge.Mod.Modules.Check.csproj, KspBridge.slnx) — C11
- [x] T002 서버 모듈 프로젝트(Wait 서버 모듈 csproj와 같은 구성)와 솔루션 등록 (src/KspBridge.Server.Modules.Check/KspBridge.Server.Modules.Check.csproj, KspBridge.slnx) — C11
- [x] T003 테스트 프로젝트(editor 테스트 프로젝트와 같은 구성: MSTest, net472, KSP 어셈블리 복사 참조)와 솔루션 등록 (tests/KspBridge.Mod.Modules.Check.Tests/KspBridge.Mod.Modules.Check.Tests.csproj, KspBridge.slnx) — C10, C11
- [x] T004 `CheckRunner` 계산 제어 부분: 지정 천체 찾기(`FlightGlobals.Bodies`의 `bodyName` 대소문자 무시, 기본 `GetHomeBody()`), `DeltaVAppValues.body` 바꾸기와 한 함수에서만 되돌리기, `SetCalcsDirty(false)`, `calcsDirty` 리플렉션 읽기(필드 없으면 거부), 계산기 없음(`vesselDeltaV` null)·꺼짐(`DoStockSimulations` false) 거부, 완료 이벤트에서 `calcsDirty == false`일 때만 콜백 호출 (src/KspBridge.Mod.Modules.Check/CheckRunner.cs) — C1, C10
- [x] T005 `CheckModule` 뼈대: `IBridgeModule`(`Name = "check"`), 이벤트 구독(`onDeltaVCalcsCompleted`, `onEditorRestart`, `onEditorLoad`, `onGameSceneLoadRequested`)과 장면 전환·모듈 해제 때 천체 되돌리기, 임시 진단 처리기 둘(T019에서 지움). 처리기는 동기라 계산 완료를 그 호출 안에서 기다릴 수 없으므로 시작과 가져오기로 나눈다. `check/diag_start`(`body` 선택)는 T004로 천체를 바꾸고 다시 계산시킨다. 완료 이벤트에서 `calcsDirty == false`일 때 그 자리에서 다음 원자료를 떠 두고 천체를 되돌린다. `check/diag_result`는 떠 둔 덤프를 JSON으로 돌려주거나, 아직이면 `{pending: true}`를 돌려준다 (src/KspBridge.Mod.Modules.Check/CheckModule.cs) — C1, C2, C5, C6, C7
  - 부품마다: `craftID`, 이름, 부모 `craftID`, `inverseStage`, `isLaunchClamp()`, 분리기 모듈 종류(`ModuleDecouple`의 분리 노드·`isOmniDecoupler`, `ModuleAnchoredDecoupler`, `ModuleDockingNode`)와 떼어 내는 쪽 부품, 계산기 `DeltaVPartInfo` 유무와 `decoupleStage`·`activationStage`·`isJettison`·`jettisonMass`, `stageFuelMass`의 키와 키마다 `startMass`·`endMass`, 자원 이름·현재량·밀도, `part.mass`, `physicalSignificance`, 무게중심 위치
  - 엔진마다: `craftID`, `stageBurnTotals`의 키와 추진제별 `amountBurnt`, `thrustVac`·`thrustASL`, `OnCenterOfThrustQuery`의 `pos`·`dir`와 추력 변환의 위치·`forward`
  - 스테이지마다: `GetStage(n)`의 `startMass`·`endMass`·`deltaVatASL`·`deltaVinVac`·`TWRASL`·`TWRVac`·`stageBurnTime`, 스테이지 대상 부품의 `inverseStage` 집합
- [x] T006 KSP 진단(KSP와 서버를 끄지 않고 `dotnet publish src/KspBridge.Mod.Modules.Check`로 Mod 모듈을 게시(`ReadMe.md` 빌드와 게시)한 뒤, Mod HTTP로 `check/diag_start`를 부르고 `check/diag_result`를 `pending`이 아닐 때까지 되물음). 결과를 작업 문서에 적고 아래 줄에 요약한다. 어긋나면 spec-driven 3절 3번대로 plan.md부터 고친다 (작업 대상 파일 없음) — C1, C2, C3, C5, C6, C7
  - 단일 엔진 2단 로켓: `stageFuelMass` 키가 스테이지 번호이고 시작·끝이 그 스테이지 연소 전후인지, `stageBurnTotals` 키, `OnCenterOfThrustQuery` 좌표계(월드 좌표, `dir`가 배기 방향), 비연료 자원(MonoPropellant·ElectricCharge)의 질량이 스테이지 사이에 그대로인지, 지정 천체를 Mun으로 바꿨다 되돌린 뒤 Kerbin 해면 값이 처음과 같은지
  - 같은 로켓의 부품 집합 비교(D032): 스테이지마다 계산기 `decoupleStage < s` 부품 집합과, 덤프한 부모 관계와 분리기 `inverseStage`로 손으로 구한 "떨어지는 스테이지 d < s" 집합이 같은지
  - 발사 클램프를 단 같은 로켓(D037): 클램프에 계산기 부품 정보가 없고, 클램프를 뺀 두 집합이 같은지
  - 방사 분리기(`ModuleAnchoredDecoupler`)로 붙인 부스터와 도킹 포트가 있는 기체: 분리기 종류별로 떼어 내는 쪽이 plan 결정 "분리 단위·남은 부품"과 같은지
  - 페어링을 단 엔진 기체(D033·D036): 스테이지마다 "`decoupleStage < s` 부품의 (`part.mass` − `JettisonInStage(s)`이면 `jettisonMass`) + `stageFuelMass[s].startMass`" 합이 계산기 `startMass`와 같은지
  - 결과(2026-11-21, 자세한 기록은 작업 문서): `stageFuelMass` 키·의미, 추력 좌표계, 비연료 자원 불변, 천체 바꾸기·되돌리기, `ModuleDecouple`·`ModuleAnchoredDecoupler`의 떼어 내는 쪽, 발사 클램프 제외, 페어링 투하 질량 합은 plan과 맞음. 다른 점 셋: `stageBurnTotals`의 `amountBurnt`는 늘 0이라 연료 소모 신호는 `maxTotalBurnTime > 0`(연소 신호는 D065로 대체), 엔진 스테이지가 아닌 스테이지에도 계산기가 연소 시간·TWR을 냄, 스테이지 대상이 아닌 도킹 포트를 계산기는 분리기로 봄 → plan 의도 변경(D044~D046, plan 다시 검토에서 D047·D048), plan·tasks 다시 확인

체크포인트: `dotnet build KspBridge.slnx` 경고 0. T006의 확인이 모두 plan과 맞는다. 맞지 않으면 plan.md부터 고치고 그 검토·확인을 다시 한 뒤 단계 2로 간다.

## 단계 2: 순수 자료와 판정

- [x] T007 [P] 순수 3차원 벡터(더하기, 빼기, 스칼라 곱, 내적, 외적, 길이) (src/KspBridge.Mod.Modules.Check/Vec.cs) — C6
- [x] T008 [P] 순수 준비 판정: 결정 "준비 판정"의 조건 값(게임 로드, 코어 로드 표시 사유, 에디터 장면, `fetch.ship` 있음, `EditorDriver.fetch` 있음·`restartingEditor`, 마지막 `onEditorRestart`·`onEditorLoad` 뒤 프레임 수, 모든 부품 `editorStarted`, `sortRoutine` null)을 입력으로 받아 준비 여부와 `not_ready` 사유를 돌려줌 (src/KspBridge.Mod.Modules.Check/Readiness.cs) — C10
- [x] T009 [P] 순수 진행 중 점검 상태 기계: 시계 주입, 대기 → 완료/시간 초과(10초)/취소, 남은 점검 정리 규칙(완료했지만 안 받아 간 점검·10초 지난 대기 점검은 정리, 10초 안의 대기 점검은 거절) (src/KspBridge.Mod.Modules.Check/PendingCheck.cs) — C10
- [x] T010 순수 자료 모델: 부품(식별자, 부모, 발사 클램프 여부, 스테이지 s의 부품 건조 질량 계산에 쓰는 `part.mass`·투하 판정 값(`isJettison`·`jettisonMass`·`decoupleStage`·`activationStage`, 계산기 정보 없음 표시), 무게중심 위치, 분리기 여부(부품 스테이지 대상 판정식 값, D047)와 작동 스테이지·떼어 내는 쪽, 스테이지별 자원 질량 시작·끝(계산기 키가 있는 스테이지만), 실린 자원 이름·현재량·밀도, 조종·승무원·낙하산 여부), 엔진(작동 스테이지, 스테이지별 연소 여부(결정 "엔진·추력 정보"의 연소 여부, D065·D067·D074), 추진제, 추력 작용점·방향, 진공·해면 추력(추력 제한 반영), 유효 짐벌 각), 계산기 스테이지 값, 스테이지 대상 부품 번호 집합, 연료 무시 목록, 시설, 천체 (src/KspBridge.Mod.Modules.Check/ShipData.cs) — C2, C5, C6, C7
- [x] T011 순수 판정 `Checks`. T007·T010을 쓴다 (src/KspBridge.Mod.Modules.Check/Checks.cs) — C2, C4, C5, C6, C7, C9
  - 분리 단위와 부품마다 떨어지는 스테이지(트리 위 스테이지에 놓인 분리기(부품 스테이지 대상 판정식, 스테이징이 꺼진 도킹 포트 제외, D046·D047) 가운데 `inverseStage` 최대, 발사 클램프 제외, D026·D037)
  - 추력 엔진·엔진 스테이지(그 스테이지 연소 여부가 참인 엔진이 있음, D065)
  - 연료 자원(엔진 추진제 − 무시 목록, D028)과 부품의 연료 질량(자원 질량 − 비연료 자원 질량, D027). 계산기 키가 없는 스테이지는 가장 가까운 앞 키의 `endMass`를 이어 보고, 키가 없으면 현재 자원 질량(D048)
  - 스테이지 표: 번호 집합은 스테이지 대상 부품의 `inverseStage`, 번호 내림차순(D041). 엔진 있는 기체는 계산기 값(`startMass`·`endMass`·Δv·TWR·연소 시간), 정보 없는 번호는 이전 끝 질량과 0. 엔진 스테이지가 아닌 스테이지는 Δv·TWR·연소 시간 0(D045). `fuel_mass_t`는 `decoupleStage < s` 부품의 연료 질량 합(D031·D032), `dry_mass_t = start − fuel`. 엔진 없는 기체는 `d < s` 부품의 스테이지 s의 부품 건조 질량(`isJettison && activationStage >= s`이면 `jettisonMass`를 뺌, D033·D036)과 자원 질량으로 시작 = 끝, Δv·TWR·연소 시간 0. 합계, 반올림(Δv·연소 시간 소수 첫째, 질량 셋째, TWR 둘째)
  - 스테이지별 무게중심(`decoupleStage < s` 부품, 스테이지 s의 부품 건조 질량(`JettisonInStage(s)` 판정)과 시작 자원 질량, D033·D034), 추력선(엔진 추력은 `thrustVac`, 0이면 `thrustASL`, 힘 방향 `-dir`, D029), 어긋남 거리·각도, 짐벌 최대 회전력(유효 짐벌 각). 엔진 추력 합 0인 스테이지는 값을 내지 않음
  - 경고 8종(`no_separation`, `fuel_blocked`, `no_engine_fuel`(같은 분리 단위의 `fuel_blocked` 엔진과 배타, D018·D021), `com_offset`, `no_control`, `low_launch_twr`(시설 VAB만, D040), `leftover_fuel`(탱크별 질량·자원 이름, 이미 알린 탱크 제외, D025), `no_parachute`)과 경고 응답 `{code, stage?, parts[], message, values{}}`
  - 결과 응답 사전 `{body, facility, stages, total, warnings}`(`com_offset_*`는 엔진 추력 합 > 0인 엔진 스테이지에만), 부품 없는 기체 결과(스테이지 없음·경고 없음)
- [x] T012 준비 판정 단위 테스트: 모든 조건이 맞으면 준비됨, 조건마다 하나만 어긋나면 준비 안 됨과 사유(`restartingEditor` true, `EditorDriver.fetch` 없음 포함) (tests/KspBridge.Mod.Modules.Check.Tests/ReadinessTests.cs) — C10
- [x] T013 진행 중 점검 단위 테스트: 완료·시간 초과(10초)·취소 전이, 남은 점검 정리와 거절 (tests/KspBridge.Mod.Modules.Check.Tests/PendingCheckTests.cs) — C10
- [x] T014 판정 단위 테스트: `ShipData`를 직접 만들어 스테이지 표(번호 집합, 내림차순, 정보 없는 번호 채움, 엔진 없는 기체, 엔진 스테이지가 아닌 스테이지에 계산기 연소 시간·TWR이 있어도 0, 반올림, `start = dry + fuel`), 연소 여부(엔진의 연소 스테이지 집합에 없으면 연소 안 함), 떨어지는 스테이지(겹친 분리기, 발사 클램프 제외, 스테이징이 꺼진 도킹 포트는 분리기 아님), 계산기 연료 키가 빠진 스테이지의 이어 보기, 투하 질량 차감, 무게중심·추력선·어긋남·짐벌 계산(추력 합 0 스테이지 값 없음), 경고 8종마다 그 결함만 가진 자료에서 그 경고만 나고 고친 자료에서 안 나는지, 배타 규칙(`fuel_blocked`·`no_engine_fuel`), 경고 응답 필드, SPH 시설에서 `low_launch_twr` 없음 (tests/KspBridge.Mod.Modules.Check.Tests/ChecksTests.cs) — C2, C4, C5, C6, C7, C9

체크포인트: `dotnet build KspBridge.slnx` 경고 0, `dotnet test` 통과.

## 단계 3: KSP 쪽 연결과 도구

- [x] T015 스냅숏: 완료 이벤트 시점에 `ShipConstruct`·`VesselDeltaV`(`GetStage`, `OperatingEngineInfo`, `DeltaVPartInfo`)·스테이지별 구간 `deltaVCalcs`(그 엔진이 든 `endMass < startMass` 구간, D065)·부품 스테이지 대상 판정식(D047)·부품 모듈(`ModuleCommand`, `ModuleParachute`, `CrewCapacity`, `ModuleGimbal`, 분리기 모듈, `OnCenterOfThrustQuery`)·`PropellantsToIgnore`·`ship.shipFacility`에서 `ShipData`를 만듦. 무게중심 위치는 `EditorMarker_CoM.findCenterOfMass` 규칙(물리 비중요 부품은 부모 위치), 유효 짐벌 각은 켜진 축 범위 최소 × `gimbalLimiter`/100(축 하나라도 꺼지면 0, 잠김이면 0) (src/KspBridge.Mod.Modules.Check/ShipSnapshot.cs) — C2, C5, C6, C7
- [x] T016 `CheckRunner` 마무리: T009 상태 기계로 진행 중 점검 관리, 완료 콜백에서 T015 스냅숏과 T011 판정 뒤 천체 되돌리기·다시 계산, 시간 초과·장면 전환·모듈 해제 때 되돌리기, 예외를 잡아 되돌린 뒤 오류로 답함 (src/KspBridge.Mod.Modules.Check/CheckRunner.cs) — C1, C9, C10
- [x] T017 처리기 `check/start`(인자 검사 `bad_request`, T008 준비 판정에 게임 값을 읽어 넘김, 부품 없음은 바로 결과, 그 밖은 `{check, pending: true}`)와 `check/result`(`pending`, 결과, `not_ready`) 등록, 마지막 `onEditorRestart`·`onEditorLoad` 프레임 기록 (src/KspBridge.Mod.Modules.Check/CheckModule.cs) — C1, C9, C10
- [x] T018 [P] MCP 도구 `check_ship`(`body` 선택 인자): `[assembly: KspBridgeModule("check")]`, `check/start` 뒤 `check/result`를 50 ms 간격으로 되묻고 15초 상한을 넘기면 `not_ready` (src/KspBridge.Server.Modules.Check/CheckTools.cs) — C1, C10, C11
- [x] T019 임시 진단 처리기 `check/diag_start`·`check/diag_result`와 덤프 코드 삭제 (src/KspBridge.Mod.Modules.Check/CheckModule.cs) — C11
- [x] T020 [P] 도구 목록에 `check_ship` 추가, 점검 동안 게임 Δv 창의 천체가 잠깐 바뀐다는 설명 (ReadMe.md) — C1
- [x] T021 [P] 기준 spec assembly-check 새로 만들기(assembly-check의 마지막 구현 작업): 이번 spec C1~C11을 R1~R11로 쓰고, 이력에 조립-점검 한 줄, `파일:` 줄에 check 모듈 쌍과 테스트 파일 (docs/features/assembly-check.md) — C1, C2, C3, C4, C5, C6, C7, C8, C9, C10, C11

체크포인트: `dotnet build KspBridge.slnx` 경고 0, `dotnet test` 통과, KSP와 서버를 끄지 않고 `dotnet publish src/KspBridge.Mod.Modules.Check`와 `dotnet publish src/KspBridge.Server.Modules.Check`로 게시(`ReadMe.md` 빌드와 게시)했다. 실행 중인 Claude Code에 `check_ship`이 나타나는지는 단계 4의 C12에서 본다. 기준 spec이 있다.

## 단계 4: 서버 호스트 MCP 전송(C12, D049·D050·D051·D058·D059·D061·D062·D063)

- [x] T025 listen 처리기 `ToolListSubscriptions`: plan 결정 "서버 MCP 전송"의 처리기 항목대로 허가 목록, `notifications/subscriptions/acknowledged`(처리기 토큰), 스트림 목록 넣기·빼기, 공유 도구 컬렉션 `Changed` 구독·해제와 `notifications/tools/list_changed`, 스트림마다 30초 간격 `notifications/kspbridge/keepalive`. `list_changed`·유지 신호는 `CancellationToken.None`으로 보내고 스트림마다 예외를 잡아 경고 로그. 기다림 토큰은 처리기 토큰과 `IHostApplicationLifetime.ApplicationStopping`을 묶음. 열림(listen 요청 id, 허가 목록)·닫힘(요청 id, 닫힌 이유: 요청 취소(연결 끊김 포함)·서버 종료) 정보 로그 (src/KspBridge.Server/ToolListSubscriptions.cs) — C12
- [x] T026 서버 호스트 전송 설정: `SessionMode = HttpServerSessionMode.Stateless`, `ConfigureSessionOptions`에서 공유 도구 컬렉션·`Tools.ListChanged`는 그대로 두고 `Handlers.SubscriptionsListenHandler`에 T025 처리기 연결 (src/KspBridge.Server/Program.cs) — C11, C12
- [x] T027 서버 테스트: 도구 목록 변경 알림 시험을 최신 프로토콜 클라이언트가 `subscriptions/listen`으로 구독(`SendRequestAsync`, `RegisterNotificationHandler`) → 서버 모듈 게시 → 그 스트림으로 `list_changed` 수신 → 새 도구 호출로 고쳐 씀. 기존 서버 테스트 통과 (tests/KspBridge.Server.Tests/McpEndpointTests.cs) — C12
- [x] T028 [P] 서버 구조와 서버 실행 절의 MCP 문장 세 곳을 최신 프로토콜 기준으로 고쳐 씀(D054·D061): 고정 호스트는 포트를 유지, 모듈 세대 교체 알림은 `subscriptions/listen`으로 구독한 클라이언트에 감, 서버 호스트를 다시 실행한 뒤 모듈 게시 알림이 오지 않으면 Claude Code에서 MCP를 다시 연결 (ReadMe.md) — C12
- [x] T029 기준 spec 갱신(server-hosting의 마지막 구현 작업): R5(서버 호스트가 실행 중인 동안 게시한 서버 모듈이 실행 중인 Claude Code에 닿음, 이번 C12), 이력에 조립-점검 한 줄, `파일:` 줄에 `ToolListSubscriptions.cs` (docs/features/server-hosting.md) — C12
- [x] T030 서버 호스트 배포: 배포 전에 사용자에게 이 세션의 `kspbridge` 도구가 다시 연결 전까지 쓸 수 없다고 알린다. 서비스를 멈추고 서버 호스트를 게시한 뒤 다시 시작(`ReadMe.md` 서버 실행). 사용자가 Claude Code에서 MCP를 한 번 다시 연결한 뒤 이 세션의 `get_status`가 동작하는지, T025의 listen 열림 로그가 남는지 본다. `get_status`가 동작하지 않거나 열림 로그가 없으면 멈춰 묻는다 (작업 대상 파일 없음) — C12
- [x] T031 C12: check 서버 모듈에 임시 도구 `check_c12_probe`를 더해 게시하고, 이 세션의 도구 목록에 새로 나타나고(`ToolSearch`) 불리는지 본다(D064). 확인 뒤 임시 도구를 지워 다시 게시한다. 그동안 `get_status`가 계속 동작하는지 보고, 처리기 로그를 함께 남긴다. 게시한 도구가 나타나지 않으면 멈춰 묻는다 (src/KspBridge.Server.Modules.Check/CheckTools.cs 임시 수정 후 원복) — C11, C12

체크포인트: `dotnet build KspBridge.slnx` 경고 0, `dotnet test` 통과, 이 세션에서 게시한 도구가 다시 연결 없이 나타나 호출된다.

## 단계 5: KSP 검증

- [x] T032 연소 신호 바꿈(D065·D067·D074): `CheckRunner`가 `SetCalcsDirty` 직전에 계산기 스테이지 정보 두 벌(private `DeltaVEngineStageSet.stageInfoEntries`의 `stageInfo_InstanceOne`·`stageInfo_InstanceTwo`, 리플렉션) 모두의 `deltaVCalcs`를 비우고(없는 벌은 건너뜀), 필드를 찾지 못하면 `RequireCalculator`가 `not_ready`로 거절한다. `ShipData`의 계산기 스테이지 값에 구간 기록(`deltaVCalcs` 구간마다 `activeEngines`의 `craftID`·`startMass`·`endMass`)을 더하고, `EngineData.BurnStages` 주석을 D065·D067 판정으로 고친다. 순수 판정 `BurnSignal`이 엔진마다 "그 엔진이 든 `endMass < startMass` 구간이 있는" 스테이지로 `BurnStages`를 채운다. 스냅숏은 계산기 값을 옮겨 담은 뒤 `BurnSignal`을 부르고 `stageBurnTotals`는 쓰지 않는다 (src/KspBridge.Mod.Modules.Check/CheckRunner.cs, src/KspBridge.Mod.Modules.Check/ShipData.cs, src/KspBridge.Mod.Modules.Check/BurnSignal.cs, src/KspBridge.Mod.Modules.Check/ShipSnapshot.cs) — C2, C5, C6, C7, C10
- [x] T033 임시 진단 처리기 `check/diag`와 임시 도구 `check_diag` 삭제 (src/KspBridge.Mod.Modules.Check/CheckModule.cs, src/KspBridge.Server.Modules.Check/CheckTools.cs) — C11
- [x] T035 연소 신호 단위 테스트(D067): 계산기 스테이지 값을 직접 만들어 실제 연소 구간이 있는 스테이지는 연소, 찌꺼기 구간(시작 질량 = 끝 질량)만 있는 스테이지는 연소 안 함, 한 스테이지에서 먼저 바닥나는 엔진도 그 스테이지에서 연소, 구간이 없는 스테이지(건너뛴 스테이지)는 연소 안 함, 계산기 스테이지 정보가 없는 스테이지는 연소 안 함을 확인 (tests/KspBridge.Mod.Modules.Check.Tests/BurnSignalTests.cs) — C2, C5, C6, C7
- [x] T037 기준 spec 반영: `docs/features/assembly-check.md`의 `파일:` 줄에 `src/KspBridge.Mod.Modules.Check/BurnSignal.cs`와 `tests/KspBridge.Mod.Modules.Check.Tests/BurnSignalTests.cs`를 더한다. 이력은 기존 조립-점검 줄을 그대로 둔다 (docs/features/assembly-check.md) — C11
- [x] T036 `dotnet build KspBridge.slnx` 경고 0, `dotnet test` 통과 확인 (작업 대상 파일 없음) — C2, C11
- [x] T034 KSP를 끄지 않고 두 check 모듈을 게시한 뒤, 이 세션의 `check_ship`으로 시험 2단 로켓 스테이지 0과 ComSat Lx 스테이지 2(연료를 다 태운 엔진만 남은 스테이지)의 Δv·TWR·연소 시간이 0이고 어긋남 값이 없는지 본다. 한 스테이지에 연소 시간이 다른 엔진 둘을 둔 시험 기체를 `editor_*` 도구로 만들어 두 엔진이 모두 그 스테이지의 추력 엔진(어긋남 값에 들어감, `fuel_blocked` 없음)인지 보고, 시험 craft는 확인 뒤 지운다(D068). `editor_set_stage`로 엔진을 다른 스테이지로 옮긴 뒤 엔진이 없어진 스테이지가 엔진 스테이지로 잡히지 않는지, `editor_remove`로 엔진을 지운 뒤 `check_ship`이 오류 없이 끝나는지 본다(D074). 이 세션 도구 목록에서 `check_diag`가 빠졌는지 본다 (작업 대상 파일 없음) — C2, C5, C6, C10, C11

- [x] T022 먼저 Mod 모듈 핫리로드: 이 세션의 `get_status`로 Mod 세대 번호(`mod.data.generation.n`)를 적고, KSP를 끄지 않고 `dotnet publish src/KspBridge.Mod.Modules.Check`로 게시한 뒤 `get_status`를 `mod.data.generation.n`이 오를 때까지 몇 초 동안 되묻고(Loader는 게시 뒤 약 2초 안에 세대를 바꿈), `mod.data.generation.n`이 1 오르고 `mod.data.versions.modules`에 `KspBridge.Mod.Modules.Check.g…`가 있으며 `check_ship`이 동작하는지 본다. 이어서 plan 검증 전략대로 C1~C3·C9·C10을 이 세션의 `kspbridge` 도구 응답으로 확인(엔드포인트를 직접 부르지 않는다. 앞선 확인은 직접 호출이라 무효). 장면을 바꾸기 전에 `get_status`로 다른 비행이 없는지 본다. C3 시험 기체는 발사 클램프 없는 액체 연료 단일 엔진 2단 로켓(D038), 손 계산 과정을 보고에 적는다. C10은 우주센터, `editor_load` 직후, `editor_enter` 시설 전환 직후(`facility` 확인), 잘못된 인자, Player.log 예외 없음 (작업 대상 파일 없음) — C1, C2, C3, C9, C10, C11
- [x] T023 C4·C8(이 세션의 `editor_*`·`check_ship` 도구로만 확인): 경고 8종마다 그 결함만 가진 기체를 `editor_*` 도구로 만들어 그 경고만 나는지, 고친 뒤 안 나는지, 결함 없는 2단 로켓(명령 모듈·낙하산·분리기·연료 탱크·엔진)이 경고 없이 점검되는지 확인. `fuel_blocked`는 상단 스테이지 엔진에서 대칭으로 끊기는 구성. 시험 craft는 확인 뒤 지운다 (작업 대상 파일 없음) — C4, C5, C6, C7, C8
- [x] T024 C11: `git -C <대상> status --porcelain`(미추적 새 파일 포함)과 `git -C <대상> diff --stat <기준점>`의 경로가 모두 새 모듈 폴더, 새 테스트 폴더, `KspBridge.slnx`, `ReadMe.md`, `docs/`, `src/KspBridge.Server/Program.cs`·`src/KspBridge.Server/ToolListSubscriptions.cs`, `tests/KspBridge.Server.Tests/McpEndpointTests.cs` 안인지 확인. 핫리로드는 T031(서버 모듈)과 T022 첫 단계의 결과(세대 번호 오름, check 모듈 있음, `check_ship` 동작)로 기록 (작업 대상 파일 없음) — C11

체크포인트: 모든 완료 조건의 확인 결과를 명령·응답과 함께 보고할 수 있고, 구현 후 검토로 넘어갈 수 있다.

## 단계 6: 구현 후 검토 1회차 반영(D075~D082)

- [x] T038 임시 진단 처리기 `check/diag`·`NoiseDiag.cs`와 임시 도구 `check_diag` 삭제(판정 정밀도 조사용, D076 근거 수집 뒤) (src/KspBridge.Mod.Modules.Check/NoiseDiag.cs, src/KspBridge.Mod.Modules.Check/CheckModule.cs, src/KspBridge.Server.Modules.Check/CheckTools.cs) — C11
- [x] T039 `fuel_blocked`의 "실려 있음"을 엔진 작동 스테이지 s에 남은 부품 가운데 그 엔진의 연료 추진제(무시 목록 제외)를 에디터 현재량 0보다 크게 가진 부품의 스테이지 s 시작 연료 질량(0.001 t 반올림으로 0보다 큼)으로 한정(D075·D080·D082), 판정 정밀도를 응답 반올림 값으로(어긋남 0.001 m, 연료 0.001 t, `MassEpsilon` 삭제, D076), 합계를 반올림한 스테이지 값의 합으로(D078) (src/KspBridge.Mod.Modules.Check/Checks.cs) — C2, C5, C6, C7
- [x] T040 분리기 판정을 게임대로: 분리 노드가 비면 아무것도 떼지 않고, omni 분리기는 부모가 있으면 자기 자신도 뗀다(D077) (src/KspBridge.Mod.Modules.Check/ShipSnapshot.cs) — C5, C7
- [x] T041 `check/start` 처리기에서 `Begin`이 예외를 내면 `Restore()`하고 진행 중 점검을 비운 뒤 다시 던진다(plan 오류·위험 "게임 예외") (src/KspBridge.Mod.Modules.Check/CheckRunner.cs) — C1, C10
- [x] T042 정리: 쓰지 않는 `CheckRunner.Waiting`·`PartData.Name`·`Vec.Cross` 삭제, `CheckRunner.cs`의 불필요한 전체 이름, 테스트 csproj 주석에 `BurnSignal` 추가, `ModuleHost.cs` 주석 "모든 MCP 세션이 공유" → "모든 MCP 요청이 공유"(D079), 테스트 도우미의 `PartData.Name` 설정도 지움 (tests/KspBridge.Mod.Modules.Check.Tests/ChecksTests.cs, src/KspBridge.Mod.Modules.Check/CheckRunner.cs, src/KspBridge.Mod.Modules.Check/ShipData.cs, src/KspBridge.Mod.Modules.Check/ShipSnapshot.cs, src/KspBridge.Mod.Modules.Check/Vec.cs, tests/KspBridge.Mod.Modules.Check.Tests/KspBridge.Mod.Modules.Check.Tests.csproj, src/KspBridge.Server/Modules/ModuleHost.cs) — C11
- [x] T043 단위 테스트: `fuel_blocked`가 스테이지 전에 떨어지는 부품의 연료를 세지 않음(D075), 0.001 m·0.001 t 아래 잡음은 경고 없음·그 이상은 경고(D076), 합계 = 반올림한 스테이지 값의 합(D078), 부품은 있고 스테이지 대상이 없는 기체는 스테이지 표가 빔(C2), 분리기의 `DetachedRoots`가 비면 아래 부품이 남고 자기 자신이 들어 있으면 함께 떨어짐(D077), `fuel_blocked`의 "실려 있음"은 스테이지 s 시작 연료 질량으로: 남아 있지만 앞 스테이지가 비운 탱크는 경고 없음, 0.0004 t면 없음·0.001 t면 있음, 그 엔진의 추진제가 없는 연료 부품만 남으면 경고 없음, 무시 목록 추진제(이온 엔진의 ElectricCharge)와 그 엔진이 쓰지 않는 다른 연료(MonoPropellant를 쓰는 다른 엔진이 있어 연료가 됨)를 함께 가진 부품만 남으면 경고 없음(D075·D076·D080·D082) (tests/KspBridge.Mod.Modules.Check.Tests/ChecksTests.cs) — C2, C5, C6, C7
- [x] T044 서버 단위 테스트: 허가하지 않은 listen이 확인 응답 뒤 끝남, 짧은 유지 신호 간격에서 유지 신호가 옴, 서버 종료(`ApplicationStopping`) 때 스트림이 닫힘 (tests/KspBridge.Server.Tests/McpEndpointTests.cs) — C12
- [x] T045 기준 spec 반영: `docs/features/assembly-check.md` R11의 코어 목록을 spec C11에 맞추고 서버 호스트 변경은 server-hosting 소관이라고 적는다. `docs/features/server-hosting.md`의 `파일:` 줄에 `src/KspBridge.Server/Modules/ModuleHost.cs`를 더한다(이력은 기존 조립-점검 줄 유지) (docs/features/assembly-check.md, docs/features/server-hosting.md) — C11
- [x] T046 `dotnet build KspBridge.slnx` 경고 0, `dotnet test` 통과 (작업 대상 파일 없음) — C11
- [x] T047 KSP와 서버를 끄지 않고 두 check 모듈을 게시한 뒤(서버 호스트 주석 변경은 다음 서버 호스트 게시 때 반영되며 동작은 같음), 이 세션의 `editor_*`·`check_ship`으로: (1) 연료가 실린 탱크가 그 엔진 스테이지에도 남아 있지만 흐름 규칙상 받지 못하는 기체에서 `fuel_blocked`가 나고(탱크와 엔진의 분리 단위가 다르면 그 탱크의 `no_engine_fuel`도 함께, D083) 고치면 사라짐(D075), 상단 탱크가 빈 기체에서는 `fuel_blocked`가 나지 않음, (2) 포드 하나짜리 기체가 스테이지 없음으로 답함, (3) 결함 없는 2단 로켓·ComSat Lx가 경고 없이 앞선 값과 같음(회귀), (4) `check_diag`가 도구 목록에서 빠짐, (5) 분리 노드가 빈 분리기를 단 기체에서 그 분리기와 아래 부품이 떨어지지 않는 것으로 잡힘(D077, 스톡에 omni 분리기가 있으면 그것도), (6) Player.log 예외 없음(진단용 직접 읽음. T041 예외 경로는 일으킬 수 없어 코드로 확인). 시험 craft는 확인 뒤 지운다. C3 기록을 보충한다(응답 `start_mass_t`와 `mass_t`, Kerbin 해면 중력 TWR) (작업 대상 파일 없음) — C2, C3, C5, C7, C8, C10, C11
- [x] T048 C11 다시 확인: `git -C <대상> status --porcelain --untracked-files=all`과 `git -C <대상> diff --stat <기준점>`(기준점 eea768b)의 경로가 D079로 바뀐 집합(`src/KspBridge.Server/Modules/ModuleHost.cs` 포함) 안인지, `ModuleHost.cs` 변경이 주석뿐인지 확인 (작업 대상 파일 없음) — C11

체크포인트: 구현 후 검토 2회차로 넘어갈 수 있다.

## 단계 7: 구현 후 검토 2회차 반영(D084~D091)

- [x] T049 `low_launch_twr`를 응답처럼 0.01로 반올림한 해면 TWR로 판정(D084), 무게중심 어긋남 각도의 0 보호를 `toCom` 길이 0 비교로(D088) (src/KspBridge.Mod.Modules.Check/Checks.cs) — C6, C7
- [x] T050 `Vec.Normalized`의 문턱을 길이 0 비교로(계산식은 그대로, D088) (src/KspBridge.Mod.Modules.Check/Vec.cs) — C6
- [x] T051 분리기가 떼어 내는 쪽을 고르는 순수 함수(omni 여부, 자기 식별자, 부모, 분리 노드에 붙은 부품, 자식들 → 떼어 내는 루트 목록)를 `ShipData.cs`에 두고, `ShipSnapshot.SeparatorOf`는 게임 값만 넘겨 그 함수를 부른다(D085·D077) (src/KspBridge.Mod.Modules.Check/ShipData.cs, src/KspBridge.Mod.Modules.Check/ShipSnapshot.cs) — C5, C7
- [x] T052 listen 처리기: 도구를 허가했으면 확인 응답 전에 스트림을 목록에 넣고, 확인 응답 전송이 실패하거나 기다림이 끝나면 목록에서 뺀다. 확인 응답 전송 직후 불리는 테스트용 internal 이음매를 둔다(D086·D091) (src/KspBridge.Server/ToolListSubscriptions.cs) — C12
- [x] T053 단위 테스트: 떼어 내는 쪽 순수 규칙(빈 노드 → 없음, 노드에 부모 → 자기 자신, 노드에 자식 → 그 자식, omni 부모 있음 → 자식들과 자기 자신, omni 부모 없음 → 자식들, D085), `low_launch_twr` 해면 TWR 0.995 경고 없음·0.99와 0 경고(D084·D089), `no_engine_fuel` 시작 연료 0.0004 t 경고 없음·0.001 t 경고(D087), `Vec.Normalized` 영벡터 입력은 영벡터, 길이 1e-20처럼 1e-12보다 작고 제곱이 아래로 넘치지 않는 길이에서 길이 1의 단위 벡터(D088) (tests/KspBridge.Mod.Modules.Check.Tests/ChecksTests.cs) — C5, C6, C7
- [x] T054 서버 단위 테스트: 확인 응답 전송 직후 불리는 테스트용 이음매 안에서 `OpenCount`가 1(옛 순서면 0이라 늘 실패, D086·D091) (tests/KspBridge.Server.Tests/McpEndpointTests.cs) — C12
- [x] T055 기준 spec 반영 확인: 이번 단계는 새 파일·요구 변화가 없어 `docs/features/assembly-check.md`·`docs/features/server-hosting.md`의 `파일:` 줄과 요구가 그대로 맞는지 확인한다(바꿀 것이 생기면 고친다) (docs/features/assembly-check.md, docs/features/server-hosting.md) — C11
- [x] T056 `dotnet build KspBridge.slnx` 경고 0, `dotnet test` 통과 (작업 대상 파일 없음) — C11
- [x] T057 KSP와 서버를 끄지 않고 두 check 모듈을 게시한 뒤 이 세션의 `get_status`·`check_ship`으로: 세대가 오름, 시험 2단 로켓·ComSat Lx가 T047 값과 같고 경고 없음(회귀), Player.log 예외 없음(진단용 직접 읽음) (작업 대상 파일 없음) — C2, C8, C10, C11
- [x] T058 서버 호스트 재배포(D086): 사용자에게 알린 뒤 사용자가 서비스를 멈추고, 서버 호스트를 게시하고, 사용자가 서비스를 다시 시작하고 Claude Code에서 MCP를 다시 연결한다(D061). 이 세션의 `get_status`가 동작하고, check 서버 모듈에 임시 도구 `check_c12_probe`를 더해 게시하면 다시 연결 없이 도구 목록에 나타나 불리는지 본 뒤 임시 도구를 지워 다시 게시한다(D064). 임시 도구를 게시·원복하는 동안 `get_status`가 계속 동작하는지도 본다. 서버 로그에 listen 열림이 남는지 본다(진단용 직접 읽음). 실패하면 멈추고 묻는다 (src/KspBridge.Server.Modules.Check/CheckTools.cs 임시 수정 후 원복) — C12
- [x] T059 C11 다시 확인: `git -C <대상> status --porcelain --untracked-files=all`과 `git -C <대상> diff --stat <기준점>`(기준점 eea768b)의 경로가 D079 집합 안이고 `check_c12_probe`가 남지 않았는지 (작업 대상 파일 없음) — C11

체크포인트: 구현 후 검토 3회차로 넘어갈 수 있다.

## 단계 8: 구현 후 검토 3회차 뒤 득실 점검(D096·D097)

구현 후 검토 3회차에서 정한 D092·D095(테스트용 이음매를 확인 응답 앞으로)는 D097로 하지 않는다. 서버 호스트는 바뀌지 않는다.

- [x] T060 `low_launch_twr`를 반올림 전 해면 TWR로 판정하고, 경고의 메시지와 `values.twr_asl`은 셋째 자리까지 보인다(D096, D084 되돌림) (src/KspBridge.Mod.Modules.Check/Checks.cs) — C7
- [x] T061 단위 테스트: 해면 TWR 0.995는 경고(`values.twr_asl` 0.995, 메시지에 0.995), 1.0은 경고 없음, 0은 경고(D089·D096) (tests/KspBridge.Mod.Modules.Check.Tests/ChecksTests.cs) — C7
- [x] T062 기준 spec 반영 확인: 새 파일·요구 변화가 없고 D096으로 spec C7·기준 spec의 "1 미만"과 다시 맞으므로 `docs/features/assembly-check.md`·`docs/features/server-hosting.md`가 그대로 맞는지 확인한다 (docs/features/assembly-check.md, docs/features/server-hosting.md) — C11
- [x] T063 `dotnet build KspBridge.slnx` 경고 0, `dotnet test` 통과 (작업 대상 파일 없음) — C11
- [x] T064 KSP를 끄지 않고 check Mod 모듈을 게시한 뒤 이 세션의 `get_status`로 Mod 세대가 오르는지, `check_ship`으로 시험 2단 로켓·ComSat Lx가 T057 값과 같고 경고 없음인지 확인한다. Player.log 예외 없음(진단용 직접 읽음) (작업 대상 파일 없음) — C7, C10, C11
- [x] T065 C11 다시 확인: `git -C <대상> status --porcelain --untracked-files=all`과 `git -C <대상> diff --stat <기준점>`(기준점 eea768b)의 경로가 D079 집합 안인지 (작업 대상 파일 없음) — C11

체크포인트: 단계 8 뒤에는 추가 검토 회차 없이 커밋 승인으로 넘어간다(D097). 남은 낮은 중요도 항목은 문제가 생기면 개별로 조치한다.

## 의존 관계

- T001은 T004·T005보다 먼저(프로젝트가 있어야 함). T002·T003은 T001과 파일이 겹치지 않지만 `KspBridge.slnx`를 함께 고치므로 같은 단계 안에서 차례로 등록한다. T004 뒤 T005(진단 처리기가 T004의 계산 제어를 씀), T005 뒤 T006.
- 단계 1의 T006은 단계 2보다 먼저 한다. 진단 결과(`stageFuelMass` 키, 분리기 떼어 내는 쪽, 부품 집합 일치, 투하 질량, 좌표계)에 따라 `ShipData`·`Checks` 설계가 바뀔 수 있기 때문이다.
- T007·T008·T009는 서로 독립이라 동시에 할 수 있다. T010은 T006 뒤, T011은 T007·T010 뒤. 테스트 T012는 T008 뒤, T013은 T009 뒤, T014는 T011 뒤.
- T015는 T010 뒤. T016은 T004·T009·T011·T015 뒤. T017은 T008·T016 뒤. T018은 인터페이스가 plan에 정해져 있어 단계 3 안에서 동시에 할 수 있다. T019는 T017 뒤(진단 처리기를 쓰는 T006이 끝났고 처리기 등록 코드를 정리할 때).
- T021(기준 spec assembly-check)은 단계 3에서 마쳤다. 단계 4는 assembly-check의 파일·요구를 바꾸지 않으므로 다시 반영하지 않는다. 단계 5는 새 파일 둘(`BurnSignal.cs`·`BurnSignalTests.cs`)을 더하므로 T037로 `파일:` 줄을 맞춘다. T020과 함께 다른 작업과 파일이 겹치지 않아 동시에 할 수 있었다.
- 단계 4: T025 뒤 T026(처리기를 연결), T026 뒤 T027. T028은 다른 작업과 파일이 겹치지 않아 동시에 할 수 있다. T029는 spec-driven 3절 2번의 마지막 구현 작업이라 T025~T028 뒤, T030 전에 한다. T030은 T025~T027 뒤(빌드·테스트 통과 뒤 배포), T031은 T030 뒤.
- 단계 5는 단계 4 뒤(이 세션의 `kspbridge` 도구에 `check_ship`이 있어야 함). T032 뒤 T035, T032·T033·T035 뒤 T037(단계 5의 마지막 구현 작업), T037 뒤 T036(빌드·테스트 통과 뒤 게시), T036 뒤 T034, T034 뒤 T022·T023. T024는 마지막에 한다.
- 단계 6은 구현 후 검토 1회차 뒤. T038·T039·T040·T041은 파일이 겹치지 않아 차례와 상관없다. T040·T041 뒤 T042(같은 `ShipSnapshot.cs`·`CheckRunner.cs`). T039·T040 뒤 T043(그 판정의 테스트). T042 뒤 T043(같은 `ChecksTests.cs`). T044는 독립. T045는 단계 6의 마지막 구현 작업이라 T038~T044 뒤. T046은 T045 뒤, T047은 T046 뒤, T048은 T047 뒤.
- 단계 7은 구현 후 검토 2회차 뒤. T049·T050·T051·T052는 파일이 겹치지 않아 차례와 상관없다. T049·T050·T051 뒤 T053(그 규칙의 테스트, 같은 `ChecksTests.cs`). T052 뒤 T054. T055는 단계 7의 마지막 구현 작업이라 T049~T054 뒤. T056은 T055 뒤, T057은 T056 뒤, T058은 T057 뒤(서버 호스트 재배포는 Mod·서버 모듈 확인을 마친 뒤), T059는 T058 뒤.
- 단계 8은 구현 후 검토 3회차 뒤. T060 뒤 T061(같은 판정의 테스트). T062는 단계 8의 마지막 구현 작업이라 T060·T061 뒤. T063은 T062 뒤, T064는 T063 뒤, T065는 T064 뒤.
