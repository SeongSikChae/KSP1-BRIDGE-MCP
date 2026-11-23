# 기동 노드 생성과 실행 계획

## 요약

새 Mod 모듈과 MCP 서버 모듈 쌍 `maneuver`가 MCP 도구 `create_node`·`get_nodes`·`delete_node`와 단계 종류 `execute_node`를 더한다. 코어는 고치지 않는다. 노드 시각·목표 Δv·성분 변환·Ap·Pe 시각·인자·준비 판정·실행 진행은 게임에 닿지 않는 규칙으로 두어 게임 없이 단위 테스트하고, 게임에 닿는 부분(노드 읽기·만들기·지우기, 단계)은 얇게 둔다.

`execute_node`의 연소(10 m/s 방향 고정, 5° 게이트, 스로틀 줄이기, 다시 늘어 완료, 자동 스테이징·엔진 소진)는 `circularize`와 같은 판정이어야 하므로(spec C5), ascent 모듈의 `CircularizeLogic`에서 연소 판정을 공개 형식으로 떼어 두 단계가 함께 쓴다. `warp`의 노드 목표(spec C6)는 warp 모듈이 maneuver 모듈의 공개 노드 읽기를 불러 정한다.

이 문서에서 "디컴파일"은 spec.md 용어 절과 같다.

## 바꿀 파일·구조

새 파일(Mod 모듈):

- src/KspBridge.Mod.Modules.Maneuver/KspBridge.Mod.Modules.Maneuver.csproj: `src/KspBridge.Mod.Modules.Ascent/KspBridge.Mod.Modules.Ascent.csproj`와 같은 구성에 flight·control·ascent 모듈 참조와 테스트용 `InternalsVisibleTo`.
- src/KspBridge.Mod.Modules.Maneuver/ManeuverModule.cs: 모듈 등록(처리기 `create`·`get`·`delete`, 단계 `execute_node`).
- src/KspBridge.Mod.Modules.Maneuver/NodeArgs.cs: `create_node`·`delete_node`·`execute_node` 인자 검증(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Maneuver/NodeAvailability.cs: 장면·기체·노드 사용 가능의 `not_ready` 판정과 이유(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Maneuver/NodeFrame.cs: 노드 시각의 위치·속도에서 게임과 같은 방향 셋을 만들고, Δv 성분과 벡터를 서로 바꾼다(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Maneuver/ConicTimes.cs: 원뿔곡선의 다음 Ap·Pe 시각(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Maneuver/NodeTime.cs: `create_node`의 시각 규칙(`ut`·`in_s`·`at`, 지금·마지막 노드·구간 끝 제약)과 착지·착수·발사 전 상태의 `bad_request`(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Maneuver/NodeGoal.cs: `goal`의 Δv 계산(Ap·Pe는 진행 성분, 원궤도는 진행·방사 성분)과 낼 수 없는 목표(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Maneuver/NodeInfo.cs: 한 노드의 값(UT, 성분, 노드 뒤 궤도 요소, 연소 추정 입력)을 담는 값 형식과 `get_nodes` 항목 만들기(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Maneuver/NodeStart.cs: 공개 `NodeStartReading`(맨 앞 노드의 날값)과 `NodeStart.Judge`(시작 실패 이유 또는 연소 시작 시각). `execute_node`와 `warp`의 노드 목표가 함께 쓴다(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Maneuver/NodeReader.cs: 활성 기체의 노드 목록을 `NodeInfo`로 읽는다. 공개 `FirstNodeStart`는 맨 앞 노드의 `NodeStartReading`을 읽어 `NodeStart.Judge`의 결과를 돌려준다(게임).
- src/KspBridge.Mod.Modules.Maneuver/NodeEditor.cs: 노드 만들기(추가, Δv 넣기, 목표 맞춤 보정, 노드 뒤 궤도 확인, 실패하면 지우기)와 지우기(게임).
- src/KspBridge.Mod.Modules.Maneuver/ExecuteNodeLogic.cs: `execute_node`의 진행(`NodeStart.Judge`의 시작 실패·시작 시각을 받아 국면, 연소 판정, 진행 값). 게임에 닿지 않음.
- src/KspBridge.Mod.Modules.Maneuver/ExecuteNodeStep.cs: 단계 `execute_node`(설명, 코어·규칙 연결, 노드 확인, 이벤트, 진행).

새 파일(서버 모듈, 테스트, 기준 spec):

- src/KspBridge.Server.Modules.Maneuver/KspBridge.Server.Modules.Maneuver.csproj: `src/KspBridge.Server.Modules.Measure/KspBridge.Server.Modules.Measure.csproj`와 같은 구성.
- src/KspBridge.Server.Modules.Maneuver/ManeuverTools.cs: `[assembly: KspBridgeModule("maneuver")]`과 도구 `create_node`·`get_nodes`·`delete_node`.
- tests/KspBridge.Mod.Modules.Maneuver.Tests/KspBridge.Mod.Modules.Maneuver.Tests.csproj: `tests/KspBridge.Mod.Modules.Ascent.Tests/KspBridge.Mod.Modules.Ascent.Tests.csproj`와 같은 구성에 maneuver·ascent·control·flight 모듈 참조.
- tests/KspBridge.Mod.Modules.Maneuver.Tests/NodeArgsTests.cs, NodeAvailabilityTests.cs, NodeFrameTests.cs, ConicTimesTests.cs, NodeTimeTests.cs, NodeGoalTests.cs, NodeInfoTests.cs, NodeStartTests.cs, ExecuteNodeLogicTests.cs: 단위 테스트.
- tests/KspBridge.Mod.Modules.Ascent.Tests/BurnControlTests.cs: 떼어 낸 연소 판정의 단위 테스트.
- docs/features/flight-maneuver.md: 새 기능의 기준 spec(마지막 구현 단계에서 만든다).

고치는 기존 파일:

- src/KspBridge.Mod.Modules.Ascent/CircularizeLogic.cs: 연소 판정(방향 고정, 게이트, 스로틀, 완료, 다시 늘어 완료, 엔진 소진, 스테이징 이유)을 `BurnControl`로 옮기고 그것을 부른다. 동작은 그대로다(기존 `CircularizeLogicTests`가 고치지 않고 통과).
- src/KspBridge.Mod.Modules.Ascent/BurnControl.cs(새 파일): 공개 연소 판정.
- src/KspBridge.Mod.Modules.Ascent/AscentReading.cs, AscentReader.cs, BurnEstimate.cs, StageRule.cs, AutoStager.cs, AoaLimit.cs, LaunchAzimuth.cs: maneuver 모듈이 쓰는 형식·함수를 공개로 한다(동작 변화 없음).
- src/KspBridge.Mod.Modules.Warp/WarpArgs.cs: `to`에 `node`.
- src/KspBridge.Mod.Modules.Warp/WarpTarget.cs: 노드 목표는 `NodeStart.Judge`의 결과를 받아, 실패면 그 이유로 실패하고 아니면 연소 시작 시각 − `lead_s`를 목표 UT로 한다(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Warp/IWarpGame.cs, GameWarp.cs: `ReadNode()`(maneuver 모듈의 `NodeReader.FirstNodeStart`를 부름).
- src/KspBridge.Mod.Modules.Warp/WarpSession.cs: 노드 목표일 때 `ReadNode`를 넘긴다.
- src/KspBridge.Mod.Modules.Warp/WarpStep.cs: 설명에 `node` 목표와 실패 조건.
- src/KspBridge.Mod.Modules.Warp/KspBridge.Mod.Modules.Warp.csproj: maneuver 모듈(과 그것이 쓰는 ascent·control) 참조.
- tests/KspBridge.Mod.Modules.Warp.Tests/KspBridge.Mod.Modules.Warp.Tests.csproj: maneuver·ascent·control 모듈 참조를 더한다(시험 프로젝트는 쓰는 모듈을 모두 직접 참조한다).
- tests/KspBridge.Mod.Modules.Warp.Tests/FakeWarpGame.cs, WarpArgsTests.cs, WarpTargetTests.cs, WarpSessionTests.cs: 노드 목표(세션이 노드 목표를 `ReadNode`로 받는 것 포함).
- src/KspBridge.Mod.Modules.Control/AttitudeSteps.cs: `attitude` 설명의 hold가 끝나는 조건에 `execute_node`. 
- src/KspBridge.Mod.Modules.Control/ThrottleStep.cs: 단계 설명에 `execute_node`의 스로틀.
- src/KspBridge.Server.Modules.Core/CoreTools.cs: `get_flight_events` 설명에 `node_executed`. flight-plan의 파일이지만 요구는 바뀌지 않는다.
- KspBridge.slnx: 새 프로젝트 셋을 더한다.
- ReadMe.md: 도구 셋과 단계 `execute_node`, `warp`의 `node` 목표, 호만 전이 예시 계획. 다른 기준 spec의 `파일:` 줄에도 있지만 그 요구는 바뀌지 않는다.
- docs/features/flight-warp.md, docs/features/flight-control.md: 요구와 `파일:` 줄·이력(마지막 구현 단계).
- docs/features/flight-ascent.md: 요구는 바뀌지 않고 `파일:` 줄(`BurnControl.cs`·`BurnControlTests.cs`)과 이력(마지막 구현 단계).

배포 구성(`Directory.Build.targets`)은 모듈을 이름 패턴으로 모으므로 고치지 않는다. 새 패키지는 없다.

## 결정

- 코어를 고치지 않는다: 도구 처리기(`ModuleContext.AddHandler`), 실행 중인 계획 확인(`ModuleContext.Coordinator.Running`, debug 모듈과 같다), 매 프레임 판정, 이벤트, 계획이 끝날 때의 처리는 이미 있다 / 검토한 대안: 노드 전용 감시를 코어에 둠 — 단계와 처리기 안에서 끝나므로 쓰지 않음
- 게임에 닿는 부분과 닿지 않는 부분을 나눈다: 시각·목표·성분·Ap·Pe 시각·진행은 위치·속도·요소 값만 받아 게임 없이 시험하고, `NodeReader`·`NodeEditor`·단계만 게임에 닿는다 / 검토한 대안: 단계와 처리기에 모두 둠 — 호만 목표의 1 m, 성분 변환, `at` 시각 규칙을 게임 없이 시험할 수 없음
- 연소 판정은 ascent 모듈에서 `BurnControl`로 떼어 `circularize`와 `execute_node`가 함께 쓴다: spec C5가 "`circularize`와 같은 판정"이라 같은 코드여야 어긋나지 않는다 / 검토한 대안: maneuver 모듈에 복사 — 두 벌이 어긋날 수 있음. `CircularizeLogic`을 그대로 씀 — 원궤도 속도를 목표로 하는 코드라 노드의 연소 벡터를 넣을 수 없음
- 게시하면 어셈블리 이름이 `<이름>.g<표식>`으로 바뀌어 `InternalsVisibleTo`로 묶을 수 없으므로(궤도-진입 plan과 같은 이유) maneuver 모듈이 쓰는 ascent 형식은 공개로 한다 / 검토한 대안: 없음
- `warp`의 노드 목표는 warp 모듈이 maneuver 모듈의 `NodeReader.FirstNodeStart`를 불러 정한다: 노드 읽기, 시작 실패 판정, 연소 시작 시각을 maneuver 모듈(`NodeStart.Judge`) 한 곳에 두어 `get_nodes`·`execute_node`와 같은 값이 된다(spec C6 "연소 시작 시각 − `lead_s`") / 검토한 대안: warp 모듈이 노드를 따로 읽음 — 예상 연소 시간이 두 벌. maneuver 모듈이 warp에 목표를 등록 — 코어에 확장 지점이 필요
- 성분과 벡터의 변환은 게임의 식을 그대로 따른다: 노드 시각 구간의 상태 벡터(`Orbit.GetOrbitalStateVectorsAtUT`)를 `xzy`로 바꾼 위치 r·속도 v에서, 진행 Z = v의 단위 벡터, 법선 Y = `Cross(−r, v)`의 단위 벡터, 방사 X = `Cross(Y, Z)`(Unity `LookRotation`의 축)이고 `DeltaV` = (X·Δv, Y·Δv, Z·Δv)이다(디컴파일 `PatchedConicSolver.CheckNextManeuver` 859~866행). 노드 뒤 궤도는 게임이 r과 v + Δv로 만든다(`Orbit.UpdateFromFixedVectors`). 왼손 좌표의 외적은 실제 외적의 반대이므로 Y는 실제 r × v, X는 천체에서 멀어지는 쪽이 되어 spec 용어의 방향과 같을 것으로 본다(미확인, 위험 1) / 검토한 대안: 부호를 바꾼 축 — 근거 없음
- 목표 Δv(`NodeGoal`)는 노드 시각의 r·v에서 계산한다. Ap·Pe 목표는 v 방향 크기 d만 바꾸며(진행 성분만), d의 범위(−|v|, 탈출 속력 − |v|)에서 이분법으로 1 mm까지 찾는다. 원궤도는 v에서 r 방향 성분을 뺀 방향으로 원궤도 속력 √(μ/|r|)인 속도와 v의 차이다. 계산한 뒤 게임이 만든 노드 뒤 궤도의 Ap(Pe)가 목표와 0.5 m를 넘게 다르면, 게임 값으로 진행 성분을 할선법으로 고친다(최대 5번). 그래도 1 m 안에 들지 않으면 노드를 지우고 이유와 함께 `bad_request`(spec C2 "기준 궤도로 낼 수 없는 목표")이다. `circular`는 보정하지 않고 확인만 한다: 게임이 만든 노드 뒤 궤도의 Ap·Pe가 모두 노드 시각의 고도와 1 m 안이 아니면 노드를 지우고 이유와 함께 `bad_request`이다(원궤도 속도를 직접 넣는 해석해라 어긋날 가능성이 낮고, 어긋나면 C9 호만 전이에서 드러난다) / 검토한 대안: 게임 값만으로 찾음 — 노드를 여러 번 고쳐야 하고 게임 없이 시험할 수 없음. 해석해만 씀 — 게임 계산과의 어긋남을 알 수 없음
- Ap(Pe) 목표를 낼 수 있는지는 같은 이분법의 범위 끝 값으로 정한다: 목표가 범위 끝의 Ap(Pe) 사이에 없으면 낼 수 없는 목표이다. 범위 끝 Ap는 노드 시각 반지름(속력 0)과 탈출(무한)이고, Pe는 0(속력 0)과 탈출 직전의 Pe이다. 결과 궤도는 닫힌 궤도로 둔다(Ap 반지름 < 영향권 반지름)
- 노드 시각 규칙(`NodeTime`): 활성 기체가 착지·착수·발사 전이면 시각을 정하기 전에 이유와 함께 `bad_request`이다(spec C2). `in_s`는 지금 UT + `in_s`, `at`은 기준 궤도의 다음 Ap(Pe) 시각을 `ConicTimes`로 지금 UT와 마지막 노드 UT 가운데 늦은 쪽 t₀보다 엄격히 뒤로 정한다(t₀와 1 ms 안이면 한 주기 뒤). 제약: 지금 UT보다 뒤, 마지막 노드 UT보다 뒤, 기준 궤도의 끝 전이(`patchEndTransition`)가 `ESCAPE`·`ENCOUNTER`이면 그 `EndUT`보다 앞(spec C2, D013). `IMPACT`는 `PatchedConics`·`PatchedConicSolver`에서 정하는 곳이 없어 따로 다루지 않는다(디컴파일 검색)
- 다음 Ap·Pe 시각(`ConicTimes`)은 요소에서 직접 낸다: 닫힌 궤도는 그 시각의 근점 이후 시간 ObT(`Orbit.getObtAtUT`)와 주기로 다음 Pe(ObT = 0)·Ap(ObT = 주기/2), 열린 궤도는 ObT < 0일 때만 Pe(지금 − ObT 뒤)이고 Ap는 없다. spec C3의 노드 뒤 궤도 첫 Ap·Pe UT와 C2의 `at`이 같은 함수를 쓴다(D011·D012) / 검토한 대안: `Orbit.GetNextApoapsisTime` — 같은 시각에서의 "뒤"가 엄격한지 디컴파일로 확정할 수 없음
- 노드 사용 가능(`NodeAvailability`, D008): `ScenarioUpgradeableFacilities.Instance`가 없으면 해금. 있으면 추적소 등급(`ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.TrackingStation)`)으로 `GameVariables.Instance.GetOrbitDisplayMode`가 `PatchedConics`인지, 관제 센터 등급으로 `GameVariables.Instance.UnlockedFlightPlanning`이 참인지 본다. 해금인데 기체의 `patchedConicSolver`가 없으면 `not_ready`(이유: 궤도 계산기가 아직 없음)이다
- `create_node`는 `PatchedConicSolver.AddManeuverNode(UT)`로 만들고 `ManeuverNode.OnGizmoUpdated(DeltaV, UT)`로 Δv를 넣는다: 이 함수는 Δv와 UT를 넣고 `UpdateFlightPlan`을 부른 뒤 화면 조작기가 없으면 바로 돌아간다(디컴파일 `ManeuverNode` 40~60행). 노드 뒤 궤도는 그 호출 안에서 계산된다(`UpdateFlightPlan` → `CheckNextManeuver`). 지우기는 게임과 같이 `ManeuverNode.RemoveSelf`(편집 조작기 떼기·지도 객체 정리 뒤 `PatchedConicSolver.RemoveManeuverNode`, 디컴파일 `ManeuverNode` 171~193행)로 한다(구현 후 검토 1회차) / 검토한 대안: `DeltaV`에 직접 넣고 `UpdateFlightPlan` — 같은 일을 두 줄로 함
- 실행 중인 계획이 있으면 `create_node`·`delete_node`는 `bad_request`이다(D007). `get_nodes`는 계획과 상관없이 응답한다
- MCP 도구는 원래 인자를 그대로 Mod로 넘기고 Mod가 모르는 인자·종류·규칙을 판정한다: 도구 메서드가 `RequestContext<CallToolRequestParams>`를 받아(SDK 2.4.0 문서의 도구 예) `Params.Arguments`를 넘긴다. 인자는 스키마 설명을 위해 선언하되 종류는 `JsonElement?`로 두어, 종류가 틀린 값도 SDK 바인딩에서 막히지 않고 Mod의 `bad_request`가 된다(spec C2·C4 "인자 규칙에 맞지 않거나 모르는 인자") / 검토한 대안: 수 형식으로 선언 — 종류가 틀리면 SDK 오류가 되어 `bad_request`가 아님. 모르는 인자는 SDK가 무시해 걸러지지 않음
- `execute_node`의 시작 시각은 `Begin`에서 한 번 정한다: 노드 UT − 예상 연소 시간(남은 Δv 기준) ÷ 2 (spec C5). 실행 중의 노드 확인은 시작 때의 `ManeuverNode` 참조가 여전히 목록의 맨 앞인지로 한다(바뀌거나 없어지면 실패) / 검토한 대안: UT 비교 — 같은 UT의 다른 노드를 구분하지 못함
- `execute_node`는 시작할 때와 연소 전 기다리는 동안 매 프레임 `PatchedConicSolver.UpdateFlightPlan()`으로 노드 뒤 궤도를 지금 궤도 기준으로 다시 계산하고, 연소 중에는 다시 계산하지 않는다(D016. 게임은 노드 뒤 궤도를 노드를 고칠 때만 만들어, 노드를 만든 뒤 바뀐 기체 궤도를 따르지 않는다).
- 남은 Δv는 매 프레임 `ManeuverNode.GetBurnVector(vessel.orbit)`(세계 좌표)이다. 자세 목표는 그 벡터를 `AoaLimit.PitchHeading`으로 피치·방위로 바꿔 hold에 넣는다(`circularize`와 같다). 완료 때의 성분(D010)은 그때 기체 궤도의 상태 벡터로 `NodeFrame`을 만들어 나눈다
- 서버 모듈은 도구 셋을 두고 이름을 `maneuver`로 한다 / 검토한 대안: 도구를 flight 서버 모듈에 둠 — C1의 모듈 쌍과 어긋남

## 인터페이스·데이터

### 게임 동작의 출처

- 노드 목록·정렬·추가·삭제: `PatchedConicSolver.maneuverNodes`, `UpdateFlightPlan`(448행, 정렬 후 맨 앞 노드를 품은 구간을 찾지 못하면 지난 노드를 지움), `AddManeuverNode`(907행), `RemoveManeuverNode`(952행). 구간 찾기 `FindPatchContainingUT`(577행)는 지난 시각이 첫 구간의 시작 이하이면 첫 구간을 돌려주므로 노드 시각이 지난 노드는 지워지지 않을 것으로 본다(위험 4).
- 노드 Δv·노드 뒤 궤도: `ManeuverNode.OnGizmoUpdated`(40행), `PatchedConicSolver.CheckNextManeuver`(859~887행: `xzy` 상태 벡터, `LookRotation`, `UpdateFromFixedVectors`, 닫힌 노드 뒤 궤도의 `EndUT` = 노드 UT + 주기).
- 연소 벡터: `ManeuverNode.GetBurnVector`(195행, `(nextPatch 속도 − 지금 궤도 속도).xzy`, 노드 뒤 궤도가 없으면 0).
- 구간 끝: `PatchedConics.CalculatePatch`(닫힌 궤도 `FINAL`, `EndUT` = 시작 + 주기. 이탈·진입이면 `ESCAPE`·`ENCOUNTER`).
- 궤도 값: `Orbit.ApA`·`PeA`·`eccentricity`·`inclination`·`period`·`getObtAtUT`(985행)·`GetOrbitalStateVectorsAtUT`·`referenceBody`.

### 인자(`NodeArgs`)

- `create_node`: 키 `ut`·`in_s`·`at`·`prograde_mps`·`normal_mps`·`radial_mps`·`goal`, `goal` 안의 키 `apoapsis_m`·`periapsis_m`·`circular`. 수는 유한해야 한다. 규칙은 spec C2와 같다.
- `delete_node`: 키 `index`(0 이상 정수)·`all`(참만).
- `execute_node`: 키 `tolerance_mps`(0 < x ≤ 100, 기본 0.5)·`auto_stage`(기본 참). 계획 제출 때 `bad_request`.

### `NodeInfo`와 `get_nodes` 항목

- `index`, `ut`, `in_s`(UT − 지금, 지났으면 음수), `prograde_mps`(`DeltaV.z`), `normal_mps`(`DeltaV.y`), `radial_mps`(`DeltaV.x`), `dv_mps`, `burn_time_s`, `burn_start_ut`, `after`(`body`, `apoapsis_m`(열린 궤도 `null`), `periapsis_m`, `eccentricity`, `inclination_deg`, `next_apoapsis_ut`, `next_periapsis_ut`) 또는 `null`과 `after_missing`(이유). 맨 앞 노드에 `remaining_dv_mps`.
- 예상 연소 시간은 `BurnEstimate.Seconds(Δv, 스테이지 표, 켜진 엔진의 진공 최대 추력 ÷ 질량)`이다. 입력은 `AscentReader.Read`의 값이며, 맨 앞 노드는 남은 Δv, 나머지는 노드 Δv 크기다. `null`이거나 노드 뒤 궤도가 없으면 두 값이 `null`.

### `ExecuteNodeLogic`

- 시작 실패(spec C5)와 시작 시각은 `NodeStart.Judge`의 결과를 쓴다. 조종 불가·기체 없음은 단계가 본다.
- 기다림: 지금 UT ≥ 시작 시각이면 연소 국면(이미 지났으면 첫 프레임).
- 연소 국면: `BurnControl`에 남은 Δv 벡터와 값을 넣는다. 추력을 원함 = 연소 국면이고 남은 Δv > `tolerance_mps`.
- 진행 값: `phase`, `start_ut`, `burn_time_s`, `remaining_dv_mps`, `nose_error_deg`, `throttle`, `stages`(spec C8).

### `BurnControl`(ascent)

- 입력: 남은 Δv 벡터, 연소 국면 여부, `AscentReading`, `tolerance_mps`, `auto_stage`. 출력: 피치·방위, 스로틀, 추력을 원함, 스테이징 이유, 완료, 실패.
- 규칙: 지금 `CircularizeLogic.Tick`의 기다림 판정 뒤 부분 그대로(`LockMps` 10, `GateDeg` 5, `FadeSeconds` 2, `MinThrottle` 0.05, `GrowMps` 0.05, `NoThrustKn` 0.01).

### 이벤트와 진행

- `node_executed`: `index`, `node_ut`, `node_dv_mps`, `burn_time_s`, `burn_start_ut`, `ut`, `stages`, `remaining_dv_mps`, `remaining_prograde_mps`·`remaining_normal_mps`·`remaining_radial_mps`, `planned`(`apoapsis_m`, `periapsis_m`, `inclination_deg`, 단계 시작 때의 노드 뒤 궤도), `actual`(`apoapsis_m`(열린 궤도 `null`), `periapsis_m`, `eccentricity`, `inclination_deg`).
- `hold`: 실행 중 `type` `execute_node`와 `target`(`pitch_deg`·`heading_deg`, `roll_deg` `null`), 완료 뒤 `attitude`·`prograde`(spec C8).

### 단계와 코어

- `Begin`은 검사(조종 가능, 시작 실패)를 먼저 하고, 통과하면 SAS를 끄고 hold를 걸고 `throttle_cut` 종료 처리를 등록한다. 검사에서 실패하면 예외로 끝나 아무것도 바꾸지 않는다(spec C5).
- `Judge`는 매 프레임 노드가 맨 앞에 그대로 있고 노드 뒤 궤도가 있는지 보고, 값을 읽어 규칙을 적용하고 스로틀을 쓰고 자동 스테이징을 시도한다. 완료 프레임에 스로틀 0, 노드 지우기, hold를 `prograde`·`attitude`로 바꾸고 이벤트를 남긴다.
- `End`는 노드를 바꾸지 않는다(실패·중단·다시 읽기에서 노드가 남음, spec C5·C9).

### `warp`의 노드 목표

- `NodeStartReading`(maneuver 모듈): 지금 UT, 노드 사용 가능의 이유, 착지·착수·발사 전, 노드 있음, 맨 앞 노드 UT, 노드 뒤 궤도 있음, 예상 연소 시간(남은 Δv 기준).
- `NodeStart.Judge`(maneuver 모듈): spec C5·C6에 같은 시작 실패(노드 사용 가능 아님, 노드 없음, 착지·착수·발사 전, 지금 UT ≥ 맨 앞 노드 UT, 노드 뒤 궤도 없음, 예상 연소 시간 없음)의 이유, 아니면 연소 시작 시각 = 노드 UT − 예상 연소 시간 ÷ 2.
- `WarpTarget`(warp 모듈): 실패 이유면 단계 실패, 아니면 목표 UT = 연소 시작 시각 − `lead_s`(spec C6).

## 오류·위험

1. 법선·방사 성분의 부호가 spec 용어의 방향과 다를 수 있다(미확인). 알아채는 방법: C9 성분 지정의 부호 확인(아래). 다르면 `NodeFrame`의 부호 하나를 바꾸는 plan 수정이다.
2. 해석해로 낸 Δv가 게임의 노드 뒤 궤도와 1 m를 넘게 어긋날 수 있다(미확인). Ap·Pe 목표는 할선 보정으로 맞추고, `circular`는 어긋나면 `bad_request`로 막는다. 알아채는 방법은 C9 노드 조작·호만 전이의 1 m 판정과 `create_node`의 `bad_request`.
3. SDK가 `JsonElement?` 인자와 `RequestContext`를 함께 받는지, 도구 스키마가 Claude Code에 나오는지(미확인). 알아채는 방법: C1(도구 목록)과 C9 오류의 모르는 인자.
4. 노드 시각이 지난 노드를 게임이 지울 수 있다(디컴파일로는 지우지 않을 것으로 봄, 미확인). 알아채는 방법: C9 오류의 지난 노드 항목에서 `get_nodes`.
5. 노드 뒤 궤도가 `AddManeuverNode`·`OnGizmoUpdated` 호출 안에서 바로 계산되지 않을 수 있다(미확인). 알아채는 방법: `create_node` 응답의 `after`가 `null`이 됨(C9 노드 조작).
6. `Kerbal X`의 높은 추력으로 짧은 연소(50 m/s)의 마지막 프레임 오차가 클 수 있다. 알아채는 방법: `node_executed`의 남은 Δv(1 m/s 판정).
7. 대기 안의 노드 실행은 게임에서 확인하지 않는다(spec 범위 밖).

## 검증 전략

빌드·단위 시험(`dotnet build KspBridge.slnx`, `dotnet test KspBridge.slnx`)을 먼저 하고, 게시(`dotnet publish`) 뒤 실행 중인 게임과 서버에 핫리로드로 반영해 실제 MCP 호출로 확인한다.

- C1: `get_status`의 서버·Mod 모듈에 `maneuver`, Claude Code의 도구 목록에 세 도구, `list_step_types`의 `execute_node` 설명. 실제 MCP 호출.
- C2: 단위 시험 `NodeArgsTests`(시각 셋 가운데 하나, 성분과 `goal`, 모두 0, `goal` 키 하나, 참만, 모르는 키, 종류), `NodeTimeTests`(착지·착수·발사 전의 `bad_request`, `at`의 t₀, 같은 시각이면 한 주기 뒤, 지금·마지막 노드 제약, `ESCAPE`·`ENCOUNTER` 끝 전 제약, `FINAL`은 한 주기 넘어도 됨, 열린 궤도 Ap·지난 Pe), `NodeGoalTests`(Ap·Pe 목표 1 mm, 원궤도, 낼 수 없는 목표, 고도·영향권 범위), `NodeFrameTests`(게임 식의 축, 성분 ↔ 벡터 왕복). 게임에서 C9 노드 조작·호만 전이·오류.
- C3: 단위 시험 `NodeInfoTests`(항목 필드, 노드 뒤 궤도 없음, 맨 앞 노드의 남은 Δv, 열린 궤도 `null`), `ConicTimesTests`(닫힌·열린 궤도의 다음 Ap·Pe, 구간 끝과 상관없음). `NodeAvailabilityTests`(장면, 기체, 시설 등급 없음, 추적소·관제 센터 미해금 이유, 궤도 계산기 없음). 게임에서 C9 노드 조작.
- C4: `NodeArgsTests`(`index`·`all`), 게임에서 C9 노드 조작·오류.
- C5: 단위 시험 `NodeStartTests`(시작 실패 각각, 연소 시작 시각), `ExecuteNodeLogicTests`(`NodeStart.Judge` 결과를 씀, 지났으면 바로 연소, 연소 판정을 `BurnControl`에 넘김), `BurnControlTests`(방향 고정, 5° 게이트, 스로틀, 완료, 다시 늘어 완료, 엔진 소진), 기존 `CircularizeLogicTests`가 그대로 통과. 게임에서 C9 호만 전이·성분 지정.
- C6: 단위 시험 `WarpArgsTests`(`node`, `lead_s`), `WarpTargetTests`(연소 시작 시각 − `lead_s`, 실패 이유 전달). 게임에서 C9 호만 전이·오류.
- C7: 기존 control 테스트가 그대로 통과하고, C9 다시 읽기. 기준 spec 반영은 문서 대조.
- C8: 단위 시험은 진행 값 모양(`ExecuteNodeLogicTests`). 게임에서 실행 중 `get_plan`의 `step_progress`와 `hold`를 조회해 기록한다(판정에는 쓰지 않음, spec C9 머리말).
- C9: 아래 게임 시험(검증 경로: 실제 MCP 호출). 시작 전에 `get_status`로 다른 사람의 비행·계획이 없는지 보고, 발사대의 stock `Kerbal X`(승무원 탑승)를 이름 있는 저장(`debug_save` `before-37`)으로 남기며, 끝나면 되돌린다. 궤도는 모두 `debug_set_orbit`으로 Kerbin에 두고, `get_flight`의 `time_warp`가 레일 위 아님인 것을 본 뒤 도구를 부른다. `Kerbal X`는 궤도-진입의 다시 읽기 시험에서 `debug_set_orbit`으로 궤도에 둔 뒤 원궤도화 연소를 해낸 기체라 다시 쓴다.
  - 노드 조작(spec 그대로, Ap 300 km·Pe 100 km): 새로 확인하는 것은 노드 생성·조회·삭제와 `at`·`ut`·`periapsis_m`이 게임과 맞는지다. 비행 없이 도구 호출만 한다.
  - 호만 전이(spec 그대로, 100 km 원궤도): 새로 확인하는 것은 노드 두 개 생성, `warp` 노드 목표, 실행 정확도, 실행한 노드 삭제다. 준비 비용은 첫 노드까지 600초와 전이 궤도 반 바퀴(약 1,200초)를 포함한 가속 비행(게임 약 30분, 실제 수 분).
  - 성분 지정: 적도 원궤도(Ap·Pe 100 km, 경사각 0)에서 성분 하나씩(50 m/s) 노드를 만들고 `warp`(`to` node, `lead_s` 60) → `execute_node`로 실행한다(성분마다 `debug_set_orbit`으로 다시 둠). 새로 확인하는 것은 세 방향 모두 실행 결과가 노드 뒤 궤도와 맞는지와 부호다. 부호 확인(spec C9 "plan에서 정한다"): 방사(+)는 실행 전 `get_nodes`의 노드 뒤 궤도에서 `next_apoapsis_ut` < `next_periapsis_ut`(밖으로 밀면 Ap로 올라감), 법선(+)은 계획 끝에 `wait` 300초를 더한 뒤 `get_flight`의 위도가 0보다 큼(적도 동향 궤도에서 r × v는 북쪽이고, 위도는 반 주기(약 980초) 동안 같은 부호). 준비 비용은 짧은 비행 셋.
  - 다시 읽기: 100 km 원궤도에서 `create_node`(`in_s` 300, `goal.apoapsis_m` 2000000, 약 570 m/s라 `Kerbal X`로도 연소가 수십 초) 뒤 계획 `warp`(`to` node, `lead_s` 60) → `execute_node`. `step_progress`로 다시 읽을 시점을 고르고 spec대로 본다.
  - 오류: spec 그대로.
  - 빼자고 제안하는 것: 없음(spec 확인 때 정한 항목만 한다).

## 원칙 점검

- 적용 규칙: 이 작업은 사용자 지시(2026-11-23)로 바뀌기 전의 스펙 주도 작업 규칙(spec·plan·tasks와 그 검토)대로 끝까지 한다. 그 밖에 `workflow/repository-content.md`(개인 경로·정보 없음), `git/github-issue.md`·`git/github-access.md`(이슈 #37은 배정·라벨이 되어 있음), `dotnet/nuget-package-selection.md`(새 패키지 없음), `dotnet/project-creation.md`. 저장소 안의 규칙 파일(CLAUDE.md·AGENTS.md)은 없다.
- 충돌: `dotnet/project-creation.md`의 net10.0과 다르게 새 Mod 모듈·시험 프로젝트는 net472이다. KSP 1.12.5(Unity 2019.4 Mono)가 net472 어셈블리를 요구하고 기존 Mod 모듈과 그 시험 프로젝트가 모두 net472이기 때문이다. 서버 모듈은 기존과 같이 net10.0이다.
