# 기동 노드 생성과 실행

기능: flight-maneuver(새 기능) / flight-warp(기준 spec docs/features/flight-warp.md) / flight-control(기준 spec docs/features/flight-control.md)
이슈: GitHub #37

## 무엇을, 왜

지금 Planner(LLM)가 궤도를 바꾸는 수단은 발사대에서 궤도까지 가는 `ascend`와 Ap에서 원궤도로 만드는 `circularize`뿐이다(docs/features/flight-ascent.md). Ap·Pe 조정이나 호만 전이처럼 궤도 위 아무 곳에서 정해진 Δv를 내는 일은, 게임의 기동 노드를 만들고 그 노드대로 정확히 연소하는 기능이 바탕이 된다.

이 변경은 새 Mod 모듈과 MCP 서버 모듈 쌍(`maneuver`)으로 다음을 더한다.

1. MCP 도구 `create_node`·`get_nodes`·`delete_node`: 활성 기체의 기동 노드를 만들고 보고 지운다. 만들 때는 Δv 성분을 직접 주거나, Ap·Pe·원궤도 같은 목표를 주어 Mod가 Δv를 계산하게 한다(D001). 조회는 이 모듈의 도구로 하고 `get_flight`는 바꾸지 않는다(D003).
2. 계획 단계 `execute_node`: 맨 앞 노드의 방향으로 기수를 맞추고, 연소의 가운데가 노드 시각에 오도록 연소해 남은 Δv가 허용값 이하가 되면 끝내고 노드를 지운다(D006). 실행 정확도를 이벤트로 남긴다.

노드까지의 긴 코스팅은 `warp`에 노드 목표(`to` node)를 더해 기다린다(D002). 계획은 `warp`(`to` node, `lead_s`) → `execute_node`로 적고, 노드가 여럿이면 이 둘을 되풀이한다.

범위 밖(D004):

- 다른 기체·천체로의 전이 계산(랑데부·행성 간 전이), 경사각 변경 계산
- 여러 노드를 한 단계로 연속 실행하는 것
- `execute_node` 안의 시간 가속(궤도-진입 D004와 같음)
- 경력 모드의 기동 노드 미해금 상태에서의 게임 검증(단위 시험으로만 확인한다)
- 대기 안에서의 노드 실행에 대한 게임 검증

## 용어

이 문서에서 "디컴파일"은 게임 설치 폴더의 `KSP_x64_Data/Managed/Assembly-CSharp.dll`(KSP 1.12.5)을 `ilspycmd`로 풀어 읽은 것이다.

- 활성 기체, 다음 스테이지: docs/features/flight-observation.md의 용어와 같다.
- 자세 유지, 조종 불가, 스로틀을 내리는 종료 사유: docs/features/flight-control.md의 용어와 같다.
- 켜진 엔진, 쓸 엔진, 자동 스테이징, 엔진 소진: docs/features/flight-ascent.md의 용어·요구(R6·R7)와 같다.
- 노드: 활성 기체의 기동 노드. 게임은 기체의 궤도 계산기(`Vessel.patchedConicSolver`)가 노드 목록(`PatchedConicSolver.maneuverNodes`)을 들고, UT 순으로 정렬한다(디컴파일 `PatchedConicSolver.UpdateFlightPlan`). 노드는 UT와 Δv를 가진다(`ManeuverNode.UT`·`DeltaV`). `DeltaV`는 연소해도 줄지 않는다(디컴파일 `ManeuverNode`에서 `DeltaV`를 바꾸는 곳은 화면 조작 `OnGizmoUpdated`와 불러오기 `Load`뿐).
- 노드 사용 가능: 다음 둘을 모두 만족하는 상태(D008). 시설 등급이 없는 게임(`ScenarioUpgradeableFacilities.Instance`가 없음)은 두 판정 모두 해금으로 본다(디컴파일 `Vessel.patchedConicsUnlocked`, `OrbitTargeter.Start`).
  - 궤도 계산기가 있음: 게임은 추적소 등급이 궤도 예측(`GameVariables.GetOrbitDisplayMode`가 `PatchedConics`)을 허용할 때만 기체에 궤도 계산기를 붙인다(디컴파일 `Vessel.AttachPatchedConicsSolver`·`patchedConicsUnlocked`).
  - 비행 계획 해금: 관제 센터 등급이 비행 계획을 허용함(`GameVariables.UnlockedFlightPlanning`, 등급 0.4 초과). 게임 화면은 이것이 거짓이면 노드를 만들 수 없게 한다(디컴파일 `OrbitTargeter.Start`의 `flightPlanningUnlocked`).
- 맨 앞 노드: UT가 가장 이른 노드. 번호 0이다. 노드 번호는 UT 순서다.
- 노드 뒤 궤도: 노드의 Δv를 낸 뒤의 궤도. 게임이 노드마다 계산한 다음 궤도(`ManeuverNode.nextPatch`)이다. 게임은 계산한 궤도 구간 안에서 찾은 노드에만 이 값을 정하므로(디컴파일 `PatchedConicSolver.CheckNextManeuver`) 없을 수 있다.
- 기준 궤도: 새 노드를 만들 때 시각과 목표를 계산하는 궤도. 노드가 있으면 마지막 노드의 노드 뒤 궤도, 없으면 활성 기체의 지금 궤도다.
- Δv 성분: 노드 시각을 품은 궤도 구간, 곧 게임이 그 노드에 정한 구간(`ManeuverNode.patch`)에서 노드 시각에 정한 세 방향의 성분(m/s). 게임은 노드 시각이 앞 구간에 없으면 다음 구간(영향권 이탈 뒤 등)에서 찾아 그 구간을 쓴다(디컴파일 `PatchedConicSolver.CheckNextManeuver`). `create_node`로 만드는 노드는 C2의 시각 제약(영향권 이탈·다른 천체 영향권 진입으로 끝나는 구간이면 그 끝 전) 때문에 이 구간이 기준 궤도와 같다. 진행(`prograde`, 궤도 속도 방향), 법선(`normal`, 궤도면의 법선. 위치 × 속도 방향), 방사(`radial`, 궤도면 안에서 진행에 수직이고 천체 중심에서 멀어지는 쪽)이다. 게임의 `ManeuverNode.DeltaV`는 x 방사·y 법선·z 진행 순서의 성분이고, 게임은 이를 노드 시각의 위치·속도로 만든 회전(`QuaternionD.LookRotation(속도, Cross(−위치, 속도))`)으로 궤도 좌표로 바꾼다(디컴파일 `PatchedConicSolver.CheckNextManeuver`). 법선·방사의 부호가 위 정의와 같은지는 미확인이며, C9 성분 지정 항목에서 게임으로 확인한다.
- 남은 Δv: 맨 앞 노드를 다 내려면 지금 더 내야 하는 Δv. 게임이 노드 연소 방향으로 쓰는 벡터(`ManeuverNode.GetBurnVector`, 노드 뒤 궤도의 노드 시각 속도 − 지금 궤도의 노드 시각 속도)의 크기이며, 그 벡터의 방향이 연소 방향이다. 노드 뒤 궤도가 없으면 게임은 0 벡터를 주므로(디컴파일 `ManeuverNode.GetBurnVector`) 남은 Δv는 없다.
- 예상 연소 시간: 맨 앞 노드는 남은 Δv를, 그 뒤 노드는 노드 Δv 크기를 내는 데 걸리는 시간. `circularize`와 같은 계산(게임 Δv 계산기의 스테이지 값, 없으면 켜진 엔진의 가속도. `src/KspBridge.Mod.Modules.Ascent/BurnEstimate.cs`의 `Seconds`)으로 낸다. 낼 수 없거나 그 노드의 노드 뒤 궤도가 없으면 없다.
- 연소 시작 시각: 노드 UT − 예상 연소 시간의 절반.

## 완료 조건

- C1. 기동 노드 기능이 Mod 모듈과 MCP 서버 모듈 쌍(`maneuver`)으로 더해지고 핫리로드로 반영된다. MCP 도구 `create_node`·`get_nodes`·`delete_node`가 Claude Code에 나오고, 단계 종류 `execute_node`가 `list_step_types`에 나온다. 도구와 단계의 설명에 인자(이름·범위·기본값), 오류·완료·실패 조건, 계획을 `warp`(`to` node, `lead_s`) → `execute_node`로 적는다는 안내, 호만 전이를 노드 두 개(`at` periapsis와 `goal.apoapsis_m` → `at` apoapsis와 `goal.circular`)로 만드는 예가 들어간다. `execute_node` 설명에는 `abort_on.no_thrust`와 함께 쓰지 말라는 안내가 들어간다(docs/features/flight-ascent.md R1과 같은 이유).
- C2. `create_node`는 기준 궤도 위 한 시각에 노드 하나를 만들고, 만든 노드를 `get_nodes`의 항목(C3)과 같은 형식으로 돌려준다.
  - 시각은 `ut`(게임 시각 s), `in_s`(지금부터의 게임 시간 s, 0보다 큼), `at`(`apoapsis`·`periapsis`) 가운데 정확히 하나로 준다. `at`의 시각은 기준 궤도에서 지금 UT와 마지막 노드 UT 가운데 늦은 쪽보다 뒤인 첫 Ap(Pe)이다(마지막 노드가 그 Ap(Pe)에 있으면 한 주기 뒤). 시각은 지금보다 뒤이고, 노드가 있으면 마지막 노드의 UT보다 뒤이며, 기준 궤도의 구간 끝 전이가 영향권 이탈이나 다른 천체 영향권 진입이면 그 구간 끝(`EndUT`)보다 앞이어야 한다. 끝 전이가 없는 닫힌 궤도(`FINAL`)는 이 제약이 없다(게임은 이탈·진입으로 끝나는 구간 뒤의 노드만 다음 구간에 놓고, `FINAL` 구간은 끝 시각을 지난 노드도 그 구간에 놓는다, 디컴파일 `PatchedConics.CalculatePatch`·`PatchedConicSolver.CheckNextManeuver`, D013).
  - Δv는 성분(`prograde_mps`·`normal_mps`·`radial_mps`, 수, 생략하면 0)으로 주거나, `goal` 객체에 다음 가운데 정확히 하나로 준다(D001). 성분으로 줄 때는 하나 이상 주어야 하고 모두 0이면 안 된다. 성분과 `goal`은 함께 줄 수 없다.
    - `apoapsis_m`(해발 m): 진행 성분만으로 노드 뒤 궤도의 Ap를 이 값으로 만든다. 노드 시각의 고도 이상이고 Ap의 반지름이 영향권 반지름보다 작아야 한다.
    - `periapsis_m`(해발 m): 진행 성분만으로 노드 뒤 궤도의 Pe를 이 값으로 만든다. 0 이상이고 노드 시각의 고도 이하여야 한다.
    - `circular`(참만 받음): 노드 시각의 위치에서 같은 궤도면의 원궤도로 만든다(진행·방사 성분).
  - 만든 뒤 게임이 계산한 노드 뒤 궤도가 목표와 맞는다. `apoapsis_m`이면 Ap가 목표와 1 m 안, `periapsis_m`이면 Pe가 목표와 1 m 안, `circular`이면 Ap·Pe가 모두 노드 시각의 고도와 1 m 안이다.
  - 오류: 비행 장면이 아니거나 장면을 불러오는 중이거나 활성 기체가 없으면 `not_ready`이다. 노드 사용 가능이 아니면 어느 시설(추적소·관제 센터)이 모자라는지 이유와 함께 `not_ready`이다. 실행 중인 계획이 있으면(D007), 활성 기체가 착지·착수·발사 전 상태이면, `at`의 Ap(Pe)가 기준 궤도에 없으면(열린 궤도의 Ap, 열린 궤도에서 이미 지난 Pe), 기준 궤도로 낼 수 없는 목표이면, 위 인자 규칙에 맞지 않거나 모르는 인자이면 이유와 함께 `bad_request`이다. 오류이면 노드를 만들지 않는다. 만든 노드에 게임이 노드 뒤 궤도를 계산하지 않았으면 그 노드를 지우고 이유와 함께 `bad_request`이다.
- C3. `get_nodes`는 활성 기체의 노드를 UT 순으로 돌려준다. 노드를 바꾸지 않는 조회다. 항목마다 번호, UT, 노드까지 남은 게임 시간(s, 지났으면 음수), Δv 성분 셋과 크기(m/s), 예상 연소 시간(s, 없으면 `null`), 연소 시작 시각(없으면 `null`), 노드 뒤 궤도(천체 이름, Ap·Pe(해발 m. 열린 궤도의 Ap는 `null`), 이심률, 경사각(°), 노드 UT보다 뒤인 첫 Ap·Pe의 UT(D011. 게임이 그 궤도에 정한 구간 끝(뒤 노드·영향권 이탈·다른 천체 영향권 진입·닫힌 궤도의 한 주기)과 상관없이 노드 뒤 궤도의 원뿔곡선에서 정하며, 열린 궤도의 Ap와 열린 궤도에서 이미 지난 Pe만 `null`, D012))가 들어간다. 노드 뒤 궤도가 없으면 그 값은 `null`이고 이유가 들어가며, 예상 연소 시간과 연소 시작 시각도 `null`이다. 맨 앞 노드에는 남은 Δv(m/s, 노드 뒤 궤도가 없으면 `null`)가 더 들어간다. 노드가 없으면 빈 목록이다. 비행 장면이 아니거나 불러오는 중이거나 활성 기체가 없으면 `not_ready`, 노드 사용 가능이 아니면 이유와 함께 `not_ready`이다. 실행 중인 계획이 있어도 된다.
- C4. `delete_node`는 `index`(노드 번호)나 `all`(참만 받음) 가운데 정확히 하나를 받아 그 노드나 모든 노드를 지우고, 지운 뒤의 `get_nodes` 목록을 돌려준다. 노드 하나를 지우면 그 뒤 노드의 번호가 하나씩 당겨진다. 없는 번호, 둘 다 주거나 둘 다 없음, 거짓인 `all`, 모르는 인자, 실행 중인 계획이 있음(D007)은 이유와 함께 `bad_request`이고 노드를 지우지 않는다. 장면·기체·노드 사용 가능에 대한 오류는 C3과 같다. `all`인데 노드가 없으면 오류가 아니다.
- C5. `execute_node`는 맨 앞 노드를 실행한다.
  - 인자: `tolerance_mps`(남은 Δv 허용값, 0보다 크고 100 이하, 기본 0.5), `auto_stage`(참·거짓, 기본 참). 범위·종류가 맞지 않거나 모르는 인자이면 계획 제출이 `bad_request`이다.
  - 시작할 때 다음이면 단계가 실패해 계획이 `step_failed`로 끝난다. 이때 스로틀·SAS·자세 유지·스테이지·노드를 바꾸지 않는다.
    - 활성 기체가 없거나 조종 불가, 노드 사용 가능이 아님, 노드가 없음, 착지·착수·발사 전 상태
    - 지금 UT가 맨 앞 노드의 UT 이상임(D009. 연소 시작 시각만 지났으면 실패가 아니다)
    - 맨 앞 노드의 노드 뒤 궤도가 없음
    - 예상 연소 시간을 낼 수 없음
  - 시작하면 SAS를 끄고 자세 유지를 건다. 롤은 맞추지 않는다. 연소 방향은 매 물리 프레임의 남은 Δv 벡터 방향이고, 남은 Δv가 10 m/s 아래가 되면 그때의 방향으로 고정한다. 연소 시작 시각(시작할 때 한 번 정함)까지 1배속으로 기수를 맞추며 기다리고, 이미 지났으면 바로 연소한다. 기수가 연소 방향에서 5°를 넘게 벗어나 있으면 추력을 내지 않고 맞을 때까지 기다린다. 남은 Δv가 작아지면 스로틀을 줄인다. 연소 국면에서는 자동 스테이징과 엔진 소진(docs/features/flight-ascent.md R6·R7)이 적용되며, 추력을 원함은 `circularize`와 같다(연소 국면이고 남은 Δv가 `tolerance_mps`보다 큼).
  - 남은 Δv가 `tolerance_mps` 이하이거나, 10 m/s 아래에서 추력을 내는데도 남은 Δv가 다시 늘기 시작하면 완료된다(docs/features/flight-ascent.md R9의 `circularize`와 같은 판정). 완료되는 물리 프레임에 스로틀이 0이 되고 실행한 노드를 지우며(D006), 자세 유지는 풀리지 않고 궤도 프로그레이드로 남는다.
  - 실행 중에 조종 불가가 되거나, 맨 앞 노드가 없어지거나 다른 노드로 바뀌거나, 맨 앞 노드의 노드 뒤 궤도가 없어지면 그 물리 프레임에 단계가 실패한다. 실패·중단으로 끝나면 노드는 남는다.
- C6. flight-warp의 `warp`에 목표 `to` `node`를 더한다(D002). 목표 UT는 단계가 시작된 물리 프레임에 한 번 정하며, 그때 맨 앞 노드의 연소 시작 시각 − `lead_s`이다(D005). 나머지 동작(배율 조절, 완료, 종료 때 배율 되돌리기, 이벤트)은 다른 목표와 같다. 단계가 시작될 때 노드 사용 가능이 아니거나, 노드가 없거나, 지금 UT가 맨 앞 노드의 UT 이상이거나(D009), 맨 앞 노드의 노드 뒤 궤도가 없거나, 활성 기체가 착지·착수·발사 전 상태이거나, 예상 연소 시간을 낼 수 없으면 단계가 실패해 계획이 `step_failed`로 끝난다. `list_step_types`의 `warp` 설명에 `node` 목표가 들어간다. 다른 목표의 동작은 바뀌지 않는다.
- C7. flight-control의 요구를 다음과 같이 바꾼다. 스로틀이 뒤 단계와 계획 완료 뒤에도 남는다는 요구(R2)의 예외에 `execute_node`를 더한다(실행되는 동안 스로틀은 단계가 정하고 완료되면 0). 스로틀을 내리는 종료 사유로 끝난 계획에서 스로틀을 0으로 내리는 조건(R3)에 "스로틀을 바꾼 `execute_node` 단계가 있었던 계획"을 더하고, 자세 유지를 시작하거나 바꾸는 단계(R6)에 `execute_node`를 더한다. `execute_node`를 쓰지 않는 계획의 동작은 바뀌지 않는다. `execute_node`를 실행하는 중에 Mod 로직을 다시 읽어도 flight-control R10과 같다(자세 유지와 조종 입력 처리기가 남지 않고 스로틀이 0). `execute_node`에서 예외가 나면 그 계획만 `step_failed`로 끝나고 게임에 예외가 남거나 도구가 멈추지 않는다.
- C8. 실행 중에는 `get_plan`의 `hold`에 `type` `execute_node`와 `attitude` 단계와 같은 필드의 `target`(마지막에 정한 `pitch_deg`·`heading_deg`, `roll_deg`는 `null`)이 나오고, 완료된 뒤에는 `type` `attitude`와 `target.direction` `prograde`가 나온다. `step_progress`에는 국면(`wait`·`burn`), 연소 시작 시각, 예상 연소 시간(s), 남은 Δv(m/s), 기수 오차(°), 스로틀(단계가 명령한 값), 작동한 스테이지 수가 나온다. 완료되면 이벤트 `node_executed`에 다음이 남는다.
  - 단계 번호, 노드 UT, 노드 Δv 크기(m/s), 예상 연소 시간(s), 연소 시작 UT와 완료 UT, 작동한 스테이지 수
  - 완료 때 남은 Δv(m/s)와 그 성분 셋(D010. 완료 시점의 지금 궤도, 곧 그때의 위치·속도로 정한 진행·법선·방사 방향의 성분)
  - 단계가 시작될 때 게임이 계산한 노드 뒤 궤도의 Ap·Pe(m)·경사각(°)
  - 완료 뒤 실제 궤도의 Ap·Pe(m)·이심률·경사각(°)
- C9. 실제 게임에서 MCP 응답만으로 다음이 맞는다(물리 시간 가속 없이). 판정에는 도구 응답과 계획을 제출하기 전이나 끝난 뒤의 응답만 쓴다. 비행 중 조회는 이상을 일찍 알아채기 위한 것이고 판정에 쓰지 않는다. 기체와 궤도는 `debug_set_orbit`으로 Kerbin 궤도에 둔 기체이며, 어느 기체를 쓰는지는 plan에서 정한다.
  - 노드 조작: 타원 궤도(Ap 300 km·Pe 100 km)에서 `create_node`(`ut` = 지금 UT + 600, 진행 성분 10)와 `create_node`(`at` periapsis, `goal.periapsis_m` 80000)를 만들면, `get_nodes`가 노드 둘을 UT 순으로 보이고 첫 노드의 UT가 준 값과 같으며, 둘째 노드의 UT가 첫 노드 항목의 노드 뒤 궤도에 나온 Pe의 UT와 1초 안에서 같고 그 노드 뒤 궤도의 Pe가 80 km와 1 m 안이다. `delete_node`(`index` 0) 뒤 목록에 노드 하나가 남아 번호가 0이고 그 노드가 앞서 둘째였던 노드(UT가 같음)이다. `delete_node`(`all` 참) 뒤 목록이 비어 있다.
  - 호만 전이: 원궤도(Ap·Pe 100 km)에서 `create_node`(`in_s` 600, `goal.apoapsis_m` 300000)와 `create_node`(`at` apoapsis, `goal.circular` 참)로 노드 둘을 만들면, `get_nodes`가 노드 둘을 UT 순으로 보이고 첫 노드 뒤 궤도의 Ap가 300 km와 1 m 안이며 둘째 노드 뒤 궤도의 Ap·Pe가 모두 첫 노드 뒤 궤도의 Ap와 1 m 안이다. 계획 `warp`(`to` node, `lead_s` 60) → `execute_node` → `warp`(`to` node, `lead_s` 60) → `execute_node`가 `completed`로 끝나고, `node_executed` 둘의 완료 때 남은 Δv가 1 m/s 이하이며 실제 Ap·Pe가 각각 단계 시작 때 노드 뒤 궤도의 Ap·Pe와 1% 안이다. 끝난 뒤 `get_flight`의 Ap·Pe가 모두 297~303 km이고 이심률이 0.003 이하, 스로틀이 0이며 `get_nodes`가 빈 목록이다.
  - 성분 지정: 진행·법선·방사 성분을 하나씩 준 노드(크기 50 m/s)를 각각 만들고 실행하면, `node_executed`의 실제 Ap·Pe·경사각이 단계 시작 때 노드 뒤 궤도의 값과 Ap·Pe 1%, 경사각 0.1° 안에서 같다. 각 성분이 용어 "Δv 성분"의 방향으로 났는지의 확인 방법은 plan에서 정한다.
  - 다시 읽기: `execute_node`의 `step_progress` 국면이 `burn`이고 스로틀이 0보다 큰 것을 본 뒤 Mod 로직을 다시 읽으면(이 조회는 다시 읽을 시점을 고르는 것이며 판정에 쓰지 않는다), `get_plan`의 마지막 계획 종료 사유가 `reload`이고 그 계획의 `throttle_cut` 이벤트의 사유가 `reload`이고 `from`이 0보다 크며, `get_status`의 `bridge_flybywire_handlers`가 0, `get_flight`의 스로틀이 0이고 `get_nodes`에 그 노드가 남아 있다.
  - 오류: 시각 없음·시각 둘, 성분과 `goal`을 함께 줌, 성분·`goal` 모두 없음, 모두 0인 성분, 노드 시각의 고도보다 낮은 `goal.apoapsis_m`, 마지막 노드보다 이른 `ut`, 모르는 인자의 `create_node`와, 없는 번호의 `delete_node`가 `bad_request`이고 노드 목록이 바뀌지 않는다. 계획(`wait` 600초) 실행 중의 `create_node`·`delete_node`가 `bad_request`이고 `get_nodes`는 정상으로 응답한다. 노드가 없을 때 계획 `execute_node`와 계획 `warp`(`to` node)가 각각 `step_failed`로 끝나고 스로틀이 바뀌지 않는다. 노드 시각이 지난 노드(`in_s` 30으로 만든 뒤 `wait` 40초)가 맨 앞일 때 계획 `execute_node`와 계획 `warp`(`to` node)가 각각 `step_failed`로 끝나고 노드가 남는다. 발사대에서 `create_node`가 `bad_request`이다. 범위 밖 `tolerance_mps`의 `execute_node`와 `to` 없는 `lead_s`는 계획 제출이 `bad_request`이다. 오류 뒤 `get_status`가 정상으로 응답한다.
