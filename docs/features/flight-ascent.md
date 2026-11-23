# flight-ascent

파일: src/KspBridge.Mod.Modules.Ascent/AoaLimit.cs, src/KspBridge.Mod.Modules.Ascent/AscendLogic.cs, src/KspBridge.Mod.Modules.Ascent/AscendStep.cs, src/KspBridge.Mod.Modules.Ascent/AscentArgs.cs, src/KspBridge.Mod.Modules.Ascent/AscentModule.cs, src/KspBridge.Mod.Modules.Ascent/AscentReader.cs, src/KspBridge.Mod.Modules.Ascent/AscentReading.cs, src/KspBridge.Mod.Modules.Ascent/AutoStager.cs, src/KspBridge.Mod.Modules.Ascent/BurnEstimate.cs, src/KspBridge.Mod.Modules.Ascent/CircularizeLogic.cs, src/KspBridge.Mod.Modules.Ascent/CircularizeStep.cs, src/KspBridge.Mod.Modules.Ascent/ControlLossRule.cs, src/KspBridge.Mod.Modules.Ascent/KspBridge.Mod.Modules.Ascent.csproj, src/KspBridge.Mod.Modules.Ascent/LaunchAzimuth.cs, src/KspBridge.Mod.Modules.Ascent/StageRule.cs, src/KspBridge.Mod.Modules.Ascent/TurnProfile.cs, src/KspBridge.Server.Modules.Ascent/AscentModule.cs, src/KspBridge.Server.Modules.Ascent/KspBridge.Server.Modules.Ascent.csproj, tests/KspBridge.Mod.Modules.Ascent.Tests/AoaLimitTests.cs, tests/KspBridge.Mod.Modules.Ascent.Tests/AscendLogicTests.cs, tests/KspBridge.Mod.Modules.Ascent.Tests/AscentArgsTests.cs, tests/KspBridge.Mod.Modules.Ascent.Tests/BurnEstimateTests.cs, tests/KspBridge.Mod.Modules.Ascent.Tests/CircularizeLogicTests.cs, tests/KspBridge.Mod.Modules.Ascent.Tests/ControlLossRuleTests.cs, tests/KspBridge.Mod.Modules.Ascent.Tests/KspBridge.Mod.Modules.Ascent.Tests.csproj, tests/KspBridge.Mod.Modules.Ascent.Tests/LaunchAzimuthTests.cs, tests/KspBridge.Mod.Modules.Ascent.Tests/Readings.cs, tests/KspBridge.Mod.Modules.Ascent.Tests/StageRuleTests.cs, tests/KspBridge.Mod.Modules.Ascent.Tests/TurnProfileTests.cs, ReadMe.md

발사대에서 궤도까지 가는 단계 종류 `ascend`(상승)와 `circularize`(원궤도화), 그리고 두 단계가 함께 쓰는 자동 스테이징. 별도 MCP 도구는 없고 Planner가 `submit_plan`으로 `ascend` → `warp`(`to` apoapsis, `lead_s`) → `circularize` 순서로 쓴다. Ap가 아닌 곳의 원궤도화와 타원 궤도 목표, 승교점·발사 시각 맞추기, 귀환과 비상 탈출, 비행기형 이륙, 과열·진동 감시, 두 단계 밖의 자동 스테이징, 두 단계 안의 시간 가속, 고체 부스터 추력을 비행 중에 바꾸는 것, 엔진 없이 탱크만 떨어지는 스테이지를 미리 떼는 것, 발사 클램프만 푸는 스테이지는 하지 않는다. 대기가 없는 천체는 단위 시험으로만 확인했다. 자세 유지·스로틀·스테이지 작동은 docs/features/flight-control.md의 공개 부분을 쓴다. 괄호 안 D번호는 docs/specs/궤도-진입/decisions.md의 결정이다.

## 용어

이 문서에서 "디컴파일"은 게임 설치 폴더의 `KSP_x64_Data/Managed/Assembly-CSharp.dll`(KSP 1.12.5)을 `ilspycmd`로 풀어 읽은 것이고, 행 번호는 그 결과의 행이다.

- 활성 기체, 지표 기준 자세, 다음 스테이지: docs/features/flight-observation.md의 용어와 같다.
- 자세 유지, 조종 불가, 스로틀을 내리는 종료 사유: docs/features/flight-control.md의 용어와 같다.
- 대기 상한: 지금 천체의 대기 높이(해발, 게임의 `CelestialBody.atmosphereDepth`). 대기가 없으면 없다.
- 동압: 활성 기체의 `Vessel.dynamicPressurekPa`(kPa). 게임은 0.0005 × 밀도 × 속도²으로 구한다(디컴파일 `FlightIntegrator` 1994행).
- 받음각: 기수 방향과 지표 속도 방향 사이의 각(°). 지표 속도가 1 m/s 미만이면 없다.
- 켜진 엔진: 게임의 `ModuleEngines.isOperational`이 참인 엔진(점화됐고 꺼지지 않음. 디컴파일 `ModuleEngines` 372행). 꺼진 엔진은 `ModuleEngines.flameout`이 참인 엔진이다.
- 버려질 엔진: 다음 스테이지가 작동하면 활성 기체에서 떨어지는 엔진. 게임이 부품마다 정한 분리 스테이지 번호(`Part.separationIndex`. 디컴파일 `StageManager.setSeparationIndices` 3914행)가 다음 스테이지 번호 이상인 부품의 엔진이다.
- 추진제를 받을 수 있음: 그 엔진의 추진제마다, 게임이 그 추진제의 흐름 방식으로 엔진 부품에서 닿는 자원 양을 센 값(`Part.GetConnectedResourceTotals`. 게임은 엔진의 추진제 공급량을 이 함수로 센다, 디컴파일 `Propellant` 102·218행)이 0보다 큰 것. 게임 Δv 계산기가 무시하는 추진제(`DeltaVGlobals.PropellantsToIgnore`. 기본 `IntakeAir`·`ElectricCharge`이며 목록을 바꿔도 이 둘은 늘 들어간다, 디컴파일 `DeltaVGlobals` 128·148·149·171·184행)는 보지 않는다(D009·D014).
- 쓸 엔진: 다음 스테이지가 작동해도 떨어지지 않고 꺼진 엔진이 아니며 추진제를 받을 수 있는 엔진 가운데, 켜진 엔진이거나, 아직 점화되지 않았고 자기 스테이지까지 차례로 작동하는 동안 떨어지지 않는 엔진.
- 추력을 원함(D010):
  - `ascend`: 동압 제한(R4)을 적용하기 전의 스로틀 명령이 0보다 큰 상태.
  - `circularize`: 연소 국면(R8)이고 남은 Δv가 `tolerance_mps`보다 큰 상태. 기수가 맞지 않아 추력을 내지 않고 기다리는 동안도 포함한다. 연소 시작을 기다리는 국면은 해당하지 않는다.

## 요구(완료 조건)

- R1. `ascend`와 `circularize`가 Mod 모듈과 MCP 서버 모듈 쌍(`ascent`)으로 더해지고 핫리로드로 반영된다. 두 단계 종류가 `list_step_types`에 나오고, 설명에 인자(이름·범위·기본값), 자동 스테이징의 규칙, 완료·실패 조건, `warp`와 함께 쓰는 순서가 들어간다. `ascend` 설명에는 고체 부스터의 추력은 스로틀로 줄일 수 없으니 조립할 때 `editor_set_part_settings`의 추력 제한으로 맞추라는 안내(D005)가 들어간다. 두 단계의 설명에는 경사각을 맞추려면 같은 `inclination_deg`를 두 단계에 모두 주라는 안내(`ascend`만으로는 다 맞지 않음, D016)와 `abort_on.no_thrust`와 함께 쓰지 말라는 안내가 들어간다(계획 감시가 단계보다 먼저 판정되어 단이 다 탄 프레임에 계획이 끝난다. docs/features/flight-plan.md R11).
- R2. 인자는 다음과 같다. 빠진 필수 인자, 종류·범위가 맞지 않는 값, 함께 줄 수 없는 인자, 모르는 인자이면 계획 제출이 `bad_request`이다(D005·D007).
  - `ascend`

    | 인자 | 뜻 | 범위 | 기본값 |
    |---|---|---|---|
    | `apoapsis_m` | 목표 Ap 고도(해발, m) | 0보다 큼 | 필수 |
    | `heading` | 상승 방위(°) | 0 이상 360 미만 | 90. `inclination_deg`와 함께 줄 수 없음 |
    | `inclination_deg` | 목표 경사각(°) | 0~180 | 없음 |
    | `launch` | 경사각 발사의 방향 | `north`·`south` | `north`. `inclination_deg`와 함께만 받음 |
    | `max_q_kpa` | 동압 상한(kPa) | 0보다 큼 | 20 |
    | `max_aoa_deg` | 받음각 한계(°) | 0보다 크고 90 이하 | 5 |
    | `turn_start_m` | 선회를 시작할 높이(단계를 시작한 고도부터, m) | 0 이상 | 대기가 있으면 500, 없으면 50 |
    | `turn_end_m` | 선회를 마칠 고도(해발, m) | 0보다 큼 | 대기가 있으면 대기 상한의 65%, 없으면 선회 시작 고도(R3) + (목표 Ap − 선회 시작 고도)의 10% |
    | `auto_stage` | 자동 스테이징 | 참·거짓 | 참 |

  - `circularize`: `tolerance_mps`(남은 Δv 허용값, 0보다 크고 100 이하, 기본 0.5), `inclination_deg`(목표 경사각(°), 0~180, 기본 없음, D016), `auto_stage`(참·거짓, 기본 참)
- R3. `ascend`는 시작하면 SAS를 끄고 자세 유지를 건다. 롤은 맞추지 않는다. 목표 방향은 매 물리 프레임에 다음과 같이 정한다(D002).
  - 피치: 90 − 90 × f^0.6(°). f는 고도 비율과 Ap 비율 가운데 작은 값이며 각각 0~1로 자른다. 고도 비율은 (고도 − 선회 시작 고도) ÷ (선회를 마칠 고도 − 선회 시작 고도), Ap 비율은 (Ap − 선회 시작 고도) ÷ (대기 상한 − 선회 시작 고도)이다. 선회 시작 고도는 단계를 시작한 고도 + `turn_start_m`이고, 대기가 없는 천체에서는 고도 비율만 쓴다.
  - 처짐 방지: Ap가 대기 상한(대기가 없으면 선회를 마칠 고도)을 넘었는데 목표 Ap에 닿기 전에 Ap까지 남은 시간이 30초보다 짧거나 이미 Ap를 지나 내려가는 중이면, 남은 시간이 짧을수록 피치를 최대 20°까지 더 든다(내려가는 중이면 20°).
  - 방위: `heading`을 주면 그 값이다. `inclination_deg`를 주면, 목표 Ap 고도의 원궤도 속력을 크기로 하고 그 경사각과 지금 위도에서 정해지는 방위를 방향으로 하는 수평 궤도 속도에서 지금의 수평 궤도 속도를 뺀 방향이다(자전이 이미 준 속도를 빼고, 상승 중 어긋난 것을 바로잡는다). 뺀 속도가 원궤도 속력의 5%보다 작으면 경사각에서 정해지는 방위를 그대로 쓴다. 경사각에서 정해지는 방위 β는 sin β = cos(경사각) ÷ cos(위도)의 두 해 가운데, `launch`가 `north`면 북쪽 성분이 0 이상인 해(경사각 90° 미만이면 북동쪽, 90°를 넘으면 북서쪽), `south`면 0 이하인 해다. 지금 위도에서 sin β의 해가 없으면(대체 규칙) 실패하지 않고, 목표 경사각이 위도의 절댓값보다 작으면 정동(경사각 = 위도의 절댓값), 180° − 위도의 절댓값보다 크면 정서(경사각 = 180° − 위도의 절댓값)를 β로 쓴다(D018). 이 방향은 목표 원궤도 속력에 닿아야 경사각이 맞는데 `ascend`는 그보다 느린 채 목표 Ap에서 끝나므로, 끝난 뒤의 경사각은 목표와 다를 수 있다. 같은 경사각을 `circularize`에 주면 원궤도화 연소에서 맞춘다(R8, D016).
  - 받음각 한계: 동압이 1 kPa를 넘으면 목표 방향을 지표 속도 방향에서 `max_aoa_deg` × max(1, 10 ÷ 동압) 안으로 당긴다.
  - 목표 Ap에 닿아 대기 안에서 기다리는 동안(R5)은 지표 속도 방향이다.
- R4. `ascend`의 스로틀은 매 물리 프레임에 다음 가운데 작은 값이다. 값은 `throttle` 단계와 같은 방식으로 게임에 적용한다.
  - Ap: 목표까지 남은 Ap가 목표의 5%보다 크면 1, 그 아래에서는 남은 만큼 줄이되 0.05보다 작지 않다. 한 번 목표에 닿은 뒤에는 R5에서 다시 채우는 동안이 아니면 0이고, 다시 채우는 동안은 다시 목표에 닿을 때까지 이 식을 쓴다.
  - 동압: `max_q_kpa`의 80% 이하이면 1, 80%부터 줄여 100% 이상에서 0이다(대기가 없으면 1). 고체 엔진처럼 스로틀을 따르지 않는 엔진에는 듣지 않는다(D005).
- R5. 목표 Ap에 닿으면 스로틀을 0으로 하고, 완료되기 전에 Ap가 목표의 99%보다 낮아지면 대기 안팎을 가리지 않고 목표까지 다시 채운다. 다음을 모두 만족하는 첫 물리 프레임에 완료된다(D003). 완료되면 스로틀은 0이고 자세 유지는 풀리지 않고 궤도 프로그레이드로 남는다.
  - 한 번이라도 목표 Ap에 닿았고 지금 Ap가 목표의 99% 이상이다.
  - 고도가 대기 상한보다 높다(대기가 없으면 보지 않는다).
  - 활성 기체의 추력이 0이다(끌 수 없는 고체 부스터가 있으면 다 탈 때까지 기다리며, 그 때문에 Ap가 목표를 넘어도 실패가 아니다).
- R6. 자동 스테이징(`auto_stage` 참)은 단계가 추력을 원하는 동안 쓸 엔진이 하나 이상 있고 다음 가운데 하나일 때 다음 스테이지를 작동한다(D001).
  - 버려질 엔진이 하나 이상 있고, 그 가운데 켜진 엔진이 없고 아직 점화되지 않은 엔진도 없으며, 버려지지 않는 켜진 엔진이 하나 이상 남아 있다(병렬 부스터, D015).
  - 켜진 엔진이 하나도 없다(발사대의 첫 점화, 다 탄 단의 분리와 다음 단의 점화).

  작동한 뒤 게임 시간 1초 동안은 다시 작동하지 않고, 게임이 거절하면(스테이징 쿨다운 등) 다음 프레임에 다시 시도한다. 작동하면 `stage` 단계와 같은 이벤트 `stage_activated`에 이유(첫째 조건은 `boosters_out`, 둘째 조건은 `no_running_engine`)가 더해져 남는다(D008). 작동했는데 그 스테이지의 부품이 하나도 작동하지 않으면 단계가 실패한다.
- R7. 다음이면 단계가 실패해 계획이 `step_failed`로 끝난다. 시작할 때의 실패는 스로틀·SAS·자세 유지·스테이지를 바꾸지 않는다.
  - `ascend`가 시작할 때: 활성 기체가 없거나 조종 불가, 목표 Ap가 대기 상한 이하이거나 영향권 밖, 선회를 마칠 고도가 선회 시작 고도 이하, `inclination_deg`가 (지금 위도의 절댓값 − 0.5°)보다 작거나 (180° − 위도의 절댓값 + 0.5°)보다 큼(그 위도에서 낼 수 없는 경사각). 허용 폭 안에서 모자라는 만큼은 R3의 대체 규칙을 따른다.
  - 두 단계가 실행 중일 때: 조종 불가가 되면 그 물리 프레임에.
  - 엔진 소진(두 단계, D008): 추력을 원하는데 켜진 엔진이 없고, `auto_stage`가 꺼져 있거나 쓸 엔진 또는 다음 스테이지가 없을 때. 실패 설명에 엔진 소진이라고 쓴다. docs/features/flight-condition.md의 추력 없음(`no_thrust`)과는 다른 조건이다.
  - 조종 상실(`ascend`만, D006·D017): 대기 안에서 동압이 1 kPa 이상이거나 엔진 추력 합이 0.01 kN 이상이고, 기수 방향과 목표 방향 사이의 각이 30°를 넘는 상태가 게임 시간 3초 동안 이어질 때. 추력을 내며 구르는 기체는 속도를 잃어 동압이 1 kPa 아래로 떨어지므로 추력만으로도 센다.
  - `circularize`가 시작할 때(`inclination_deg`를 주었을 때): `inclination_deg`가 (지금 위도의 절댓값 − 0.5°)보다 작거나 (180° − 위도의 절댓값 + 0.5°)보다 큼. 허용 폭 안에서 모자라는 만큼은 R3의 대체 규칙을 따른다.
- R8. `circularize`는 SAS를 끄고 자세 유지를 걸고, 연소가 다음 Ap를 가운데 두도록 시작 시각을 정해 1배속으로 기다렸다 연소한다(시간 가속은 하지 않는다, D004). 시작 시각이 이미 지났거나 Ap를 지나 내려가는 중이면 바로 연소한다. 연소 방향은 지금 궤도 속도에서 지금 위치의 원궤도 속도(수평)로 가는 Δv의 방향이다. 원궤도 속도의 방향은 `inclination_deg`가 없으면 지금 수평 궤도 속도의 방향(같은 궤도면)이고, 있으면 그 경사각과 지금 위도에서 R3과 같이 정해지는 방위 β 두 해 가운데 지금 수평 궤도 속도의 방향에 가까운 해다(D016). 연소하는 위도에서 그 경사각을 낼 수 없으면 R3의 대체 규칙을 따르며 실패하지 않는다. 끝난 뒤의 경사각은 `circularize_completed`에 남는다(D018). 남은 Δv에는 궤도면을 맞추는 몫도 들어간다. 남은 Δv가 10 m/s 아래가 되면 그때의 방향으로 고정한다. 기수가 연소 방향에서 5°를 넘게 벗어나 있으면 추력을 내지 않고 맞을 때까지 기다린다. 남은 Δv가 작아지면 스로틀을 줄인다. 시작할 때 활성 기체가 없거나 조종 불가이거나, 착지·착수·발사 전 상태이거나, 궤도가 열려 있거나(이심률 1 이상), Ap가 영향권 밖이거나, Ap가 대기 상한 이하이면 단계가 실패한다(R7과 같이 아무것도 바꾸지 않는다). 연소 국면에서는 자동 스테이징(R6)과 엔진 소진(R7)이 적용된다.
- R9. `circularize`는 남은 Δv가 `tolerance_mps` 이하이거나, 10 m/s 아래에서 추력을 내는데도 남은 Δv가 다시 늘기 시작하면 완료된다. 완료되면 스로틀은 0이고 자세 유지는 풀리지 않고 궤도 프로그레이드로 남는다.
- R10. 실행 중에는 `get_plan`의 `hold`에 `type`으로 단계 이름(`ascend`·`circularize`)이, `target`으로 `attitude` 단계와 같은 필드(마지막에 정한 `pitch_deg`·`heading_deg`, `roll_deg`는 `null`)가 나온다. 완료된 뒤에는 `attitude` 단계와 같은 `type` `attitude`와 `target.direction` `prograde`가 나온다(D013). `get_plan`의 `step_progress`에는 다음이 나온다. 스로틀은 단계가 명령한 값이다.
  - `ascend`: 국면(`climb` 상승과 Ap를 다시 채우는 동안·`coast` 목표 Ap에 닿아 추력을 끄고 기다리는 동안. 대기 안에서, 또는 끌 수 없는 엔진이 다 탈 때까지), 목표와 지금 Ap(m), 목표 피치·방위(°), 기수 오차(°), 스로틀, 동압(kPa), 받음각(°, 없으면 `null`), 작동한 스테이지 수
  - `circularize`: 국면(`wait`·`burn`), 시작 UT, 예상 연소 시간(s), 남은 Δv(m/s), 기수 오차(°), 스로틀, 작동한 스테이지 수

  완료되면 이벤트가 남는다. `ascend_completed`: 단계 번호, Ap·Pe(m), 경사각(°), 고도(m), 동압 최대(kPa), 동압 1 kPa 이상에서의 받음각 최대(°, 없으면 `null`), 작동한 스테이지 수. `circularize_completed`: 단계 번호, Ap·Pe(m), 이심률, 경사각(°), 남은 Δv(m/s), 연소 시작 UT와 완료 UT, 작동한 스테이지 수.
- R11. 두 단계가 실행되는 동안 스로틀은 단계가 정하고, 완료되면 0이다(앞 단계의 `throttle` 값은 이어지지 않는다). 스로틀을 내리는 종료 사유로 끝난 계획에서는 스로틀을 0으로 내리고 이벤트 `throttle_cut`(사유와 내리기 전 스로틀 `from`)을 남긴다. 이 둘과 자세 유지는 docs/features/flight-control.md R2·R3·R6의 요구이다. 두 단계를 실행하는 중에 Mod 로직을 다시 읽으면 자세 유지와 조종 입력 처리기가 남지 않고 스로틀이 0이다(flight-control R10). 두 단계에서 예외가 나면 그 계획만 `step_failed`로 끝나고 게임에 예외가 남거나 도구가 멈추지 않는다.
- R12. 실제 게임에서 MCP 응답만으로 다음이 맞는다(물리 시간 가속 없이). 판정에는 계획을 제출하기 전이나 끝난 뒤의 응답만 쓴다. 비행 중 조회는 이상을 일찍 알아채기 위한 것이고 판정에 쓰지 않는다.
  - 궤도 진입: 발사대의 stock `Kerbal X`(승무원 탑승, 스테이징 전)에서 계획 `ascend`(`apoapsis_m` 80000) → `warp`(`to` apoapsis, `lead_s` 60) → `circularize`가 `completed`로 끝난다. `ascend_completed`의 Ap가 79.2~81.6 km, 동압 최대가 20 kPa 이하이다. 끝난 뒤 `get_flight`의 Ap·Pe가 모두 76~84 km이고 이심률이 0.003 이하이며 스로틀이 0이다. 이벤트 `stage_activated`의 번호가 7부터 차례로 내려가고 2 이상이며(스테이지 1 사령선 분리와 0 낙하산은 작동하지 않음), 끝난 뒤 다음 스테이지가 1 이상이다. `parts_lost` 이벤트가 없다.
  - 경사각: 같은 기체에서 계획 `ascend`(`apoapsis_m` 80000, `inclination_deg` 45) → `warp`(`to` apoapsis, `lead_s` 60) → `circularize`(`inclination_deg` 45)가 `completed`로 끝나고, 끝난 뒤 `get_flight`의 경사각이 45°와 1° 안에서 같으며 이심률이 0.003 이하이다(D016).
  - 조종 상실: 대기 안에서 뒤집히는 기체의 `ascend`가 `step_failed`로 끝나고 종료 설명이 조종 상실이며, `throttle_cut` 이벤트가 있고 그 뒤 `get_flight`의 스로틀이 0이다. 기체는 무게중심 앞에 날개를 단 고체 부스터 탐사선이고 인자는 `apoapsis_m` 80000이다(docs/specs/궤도-진입/plan.md 검증 전략).
  - 다시 읽기(D011): `debug_set_orbit`으로 Kerbin Ap 1,000 km·Pe 100 km 궤도에 둔 `Kerbal X`에서 계획 `warp`(`to` apoapsis, `lead_s` 60) → `circularize`를 제출한다(Ap에서 원궤도까지 활력 방정식으로 약 330 m/s라 연소가 10초 넘게 이어진다). `get_plan`의 `step_progress` 국면이 `burn`이고 스로틀이 0보다 큰 것을 본 뒤 Mod 로직을 다시 읽으면(이 조회는 다시 읽을 시점을 고르는 것이며 판정에 쓰지 않는다), `get_plan`의 마지막 계획 종료 사유가 `reload`이고, 그 계획의 `throttle_cut` 이벤트가 있어 사유가 `reload`이고 내리기 전 스로틀(`from`)이 0보다 크며(연소 중이었음을 보인다, D012), `get_status`의 `bridge_flybywire_handlers`가 0, `get_flight`의 스로틀이 0이다.
  - 오류: `apoapsis_m` 없음, `heading`과 `inclination_deg`를 함께 줌, `inclination_deg` 없는 `launch`, `max_q_kpa` 0, 모르는 인자를 제출하면 `bad_request`이고 그 뒤 `get_status`가 정상으로 응답한다. 발사대에서 `ascend`(`apoapsis_m` 60000)와 `circularize`가 각각 `step_failed`로 끝나고 기체의 다음 스테이지와 스로틀이 바뀌지 않는다.

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-23 | 궤도-진입 | R1~R12 추가(기준 spec 처음 만듦). 게임 검증 뒤 R1·R2·R3·R7·R8·R12를 D016~D018로 고침 | docs/specs/궤도-진입/ |

표에 없는 이전 변경: 기록 없음
