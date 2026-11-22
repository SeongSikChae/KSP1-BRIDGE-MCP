# flight-warp

파일: src/KspBridge.Mod.Modules.Warp/KspBridge.Mod.Modules.Warp.csproj, src/KspBridge.Mod.Modules.Warp/WarpModule.cs, src/KspBridge.Mod.Modules.Warp/WarpArgs.cs, src/KspBridge.Mod.Modules.Warp/WarpTarget.cs, src/KspBridge.Mod.Modules.Warp/WarpRule.cs, src/KspBridge.Mod.Modules.Warp/IWarpGame.cs, src/KspBridge.Mod.Modules.Warp/WarpSession.cs, src/KspBridge.Mod.Modules.Warp/GameWarp.cs, src/KspBridge.Mod.Modules.Warp/WarpStep.cs, src/KspBridge.Server.Modules.Warp/KspBridge.Server.Modules.Warp.csproj, src/KspBridge.Server.Modules.Warp/WarpModule.cs, tests/KspBridge.Mod.Modules.Warp.Tests/KspBridge.Mod.Modules.Warp.Tests.csproj, tests/KspBridge.Mod.Modules.Warp.Tests/FakeWarpGame.cs, tests/KspBridge.Mod.Modules.Warp.Tests/WarpArgsTests.cs, tests/KspBridge.Mod.Modules.Warp.Tests/WarpTargetTests.cs, tests/KspBridge.Mod.Modules.Warp.Tests/WarpRuleTests.cs, tests/KspBridge.Mod.Modules.Warp.Tests/WarpSessionTests.cs, ReadMe.md

목표 시점까지 게임의 시간 가속으로 가서 1배로 정확히 멈추는 단계 종류 `warp`. 별도 MCP 도구는 없고 Planner가 `submit_plan`의 단계로 쓴다. 지금 가속 상태는 `get_flight`의 `time_warp`(docs/features/flight-observation.md R2)에 나온다. 상태 조건으로 멈추기, 기동 노드 목표, 다른 단계의 자동 가속, 비행 장면 밖의 가속, 영향권 전환 시점을 목표로 하기, 레일이 가능한 구간에서 물리 가속을 고르는 것, 사람의 조작을 감지해 멈추는 것은 하지 않는다. 괄호 안 D번호는 docs/specs/시간-가속/decisions.md의 결정이다.

## 용어

- 활성 기체: docs/features/flight-observation.md의 용어와 같다.
- 배율: 지금 실제로 적용된 시간 가속 배수(게임의 `TimeWarp.CurrentRate`). 가속이 없으면 1이다.
- 레일 가속: 게임의 높은 시간 가속(`TimeWarp.Modes.HIGH`)에서 배율이 1보다 큰 상태. 기체가 레일 위인지는 "레일 위" 항목을 따른다.
- 물리 가속: 게임의 물리 시간 가속(`TimeWarp.Modes.LOW`)에서 배율이 1보다 큰 상태. 물리 프레임 하나의 게임 시간이 배율만큼 길어진다.
- 레일 위: 활성 기체가 물리 계산에서 빠진 상태(`Vessel.packed`). 게임은 높은 시간 가속에서 배율이 `TimeWarp.MaxPhysicsRate`보다 크면 기체를 레일에 올리고, 그렇지 않으면 물리 계산으로 되돌린다. 배율이 내려온 뒤 되돌릴 때까지도 레일 위이다.
- 배율 단: 게임이 고를 수 있는 배율 값(레일 `TimeWarp.warpRates`, 물리 `TimeWarp.physicsWarpRates`). 구현은 값을 적어 두지 않고 게임에서 읽는다.
- 레일 상한: 지금 상황에서 게임이 허용하는 가장 높은 레일 배율 단. 게임 설정(`GameParameters.Flight.CanTimeWarpHigh`)이 막으면 1이다. 불러온 기체 가운데 사다리를 잡은 승무원이 있거나, 착지·착수 상태의 기체가 0.3 m/s보다 빠르게 움직이거나, 그 밖의 기체가 대기 안이거나 가속 중이면 1이다. 착지·착수 상태가 아니면 고도에 따라, 그리고 다가오는 영향권 전환에 따라 낮아진다(게임 설정 `ORBIT_WARP_MAXRATE_MODE`·`ORBIT_WARP_DOWN_AT_SOI`).
- 레일이 막힌 구간: 레일 상한이 1인 동안.

## 요구(완료 조건)

- R1. 시간 가속이 Mod 모듈과 MCP 서버 모듈 쌍(`warp`)으로 더해지고 핫리로드로 반영된다. 단계 종류 `warp`가 `list_step_types`에 나오고, 설명에 인자(이름·범위·기본값), 레일이 막힌 구간의 처리, 완료·실패 조건이 들어간다.
- R2. `warp`는 목표로 `params.seconds`(단계가 시작된 UT부터의 게임 시간 s, 0보다 큼), `params.ut`(게임 시각 s), `params.to`(`apoapsis` 또는 `periapsis`) 가운데 하나를 받는다. `to`는 `params.lead_s`(몇 초 전, 0 이상, 생략하면 0)와 함께 쓸 수 있고 `lead_s`는 `to`와 함께만 받는다(D001). 선택 인자 `params.max_rate`(1 이상의 수)를 주면 그 값을 넘지 않는 가장 높은 배율 단까지만 레일 가속을 쓰고, 생략하면 제한이 없다. 선택 인자 `params.physics_rate`(2 이상 4 이하의 정수)는 레일이 막힌 구간에서 쓸 물리 가속의 상한이며, 생략하면 물리 가속을 쓰지 않는다(D002). 목표가 없거나 둘 이상이거나, 종류·범위가 맞지 않거나, 모르는 `to`나 모르는 인자이면 계획 제출이 `bad_request`이다.
- R3. 목표 UT는 단계가 시작된 물리 프레임에 한 번 정하고 그 뒤 바꾸지 않는다. `to`의 목표 UT는 그때의 활성 기체 궤도로 정한 다음 Ap·Pe의 UT에서 `lead_s`를 뺀 값이다. 도중에 추력이나 항력으로 궤도가 바뀌어도 따라가지 않고, Ap·Pe에 닿기 전에 다른 천체의 영향권으로 들어가는지는 보지 않는다.
- R4. 단계가 시작될 때 활성 기체가 없거나, `to`를 쓰는데 활성 기체가 착지·착수·발사 전 상태이거나, `apoapsis`인데 Ap가 없거나 영향권 밖이거나(이심률 1 이상, 또는 Ap의 반지름이 영향권 반지름 이상), `periapsis`인데 열린 궤도에서 이미 Pe를 지났으면 단계가 실패해 계획이 `step_failed`로 끝난다. 이때도 배율은 R7대로 1이 된다(D007).
- R5. 단계는 레일 상한과 `max_rate` 안에서 남은 시간에 맞춰 레일 배율을 올리고 내린다. 게임이나 사람이 배율을 바꿔도 다시 맞춘다(D003). 게임의 자동 가속이 켜져 있으면 끄고 이어받는다. 레일이 막힌 구간에서는 1배로 진행하고, `physics_rate`가 있으면 그 값 이하의 물리 배율을 쓴다(게임 설정 `GameParameters.Flight.CanTimeWarpLow`가 막으면 1배). 레일이 다시 가능해지면 스스로 레일 가속으로 돌아간다. 단계가 시작된 뒤 25 물리 프레임 동안은, 이미 레일 위가 아니면 레일 배율을 올리지 않는다(게임이 가속을 최근 20프레임의 평균으로 보아 방금 켠 추력이 아직 드러나지 않으므로. D017). 영향권 전환이 다가오면 게임이 내리기 전에 단계가 먼저 즉시 내린다(D011).
- R6. 단계는 배율이 1이고 활성 기체가 레일 위가 아닌 상태에서 UT가 목표 UT 이상인 첫 물리 프레임에 완료된다. 배율은 목표 UT 전에 1로 내려 가속 때문에 목표를 지나치지 않는다. 시작할 때 목표 UT가 이미 지났으면 실패하지 않고 같은 조건으로 바로 완료된다(사람이 켜 둔 가속이 있으면 1배로 내린 뒤).
- R7. 단계가 실행되는 동안(시작할 때 실패한 경우 포함) 계획이 어떤 사유로 끝나도 배율을 1로 되돌린다. 게임이 일시정지 중이면 멈춘 상태를 지킨다(D012). `warp`가 실행 중이 아닐 때는 배율을 바꾸지 않는다. `timeout_s`는 다른 단계와 같이 게임 시간이다.
- R8. `warp`는 조종 입력을 넣지 않고 자세 유지를 바꾸지 않는다. 단계가 완료되면 스로틀이 시작할 때와 같고(다르면 완료되는 물리 프레임에 되돌린다. D005), 자세 유지는 풀리지 않으며 레일에서 내려온 뒤 그 목표로 이어진다. 단계가 완료되지 못하고 계획이 끝났을 때는 스로틀을 되돌리지 않아 0(발사 전 상태의 기체는 발사 전 기본 스로틀)으로 남을 수 있다(docs/features/flight-control.md R2·R3의 예외, D010).
- R9. 안전 중단(docs/features/flight-condition.md)은 가속 중에도 매 물리 프레임에 판정된다. 다만 레일에 오른 뒤 단계가 완료될 때까지는 스로틀이 0(발사 전 상태의 기체는 발사 전 기본 스로틀)이므로, 스로틀이 0인 동안 추력 없음은 성립하지 않는다. 단계에서 예외가 나면 그 계획만 `step_failed`로 끝나고 배율이 1로 돌아오며, 게임에 예외가 남거나 도구가 멈추지 않는다.
- R10. 실행 중에는 `get_plan`의 `step_progress`에 목표 UT, 남은 게임 시간(s, 0 미만이 되지 않음), 지금 배율과 방식, 레일 위 여부, 레일이 막혔으면 그 이유(막히지 않았으면 `null`)가 나온다. 단계가 완료되면 이벤트 `warp_completed`에 단계 번호, 목표 UT, 완료 UT, 둘의 차(s), 쓴 레일 배율의 최대와 물리 배율의 최대(쓰지 않았으면 각각 1), 걸린 실제 시간(s), 단계가 실행되는 동안 레일이 막혔던 이유의 목록(`blocked`, 같은 이유는 한 번만, 막힌 적이 없으면 빈 목록)이 남는다(D008). 실행 중에 계획이 끝나 배율을 되돌리면(되돌리기 전 배율이 1보다 클 때만) 이벤트 `warp_released`에 단계 번호, 종료 사유, 되돌리기 전 배율이 남는다(D007).
- R11. 실제 게임에서 MCP 응답(계측 포함)만으로 다음이 맞는다. 판정에는 계획을 제출하기 전이나 끝난 뒤의 응답만 쓴다(중단 항목에서 실행 중의 값을 보는 것은 예외). "정확히 도착"은 `warp_completed`의 완료 UT − 목표 UT가 0 이상 0.1초 이하이고, 계획이 끝난 뒤 `get_flight`의 `time_warp`가 배율 1·`none`·레일 위 아님인 것이다(D009). 무인 `C33 Loss Test`(docs/features/flight-condition.md R10)로 하며, 궤도 항목은 `debug_set_orbit`으로 Kerbin Ap 120 km·Pe 90 km에 두고, 발사대와 실패 경로는 발사대에서 한다. `debug_set_orbit`을 쓰는 항목은 `get_flight`의 `time_warp`가 레일 위 아님인 것을 본 뒤 계획을 제출한다(D014).
  - 시간: `warp`(`seconds` 600)가 `completed`로 끝나고 정확히 도착한다. 레일 배율 최대가 1보다 크고 물리 배율 최대가 1이며 걸린 실제 시간이 600초보다 짧다. Ap·Pe가 계획 전과 1% 안에서 같다.
  - 시각: `warp`(`ut` = `get_flight`의 UT + 1,000,000)가 정확히 도착하며 레일 배율 최대가 시간 항목의 것보다 크다.
  - 최대 배율: `warp`(`seconds` 600, `max_rate` 30)가 정확히 도착하고 레일 배율 최대가 1보다 크고 30 이하이다.
  - Ap·Pe: `warp`(`to` apoapsis, `lead_s` 30)와 `warp`(`to` periapsis, `lead_s` 30)가 각각 정확히 도착하고, 끝난 뒤 `get_flight`의 Ap(Pe)까지 남은 시간 + (그 응답의 UT − 완료 UT)가 30초와 0.5초 안에서 같다.
  - 이미 지난 목표: `warp`(`ut` 1)가 `completed`로 끝나고 레일·물리 배율 최대가 모두 1이며, 그 단계의 끝 UT − 시작 UT가 0.1초 이하이다.
  - 짧은 목표: `warp`(`seconds` 1)가 정확히 도착한다.
  - 스로틀과 자세: 엔진을 켜지 않고 계획 `throttle` 0.3 → `attitude`(prograde) → `warp`(`seconds` 900) → `wait` 60초 → `measure_start` → `wait` 10초 → `measure_stop`이 `completed`로 끝난다. `warp`의 레일 배율 최대가 1보다 크고, 계측의 스로틀 최소·최대가 모두 0.3(0.001 안)이며, 계측 표본마다 기수 방향과 그 표본의 궤도 속도 방향 사이 각도가 3° 이하이다(계산은 docs/specs/기본-제어/spec.md C11의 식). 그 계획의 `hold_released` 이벤트는 사유 `completed` 하나뿐이다.
  - 발사대: 계획 `throttle` 0.5 → `warp`(`seconds` 600)가 정확히 도착하고 레일 배율 최대가 1보다 크며, 끝난 뒤 스로틀이 0.5(0.001 안)이고 상황이 `PRELAUNCH`로 남으며 해발 고도가 계획 전과 1 m 안에서 같다.
  - 물리 가속: `debug_set_orbit`으로 Kerbin 대기 상한(70 km) 바로 위(Ap 75 km·Pe 71 km)에 둔 기체에서 역추진으로 속도를 줄여 해발 65 km 아래로 내려온 뒤의 `warp`(`seconds` 20, `physics_rate` 4)가 정확히 도착하고, 레일 배율 최대가 1, 물리 배율 최대가 1보다 크고 4 이하이며, 걸린 실제 시간이 20초보다 짧다(D015).
  - 중단: `warp`(`seconds` 1,000,000, `max_rate` 50)를 실행하고 `get_plan`의 `step_progress`가 레일 위를 보일 때까지 읽는다. 그 응답의 `step_progress`는 배율이 1보다 크고 방식이 `rails`이며 막힌 이유가 `null`이고, 목표 UT가 그 단계의 시작 UT + 1,000,000이며, 남은 게임 시간이 목표 UT − 그 응답의 UT와 1초 안에서 같고, 이어서 읽은 `get_flight`의 `time_warp`가 배율 1보다 큼·`rails`·레일 위이다. 그 뒤 `abort_plan`하면 이벤트 `warp_released`가 있고 그 배율이 1보다 크며, 이어서 읽은 `get_flight`의 `time_warp`가 배율 1이고 레일 위 아님이 된다. 같은 방법으로 실행 중에 `debug_revert`(`new_flight`), `debug_space_center`(`scene_change`), Mod 로직 다시 읽기(`reload`)를 한 뒤에도, 그 뒤의 비행 장면에서 배율 1·레일 위 아님이고 `bridge_flybywire_handlers`가 0이다.
  - 시간 초과: `warp`(`seconds` 1,000,000, `max_rate` 50, `timeout_s` 600)가 `timeout`으로 끝나고 이벤트 `warp_released`의 배율이 1보다 크며 `get_flight`의 배율이 1이다.
  - 실패 경로: 발사대에서 `warp`(`to` apoapsis)가 `step_failed`로 끝난다. 목표 없음, 목표 둘, `seconds` 0, 모르는 `to`, `to` 없는 `lead_s`, `physics_rate` 5, `max_rate` 0.5, 모르는 인자를 제출하면 `bad_request`이고 그 뒤 `get_status`가 정상으로 응답한다.

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-22 | 시간-가속 | R1~R11 추가(기준 spec 처음 만듦) | docs/specs/시간-가속/ |

표에 없는 이전 변경: 기록 없음
