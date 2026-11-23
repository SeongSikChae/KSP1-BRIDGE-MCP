# 상승과 원궤도화 단계 계획

## 요약

새 Mod 모듈과 MCP 서버 모듈 쌍 `ascent`가 단계 종류 `ascend`와 `circularize`를 더한다. 코어는 고치지 않는다. 매 물리 프레임(코어의 `Judge`)에 게임에서 값을 읽어(`AscentReader`) 게임에 닿지 않는 규칙으로 목표 방향·스로틀·스테이징·완료·실패를 정하고, 결과를 자세 유지와 스로틀에 적용한다. 규칙(피치, 방위, 받음각 한계, 스로틀, Ap 상태, 조종 상실, 스테이징 판정, 연소 시작 시각, 원궤도화 판정)은 모두 게임 없이 단위 테스트한다.

자세 유지·스로틀 쓰기·스테이지 작동은 이미 control 모듈에 있다. 이번에 그 코드를 공개로 나누어 `ascent` 모듈이 함께 쓴다(control 모듈의 요구는 C11의 문구만 바뀐다).

이 문서에서 "디컴파일"은 spec.md 용어 절과 같다.

## 바꿀 파일·구조

새 파일(Mod 모듈):

- src/KspBridge.Mod.Modules.Ascent/KspBridge.Mod.Modules.Ascent.csproj: `src/KspBridge.Mod.Modules.Warp/KspBridge.Mod.Modules.Warp.csproj`와 같은 구성에 control·flight 모듈 참조와 테스트용 `InternalsVisibleTo`.
- src/KspBridge.Mod.Modules.Ascent/AscentModule.cs: 모듈 등록(단계 `ascend`·`circularize`).
- src/KspBridge.Mod.Modules.Ascent/AscentArgs.cs: `ascend`·`circularize` 인자 검증(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Ascent/AscentReading.cs: 한 프레임의 값(고도·Ap·속도·대기·엔진 등)을 담는 값 형식. 규칙의 입력(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Ascent/TurnProfile.cs: 피치와 처짐 방지, 선회 기본값과 시작 조건(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Ascent/LaunchAzimuth.cs: 방위와 경사각 허용 범위(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Ascent/AoaLimit.cs: 받음각 한계, 방향 벡터와 피치·방위 변환(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Ascent/AscendLogic.cs: `ascend`의 진행(닿음·다시 채움, 국면, 스로틀, 완료, 실패, 기록). `AscentReading`만 쓴다(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Ascent/ControlLossRule.cs: 조종 상실 판정(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Ascent/BurnEstimate.cs: 연소 시간 추정(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Ascent/CircularizeLogic.cs: `circularize`의 진행(기다림·연소, 방향, 스로틀, 완료, 실패). 게임에 닿지 않음.
- src/KspBridge.Mod.Modules.Ascent/StageRule.cs: 엔진 목록과 다음 스테이지로 자동 스테이징과 엔진 소진을 판정(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Ascent/AutoStager.cs: 쿨다운, `StageRule`의 결과를 control 모듈의 스테이지 작동으로 실행, 이벤트와 횟수.
- src/KspBridge.Mod.Modules.Ascent/AscentReader.cs: 활성 기체에서 `AscentReading`과 엔진 목록을 읽는다(게임).
- src/KspBridge.Mod.Modules.Ascent/AscendStep.cs: 단계 `ascend`(설명, 코어와 규칙의 연결, 이벤트, 진행).
- src/KspBridge.Mod.Modules.Ascent/CircularizeStep.cs: 단계 `circularize`.

새 파일(서버 모듈, 테스트, 기준 spec):

- src/KspBridge.Server.Modules.Ascent/KspBridge.Server.Modules.Ascent.csproj: `src/KspBridge.Server.Modules.Warp/KspBridge.Server.Modules.Warp.csproj`와 같은 구성.
- src/KspBridge.Server.Modules.Ascent/AscentModule.cs: `[assembly: KspBridgeModule("ascent")]`만 둔다. MCP 도구는 없다.
- tests/KspBridge.Mod.Modules.Ascent.Tests/KspBridge.Mod.Modules.Ascent.Tests.csproj: `tests/KspBridge.Mod.Modules.Warp.Tests/KspBridge.Mod.Modules.Warp.Tests.csproj`와 같은 구성에 ascent·control 모듈 참조.
- tests/KspBridge.Mod.Modules.Ascent.Tests/AscentArgsTests.cs, TurnProfileTests.cs, LaunchAzimuthTests.cs, AoaLimitTests.cs, AscendLogicTests.cs, ControlLossRuleTests.cs, BurnEstimateTests.cs, CircularizeLogicTests.cs, StageRuleTests.cs: 단위 테스트.
- docs/features/flight-ascent.md: 새 기능의 기준 spec(마지막 구현 단계에서 만든다).

고치는 기존 파일:

- src/KspBridge.Mod.Modules.Control/AttitudeHold.cs: `AttitudeHold`를 공개로 하고, 목표(`AttitudeTarget`)와 표시 이름(`type`)을 바꿀 수 있게 한다(바꿔도 hold는 그대로라 `hold_released`가 나지 않음). 오차 계산은 공개로 쓴다.
- src/KspBridge.Mod.Modules.Control/AttitudeTarget.cs, AttitudeLaw.cs, VesselReader.cs: 형식을 공개로 한다(동작 변화 없음).
- src/KspBridge.Mod.Modules.Control/AttitudeSteps.cs: `ControlStep`을 ControlStep.cs로 옮기고, `attitude` 설명의 hold가 끝나는 조건에 `ascend`·`circularize`를 더한다.
- src/KspBridge.Mod.Modules.Control/ControlStep.cs(새 파일): `RequireVessel`·`RequireControllable`을 공개로 둔다.
- src/KspBridge.Mod.Modules.Control/ThrottleStep.cs: 스로틀 쓰기와 계획 종료 때 내리기를 `ThrottleControl`로 옮기고, 단계 설명에 `ascend`·`circularize`의 스로틀을 더한다.
- src/KspBridge.Mod.Modules.Control/ThrottleControl.cs(새 파일): `Write(vessel, value)`와 `CutAtEnd(plan, vessel, events)`(같은 `throttle_cut` 키로 등록).
- src/KspBridge.Mod.Modules.Control/StageStep.cs: 스테이지 작동을 `StageActivator`로 옮긴다.
- src/KspBridge.Mod.Modules.Control/StageActivator.cs(새 파일): 다음 스테이지 판정과 작동, 거절·작동한 부품 수 결과를 돌려준다. `stage` 단계의 이벤트와 동작은 그대로다.
- src/KspBridge.Server.Modules.Core/CoreTools.cs: `get_flight_events` 설명에 `ascend_completed`·`circularize_completed`와 `stage_activated`의 이유. flight-plan의 파일이지만 요구는 바뀌지 않는다.
- KspBridge.slnx: 새 프로젝트 셋을 더한다.
- ReadMe.md: 단계 `ascend`·`circularize`, 고체 부스터 안내, 예시 계획, 경사각은 두 단계에 같은 `inclination_deg`를 준다는 안내(D016). 다른 기준 spec의 `파일:` 줄에도 있지만 그 요구는 바뀌지 않는다.
- docs/features/flight-control.md: R2·R3·R6과 `파일:` 줄·이력(마지막 구현 단계).

배포 구성(`Directory.Build.targets`)은 모듈을 이름 패턴으로 모으므로 고치지 않는다. 새 패키지는 없다.

## 결정

- 코어를 고치지 않는다: 매 프레임 판정, 이벤트, 계획이 끝날 때의 처리는 이미 단계와 `IPlanScope`에 있다 / 검토한 대안: 상승 전용 계획 감시를 코어에 추가 — 단계 안에서 끝나므로 쓰지 않음
- 게임에 닿는 부분과 닿지 않는 부분을 나눈다: 규칙은 `AscentReading`만 받아 게임 없이 시험하고, `AscentReader`·단계만 게임에 닿는다 / 검토한 대안: 단계에 모두 둠 — 스테이징 순서, Ap 상태, 조종 상실, 원궤도화 판정을 게임 없이 시험할 수 없음
- control 모듈의 자세 유지·스로틀·스테이지 작동을 공개로 나눠 함께 쓴다: 같은 동작을 두 벌 갖지 않고, `throttle_cut`의 키가 같아 한 계획에서 한 번만 실행된다 / 검토한 대안: ascent 모듈에 복사 — 두 벌이 어긋날 수 있음. `InternalsVisibleTo` — 게시할 때 어셈블리 이름이 `<이름>.g<표식>`으로 바뀌어 이름으로 묶는 노출이 안 됨(`Directory.Build.targets`, csproj의 `AssemblyName`)
- 자세 목표는 프레임마다 피치·방위(지표 기준) 값으로 바꾸고 hold는 하나를 그대로 쓴다: 기준 프레임이 바뀌어도 안전하고 바꿀 때 `hold_released`가 나지 않는다(C5·C9) / 검토한 대안: 프레임마다 새 hold — 이벤트가 계속 남음. 방향 벡터를 hold에 둠 — 세계 좌표의 회전 프레임 전환에 걸림
- 방위 계산: spec C3의 식. 목표 Ap의 원궤도 속력과 경사각에서 정해지는 수평 속도 벡터에서 지금의 수평 궤도 속도를 뺀 방향, 5%보다 작으면 경사각 방위 / 검토한 대안: 경사각 방위를 고정 — 자전이 준 속도와 상승 중 어긋남이 남아 경사각이 벗어남 (D007)
- 경사각은 `circularize`의 연소 방향에서도 맞춘다: `ascend`는 원궤도 속력에 닿기 전에 끝나므로, `circularize`가 같은 경사각의 방위 β 방향 원궤도 속도로 가는 Δv로 연소해 면 보정을 원궤도화에 합친다. β는 `LaunchAzimuth`가 두 단계에 같은 함수로 낸다 / 검토한 대안: decisions.md D016의 선택지 (D016)
- 해가 없는 위도의 β는 sin β를 −1~1로 자른 값이다: 1이면 β = 90°(정동), −1이면 β = −90°(정서). `north`·`south` 어느 해를 골라도 같은 방위가 되므로 대체 규칙(spec C3)과 같다 / 검토한 대안: decisions.md D018의 선택지 (D018)
- 조종 상실은 대기 안에서 동압 ≥ 1 kPa 또는 엔진 추력 합 ≥ 0.01 kN일 때 센다. 추력 기준은 `AscendLogic` 완료·`CircularizeLogic` 다시 늘어 완료와 같은 값이다 / 검토한 대안: decisions.md D017의 선택지 (D017)
- 선회는 고도 비율과 Ap 비율 가운데 작은 값으로 정한다 / 검토한 대안: decisions.md D002의 선택지 (D002)
- 완료는 Ap 유지 폭(99%)과 대기 상한 위를 함께 본다: 완료 전에 Ap가 99%보다 낮아지면 대기 안팎을 가리지 않고 다시 채우고, 대기 상한 위에서 끝난다 / 검토한 대안: decisions.md D003의 선택지 (D003)
- 스테이징 판정은 순수 규칙(`StageRule`)이 엔진 목록만 보고 한다: 엔진마다 켜짐·꺼짐·점화됨·스테이지·분리 스테이지·추진제를 게임에서 읽어 넘긴다 / 검토한 대안: 단계에서 직접 — 병렬 부스터의 순서를 게임 없이 시험할 수 없음 (D001, D008)
- 쓸 엔진이 아닌 조건은 spec 용어대로: `Part.separationIndex`(디컴파일 `StageManager.setSeparationIndices` 3914행)와 다음 스테이지 번호, `Part.inverseStage`, `Part.stagingOn`, `Part.GetConnectedResourceTotals`(게임은 같은 함수로 엔진의 추진제 공급량을 센다, 디컴파일 `Propellant` 102·218행)로 계산한다. 점화되지 않은 엔진은 자기 스테이지가 다음 스테이지 이하여야 하고(이미 작동한 스테이지에서 점화되지 않은 엔진(사람이 끈 엔진 등)은 스테이징으로 점화되지 않음), 분리 스테이지가 자기 스테이지보다 작아야 한다 (D009, D014)
- 원궤도화 연소 시간은 게임의 Δv 계산기 스테이지 표(`VesselDeltaV.OperatingStageInfo`의 `startMass`·`endMass`·`deltaVinVac`·`stageBurnTime`)로 높은 스테이지부터 로켓 식으로 추정하고, 계산기가 아직 준비되지 않았거나 표가 비면 지금 켜진 엔진의 진공 최대 추력 ÷ 질량으로 추정한다: 시작 시각을 정하는 데만 쓰므로 정확도가 낮아도 연소가 Ap를 조금 벗어날 뿐이다 / 검토한 대안: 늘 지금 엔진만 — 켜진 엔진이 없는 채 상단을 기다리면 추정할 수 없음
- 연소 방향은 매 프레임 지금 위치의 원궤도 속도로 가는 Δv의 방향이며 10 m/s 아래에서는 그때의 피치·방위로 고정한다: 남은 Δv가 작을 때 방향이 흔들리지 않는다 / 검토한 대안: 처음부터 고정 — 중력으로 방향이 변해 오차가 남음
- 서버 모듈은 도구 없이 이름만 둔다(control·warp·condition 모듈과 같은 방식) / 검토한 대안: 서버 모듈을 두지 않음 — C1의 모듈 쌍과 어긋남

## 인터페이스·데이터

### 게임 동작의 출처

- 분리 스테이지: `Part.separationIndex`는 `StageManager.setSeparationIndices`(3914행)가 부품마다 정하고, 스테이지 번호 정렬(3112행)과 활성 기체 다시 계산(`RecalculateVesselStaging` 3846행)에서 갱신된다. craft 파일의 `sepI`가 저장된 값이며, stock `Kerbal X`에서 부스터 엔진은 6·5·4, Mainsail은 3, Poodle은 1이다(게임 설치 폴더의 `Ships/VAB/Kerbal X.craft`). 발사한 기체에서 이 값이 그대로인지는 미확인이다(위험 1).
- 엔진: `ModuleEngines.isOperational`(372행), `flameout`(706행에서 참, 744행에서 거짓이 됨), `EngineIgnited`(296행), 진공 최대 추력 `MaxThrustOutputVac(useThrustLimiter: true)`(4551행). 추진제는 `ModuleEngines.propellants`의 `Propellant.id`·`GetFlowMode()`와 `Part.GetConnectedResourceTotals`(17588행)로 본다.
- 스테이지 작동과 쿨다운: `StageManager.ActivateStage`(3226행)는 게임이 거절하면 아무 일도 하지 않으므로, 기존 `stage` 단계와 같이 `onStageActivate` 이벤트로 받아들였는지 판정한다(`src/KspBridge.Mod.Modules.Control/StageStep.cs`).
- 동압: `Vessel.dynamicPressurekPa`(디컴파일 `FlightIntegrator` 1994행). 대기 상한: `CelestialBody.atmosphereDepth`.
- Ap: `Orbit.ApA`(해발, 닫힌 궤도), `Orbit.timeToAp`(디컴파일 `Orbit` 722~734행: 닫힌 궤도는 다음 Ap까지의 0 이상의 시간). 이심률이 1 이상이면 목표 Ap에 닿은 것으로 본다(Ap가 없으므로).
- 속도: `Vessel.obt_velocity`·`srf_velocity`·`up`·`north`·`east`(이미 `VesselReader`가 쓰는 값).
- 스로틀: `FlightInputHandler.state.mainThrottle`과 `Vessel.ctrlState.mainThrottle`을 함께 쓴다(기존 `throttle` 단계와 같다).

### 인자(`AscentArgs`)

`ascend`·`circularize`의 허용 키는 spec C2의 표와 같다. 모르는 키·범위 밖·`heading`과 `inclination_deg`를 함께 줌·`inclination_deg` 없는 `launch`는 `bad_request`이다. 수는 유한해야 한다.

### 선회(`TurnProfile`)

- 입력: 고도, Ap(이심률 1 이상이면 무한대로 본다), 단계를 시작한 고도, 대기 상한(없으면 없음), 목표 Ap, `turn_start_m`, `turn_end_m`(없으면 기본값), 남은 시간 `timeToAp`, 수직 속도.
- 선회 시작 고도 S = 시작 고도 + `turn_start_m`. 마칠 고도 E는 인자, 없으면 대기가 있으면 대기 상한 × 0.65, 없으면 S + (목표 Ap − S) × 0.1.
- f = min(고도 비율, Ap 비율)을 0~1로 자름(대기가 없으면 고도 비율만). 피치 = 90 − 90 × f^0.6.
- 처짐 방지: 닿기 전이고 Ap가 대기 상한(대기가 없으면 E)을 넘었으면, 내려가는 중이거나 `timeToAp` < 30이면 20° × clamp((30 − timeToAp) ÷ 30, 0, 1)을 더한다(내려가는 중이면 20°).
- 시작 실패: 목표 Ap가 대기 상한 이하(대기가 있을 때)이거나 영향권 밖, E ≤ S.

### 방위(`LaunchAzimuth`)

- 입력: 위도, 수평 궤도 속도 성분(북·동), 목표 Ap의 원궤도 속력, 경사각, 방향(`north`·`south`), `heading`(경사각이 없으면).
- 경사각 방위 β = asin(clamp(cos 경사각 ÷ cos 위도, −1, 1))과 180° − β 가운데 방향에 맞는 해(북쪽 성분 cos β가 0 이상이면 `north`). 해가 없으면 clamp로 정동(90°)이나 정서(270°)가 된다(대체 규칙, D018). 목표 속도(북 = v cos β, 동 = v sin β)에서 지금 속도를 뺀 벡터가 원궤도 속력의 5%보다 작으면 β, 아니면 그 벡터의 방위.
- `circularize`용 β(D016): 두 해 가운데 지금 수평 궤도 속도(북·동 성분)와 이루는 각이 작은 해. 같으면 `north`.
- 시작 실패: 경사각이 |위도| − 0.5°보다 작거나 180° − |위도| + 0.5°보다 큼(`ascend`와 `inclination_deg`를 준 `circularize`).

### 받음각 한계(`AoaLimit`)

- 목표 벡터를 지표 속도 방향에서 `max_aoa_deg` × max(1, 10 ÷ 동압) 안으로 구면 보간해 당긴다. 동압이 1 kPa 이하이거나 지표 속도가 1 m/s 미만이면 그대로다. 결과 벡터는 피치·방위로 바꿔 hold에 넣는다.

### `AscendLogic`

- `AscentState`(닿음 여부, 다시 채우는 중)를 프레임마다 갱신한다. Ap ≥ 목표이면 닿음·다시 채우기 끝, 닿은 뒤 Ap < 99%이면 다시 채우기. 추력을 원함 = 닿기 전 또는 다시 채우는 중.
- 스로틀: 추력을 원하면 Ap 식(남은 Ap ÷ (목표의 5%)를 0.05~1로 자름)과 동압 식(80%부터 100%까지 1에서 0으로)의 작은 값, 아니면 0. 동압 제한 전 Ap 식이 "추력을 원함"의 명령 값이다.
- 목표 방향: 대기 안에서 기다리는 동안(추력을 원하지 않고 대기 안)은 지표 속도 방향, 아니면 선회·방위·처짐 방지를 합친 벡터에 받음각 한계.
- 완료(spec C5): 닿은 적이 있고, Ap ≥ 99%이고, 대기 상한 위(또는 대기 없음)이고, 엔진 추력 합 < 0.01 kN.
- 실패: 조종 상실(`ControlLossRule`: 대기 안, 동압 ≥ 1 kPa 또는 엔진 추력 합 ≥ 0.01 kN, 기수 오차 > 30°가 3초. 조건이 한 프레임이라도 깨지면 다시 센다, D017), 엔진 소진(`StageRule`), 조종 불가(단계에서).
- 기록: 동압 최대, 동압 ≥ 1 kPa에서 받음각 최대.

### `StageRule`

- 입력: 추력을 원하는지, `auto_stage`, 다음 스테이지 번호(없으면 없음), 엔진 목록(켜짐, 꺼짐, 점화됨, 스테이지 번호, 분리 스테이지, 추진제를 받을 수 있음, 스테이징 사용 여부).
- 다음 스테이지 n의 버려질 엔진 = 분리 스테이지 ≥ n인 엔진. 쓸 엔진 = 추진제가 있고 꺼지지 않았으며 분리 스테이지 < n이고, 켜져 있거나(점화됐고 꺼지지 않음) 점화되지 않았고 스테이징이 켜져 있으며 자기 스테이지 ≤ n이고 분리 스테이지 < 자기 스테이지.
- 결과: `Boosters`(버려질 엔진이 있고 그 가운데 켜진 것과 점화되지 않은 것이 없으며, 버려지지 않는 켜진 엔진이 하나 이상 있음), `NoRunningEngine`(켜진 엔진이 없음). 두 조건은 겹치지 않는다(D015). 쓸 엔진이 없거나 다음 스테이지가 없거나 `auto_stage`가 거짓이면 `None`. 켜진 엔진이 없고 추력을 원하며 위 이유로 스테이징할 수 없으면 엔진 소진(실패).
- `AutoStager`: 마지막 작동에서 게임 시간 1초가 지났을 때만, `StageRule`이 낸 이유가 있으면 `StageActivator`를 부른다. 거절이면 다음 프레임에 다시, 작동한 부품이 없으면 실패. 성공하면 `stage_activated` 이벤트에 `reason`(`boosters_out`·`no_running_engine`)을 더하고 횟수를 센다.

### `CircularizeLogic`

- 시작 실패: spec C8의 조건과, `inclination_deg`를 주었으면 `LaunchAzimuth`의 시작 실패(spec C7).
- 원궤도 속도의 방향: `inclination_deg`가 없으면 지금 수평 궤도 속도의 방향, 있으면 프레임마다 지금 위도의 `circularize`용 β 방향(D016·D018). 크기는 지금 위치의 원궤도 속력.
- 기다림: 시작 시각 = 다음 Ap 시각 − 연소 시간 ÷ 2. 지났거나 내려가는 중이면 바로 연소 국면.
- 연소 국면: 남은 Δv = 위의 원궤도 속도 − 지금 속도의 벡터(면을 맞추는 몫 포함). 크기 ≤ `tolerance_mps`이면 완료. 10 m/s 아래에서 방향(피치·방위) 고정. 다시 늘어 완료(spec C9). 이 항목에서 추력을 내는 프레임은 엔진 추력 합이 0.01 kN 이상인 프레임이다(명령 스로틀이 아님, `AscendLogic` 완료의 기준과 같다). 최소값은 추력을 내는 프레임에 남은 Δv가 10 m/s 아래일 때만 갱신하고, 추력이 없는 프레임이 있거나 10 m/s 위로 나가면 버린다. 추력을 내는 프레임의 남은 Δv가 10 m/s 아래이면서 그 최소보다 0.05 m/s 넘게 크면 완료한다(단을 바꾸는 동안 늘어난 것과 재점화 뒤 다시 줄어드는 동안은 완료가 아니다). 스로틀 = 기수 오차 ≤ 5°이면 남은 Δv ÷ (지금 최대 가속도 × 2초)를 0.05~1로 자름(켜진 엔진이 없으면 1), 아니면 0.
- 목표 방향: 기다리는 동안도 같은 Δv의 방향.

### 이벤트와 진행

- `ascend_completed`: `index`, `apoapsis_m`, `periapsis_m`, `inclination_deg`, `altitude_m`, `max_q_kpa`, `max_aoa_deg`(없으면 `null`), `stages`.
- `circularize_completed`: `index`, `apoapsis_m`, `periapsis_m`, `eccentricity`, `inclination_deg`, `remaining_dv_mps`, `burn_start_ut`, `ut`, `stages`.
- `step_progress`는 spec C10의 항목. `hold`는 spec C10(D013).

### 단계와 코어

- `Begin`은 검사(조종 가능, 시작 조건)를 먼저 하고, 통과하면 SAS를 끄고 hold를 걸고 `throttle_cut` 종료 처리를 등록한 뒤 첫 프레임부터 규칙을 적용한다. 검사에서 실패하면 예외로 끝나 아무것도 바꾸지 않는다(C7·C8).
- `Judge`는 매 프레임 값을 읽고 규칙을 적용해 스로틀을 쓰고 자동 스테이징을 시도한다. `Control`은 하지 않는다(hold가 조종 입력을 넣는다).
- 완료할 때 hold의 목표를 `prograde`로, 표시 이름을 `attitude`로 바꾼다(D013).

## 오류·위험

1. 발사한 기체의 `Part.separationIndex`가 craft 파일의 값과 다를 수 있다(미확인). 다르면 병렬 부스터가 제때 떨어지지 않거나 쓸 엔진 판정이 어긋난다. 알아채는 방법: C12 궤도 진입의 `stage_activated` 순서와 번호, 이벤트의 `reason`.
2. 엔진이 추진제를 받을 수 있는지의 판정(`GetConnectedResourceTotals`)이 분리 전 흐름에서 세어지므로 분리 뒤 흐름과 다를 수 있다(미확인). 알아채는 방법: C12 궤도 진입에서 상단이 점화되는지(끝난 뒤 스테이지 2 작동 여부와 완료).
3. 동압 20 kPa 제한으로 `Kerbal X`가 중력 손실을 크게 봐 Δv가 모자랄 수 있다(미확인). 알아채는 방법: C12 궤도 진입이 엔진 소진으로 `step_failed`가 되는 것. 어긋나면 기본값·선회 값을 plan 의도 변경으로 고친다.
4. 동압 스로틀이 흔들리거나 스로틀이 0이 되는 동안 짐벌·롤 제어를 잃을 수 있다(미확인). 알아채는 방법: 비행 중 조회의 자세 오차와 `max_aoa_deg`(판정에는 쓰지 않음).
5. 조종 상실 시험 기체가 실제로 뒤집히지 않을 수 있다. 알아채는 방법: 시험이 `step_failed`(조종 상실)로 끝나지 않음. 앞날개를 무게중심 앞에 단 기체(검증 전략)로 만들며, 뒤집히지 않으면 날개를 늘린다. 1차 게임 검증에서 이 기체는 이륙 1.5초에 뒤집혀 구르며 동압이 1 kPa 아래로 떨어졌다(D017로 고침).
9. 추력 중 조종 상실(D017)이 대기 상층에서 잘못 판정될 수 있다: 관성 비행(지표 속도 방향)에서 Ap 다시 채우기로 바뀔 때 목표 방향이 크게 바뀌고, 큰 기체가 3초 안에 30° 안으로 돌지 못하면 실패한다(1차 검증 `Kerbal X`의 바뀜은 약 26°). 알아채는 방법: C12 궤도 진입·경사각이 조종 상실로 `step_failed`가 되는 것.
6. `VesselDeltaV`가 아직 준비되지 않았거나 표가 비면 연소 시간 추정이 대체 방식으로 바뀐다. 알아채는 방법: `step_progress`의 예상 연소 시간과 `burn_start_ut`.
7. 대기가 없는 천체의 선회·완료는 게임에서 확인하지 않는다(spec 범위 밖). 단위 테스트만 한다.
8. 자동 스테이징이 1초 안에 두 번 일어나지 않는지는 게임 쿨다운과 함께 C12의 이벤트 시각으로 본다.

## 검증 전략

빌드·단위 시험(`dotnet build KspBridge.slnx`, `dotnet test KspBridge.slnx`)을 먼저 하고, 게시(`dotnet publish`) 뒤 실행 중인 게임과 서버에 핫리로드로 반영해 실제 MCP 호출로 확인한다.

- C1: `get_status`의 서버·Mod 모듈에 `ascent`, `list_step_types`의 두 단계 설명. 실제 MCP 호출.
- C2: 단위 시험 `AscentArgsTests`(빠짐·범위·함께 줄 수 없음·모르는 키), 게임에서 `bad_request` 몇 가지(C12 오류 항목).
- C3: 단위 시험 `TurnProfileTests`(고도·Ap 비율, 대기 없음, 처짐 방지, 시작 실패), `LaunchAzimuthTests`(경사각 0·45·90·135·180, 북·남, 자전을 뺀 방위, 5% 아래, 허용 폭, 대체 규칙(위도 −0.1°에서 경사각 0 → 정동, 179.95 → 정서), `circularize`용 β가 지금 수평 속도에 가까운 해), `AoaLimitTests`(동압에 따른 한계, 낮은 속도, 벡터 변환).
- C4·C5: 단위 시험 `AscendLogicTests`(스로틀의 Ap·동압 식, 닿음과 다시 채움의 순서(99% 안팎, 대기 안팎), 국면, 완료 조건, 추력이 남으면 완료되지 않음, 이심률 1 이상). 게임에서 C12 궤도 진입의 `ascend_completed`.
- C6: 단위 시험 `StageRuleTests`: `Kerbal X`의 엔진 목록(7단계 발사 점화, 부스터 쌍이 꺼짐 → `boosters_out`, 중심 엔진이 다 탐 → 스테이지 3(분리, `no_running_engine`) → 2(상단 점화, `no_running_engine`), 다음 스테이지 1에서는 작동하지 않음), 다음 스테이지 0(낙하산)에서 작동하지 않음, `auto_stage` 거짓, 엔진 소진, 버려질 엔진에 켜진 것이 있음, 점화되지 않은 엔진이 버려질 스테이지보다 먼저 떨어짐. 게임에서 C12 궤도 진입의 `stage_activated` 순서.
- C7: 단위 시험 `ControlLossRuleTests`·`AscendLogicTests`(시작 실패, 조종 상실 3초·30°·1 kPa, 동압 1 kPa 아래에서 추력 중이면 셈·추력 없으면 세지 않음, 엔진 소진). 게임에서 C12 조종 상실과 오류 항목.
- C8·C9: 단위 시험 `BurnEstimateTests`(단일·여러 단계, 계산기 없음), `CircularizeLogicTests`(시작 시각, 바로 연소, 방향 고정, 5° 문, 스로틀, 남은 Δv 이하 완료, 10 m/s 아래에서 다시 늘어 완료, 10 m/s 위 단 교체 중 늘어남은 완료 아님, 10 m/s 아래 단 교체 뒤 늘었다가 다시 줄어드는 동안은 완료 아님, 시작 실패, `inclination_deg`의 연소 방향·남은 Δv에 면 몫이 들어감·낼 수 없는 경사각의 시작 실패, `AscentArgsTests`의 `circularize` `inclination_deg` 범위). 게임에서 C12 궤도 진입과 다시 읽기.
- C10: 단위 시험은 이벤트와 진행 값 모양(`AscendLogicTests`·`CircularizeLogicTests`의 출력 사전). 게임에서 실행 중 `get_plan`의 `step_progress`와 `hold`를 조회해 기록한다(판정에는 쓰지 않음, C12 머리말).
- C11: 기존 control 테스트가 그대로 통과하고(`dotnet test`), C12 다시 읽기. 기준 spec 반영은 문서 대조.
- C12: 아래 게임 시험(검증 경로: 실제 MCP 호출). 시작 전에 게임 상태를 이름 있는 저장(`debug_save` `before-36`)으로 남기고, 시험이 끝나면 되돌린다.
  - 궤도 진입: 발사대의 stock `Kerbal X`에 승무원을 태워 spec의 계획 하나. 새로 확인하는 것은 상승·병렬 스테이징·원궤도화가 이어져 동작하고 낙하산·사령선이 남는지다. 준비 비용은 비행 한 번(게임 5~8분, 대기 구간은 1배속)이다.
  - 경사각: 같은 기체로 spec의 계획(`ascend` → `warp` → `circularize`, 두 단계에 `inclination_deg` 45). 새로 확인하는 것은 원궤도화 연소가 면을 맞춰 실제로 45°를 내는지다(1차 검증의 `ascend`만으로는 42.8°). 준비 비용은 비행 한 번.
  - 조종 상실: 시험 기체는 1차 검증에서 에디터 도구로 만든 `Ascent Flip Test`(`probeCoreOcto.v2` 루트, `solidBooster.v2`, 부스터 위쪽 `deltaWing` 4장)를 `debug_launch`로 다시 쓴다. 무게중심 앞의 날개 때문에 공력으로 불안정해 동압이 오르면 뒤집힌다. 고체 부스터는 동압 제한을 받지 않아(spec C4) 최대 추력으로 오르며, 인자는 `apoapsis_m` 80000이고 나머지는 기본값을 쓴다. 새로 확인하는 것은 실제 게임에서 조종 상실이 판정되고 스로틀이 0이 되는지다. 준비 비용은 저장된 기체를 불러와 짧은 비행 한 번(수십 초).
  - 다시 읽기: spec의 궤도 시험. 준비 비용은 `debug_set_orbit`과 몇 분(가속으로 Ap까지).
  - 오류: spec의 `bad_request`·`step_failed`.
  - 빼자고 제안하는 것: 원궤도화만 따로 하는 시험과 다른 stock 기체 반복은 spec 확인 때 제안한 대로 하지 않는다.

## 원칙 점검

- 적용 규칙: `workflow/spec-driven.md`와 하위 문서, `workflow/repository-content.md`(개인 경로·정보 없음), `workflow/conversation-language.md`, `git/github-issue.md`·`git/github-access.md`(이슈 #36은 배정·라벨이 되어 있음), `dotnet/nuget-package-selection.md`(새 패키지 없음), `dotnet/project-creation.md`. 저장소 안의 규칙 파일(CLAUDE.md·AGENTS.md)은 없다.
- 충돌: `dotnet/project-creation.md`의 net10.0과 다르게 새 Mod 모듈·시험 프로젝트는 net472이다. KSP 1.12.5(Unity 2019.4 Mono)가 net472 어셈블리를 요구하고 기존 Mod 모듈과 그 시험 프로젝트가 모두 net472이기 때문이다. 서버 모듈은 기존과 같이 net10.0이다.
