# 시간 가속 단계

기능: flight-warp(새 기능) / flight-observation(기준 spec docs/features/flight-observation.md) / flight-control(기준 spec docs/features/flight-control.md)
이슈: GitHub #34

## 무엇을, 왜

계획의 대기는 실시간이다. `wait`는 게임 시간으로 기다리지만 배율을 바꾸지 않고(`src/KspBridge.Mod.Modules.Wait/WaitModule.cs`), 다른 단계도 시간 가속을 쓰지 않는다. 궤도에서 Ap까지 가는 데 수십 분, 다른 천체까지는 며칠이 걸리므로 긴 코스팅이 들어가는 비행(#36·#37·#42)을 실용적으로 실행할 수 없다.

이 변경은 다음을 더한다.

1. 시간 가속 단계(`warp`): 새 Mod 모듈과 MCP 서버 모듈 쌍(`warp`)이 단계 종류 `warp`를 더한다. 목표 시점까지 게임의 시간 가속으로 가고, 목표 전에 배율을 내려 1배에서 완료된다. 조종 입력을 넣지 않는다. 목표는 시간·시각이나 Ap·Pe로 정한다(D001).
2. 레일이 막힌 구간은 1배로 진행하고, 계획에 적었을 때만 물리 가속을 쓴다(D002).
3. 단계가 실행되는 동안 배율은 단계가 정하고(D003), 단계가 어떻게 끝나도 1배로 되돌린다.
4. `get_flight`에 지금 가속 상태를 더한다(D004).

범위 밖: 상태 조건으로 멈추기(D001), 기동 노드 목표(#37), 다른 단계(`wait`·`wait_until` 등)의 자동 가속, 비행 장면 밖의 가속, 영향권 전환 시점을 목표로 하기, 레일이 가능한 구간에서 물리 가속을 고르는 것, 사람의 조작을 감지해 멈추는 것(D003).

## 용어

이 문서에서 "디컴파일"은 게임 설치 폴더의 `KSP_x64_Data/Managed/Assembly-CSharp.dll`(KSP 1.12.5)을 `ilspycmd`로 풀어 읽은 것이고, 행 번호는 그 결과의 행이다.

- 활성 기체: docs/features/flight-observation.md의 용어와 같다.
- 배율: 지금 실제로 적용된 시간 가속 배수(게임의 `TimeWarp.CurrentRate`). 가속이 없으면 1이다.
- 레일 가속: 게임의 높은 시간 가속(`TimeWarp.Modes.HIGH`)에서 배율이 1보다 큰 상태. 기체가 레일 위인지는 "레일 위" 항목을 따른다.
- 물리 가속: 게임의 물리 시간 가속(`TimeWarp.Modes.LOW`)에서 배율이 1보다 큰 상태. 물리 계산을 하며 물리 프레임 하나의 게임 시간이 배율만큼 길어진다(디컴파일 `TimeWarp.updateRate` 1110행).
- 레일 위: 활성 기체가 물리 계산에서 빠진 상태(`Vessel.packed`). 게임은 높은 시간 가속에서 배율이 `TimeWarp.MaxPhysicsRate`보다 크면 기체를 레일에 올리고, 그렇지 않으면 물리 계산으로 되돌린다. 배율이 내려온 뒤 되돌릴 때까지도 레일 위이다(디컴파일 `OrbitPhysicsManager.LateUpdate` 270행). 이 경계값이 1이라는 것은 미확인이다(`TimeWarp.maxPhysicsRate_index`의 값이 코드에 없다).
- 배율 단: 게임이 고를 수 있는 배율 값(레일 `TimeWarp.warpRates`, 물리 `TimeWarp.physicsWarpRates`). 값은 게임 장면 자료에 있고 코드에 없어 미확인이며, 구현은 값을 적어 두지 않고 게임에서 읽는다.
- 레일 상한: 지금 상황에서 게임이 허용하는 가장 높은 레일 배율 단(디컴파일 `TimeWarp.getMaxOnRailsRateIdx` 1440행). 게임 설정(`GameParameters.Flight.CanTimeWarpHigh`)이 막으면 1로 본다. 게임은 다음과 같이 정한다.
  - 불러온 기체 가운데 사다리를 잡은 승무원이 있으면 1이다.
  - 활성 기체가 착지·착수 상태(`Vessel.LandedOrSplashed`)이면 지표 속도가 0.3 m/s를 넘을 때 1이다. 대기·가속·고도는 보지 않는다.
  - 그 밖에는 대기 안이거나 가속 중(`geeForce`가 `TimeWarp.GThreshold`보다 큼)이면 1이고, 다음 프레임에 대기나 지표에 들어가게 될 배율과 고도 제한에 걸리는 배율은 쓰지 못한다. 고도 제한은 게임 설정 `ORBIT_WARP_MAXRATE_MODE`가 `PeAltitude`이고 Pe가 안전 높이에 `ORBIT_WARP_PEMODE_SURFACE_MARGIN`을 더한 것보다 높으면 걸리지 않는다.
  - 게임 설정 `ORBIT_WARP_DOWN_AT_SOI`가 켜져 있고 영향권 전환이 다가오면 낮아진다.
- 레일이 막힌 구간: 레일 상한이 1인 동안.

게임은 레일 가속 중에 기체가 착지·착수 상태가 아니면 레일 상한에 맞춰 배율을 스스로 내리지만 다시 올리지 않는다(디컴파일 `TimeWarp.FixedUpdate` 1037행, `setRate` 1227행). 게임의 자동 가속(`TimeWarp.WarpTo`)은 멈출 때 게임 시각을 강제로 되돌려 맞추므로(디컴파일 `TimeWarp.autoWarpTo` 2430행) 쓰지 않는다.

## 완료 조건

- C1. 시간 가속이 새 Mod 모듈과 MCP 서버 모듈 쌍(`warp`)으로 더해지고 핫리로드로 반영된다. 단계 종류 `warp`가 `list_step_types`에 나오고, 설명에 인자(이름·범위·기본값), 레일이 막힌 구간의 처리, 완료·실패 조건이 들어간다.
- C2. `warp`는 목표로 다음 가운데 하나를 받는다(D001).
  - `params.seconds`: 단계가 시작된 UT부터의 게임 시간(s, 0보다 큼)
  - `params.ut`: 게임 시각(UT, s)
  - `params.to`: `apoapsis` 또는 `periapsis`. 그 지점에 닿기 `params.lead_s`초 전(0 이상, 생략하면 0). `lead_s`는 `to`와 함께만 받는다.

  선택 인자 `params.max_rate`(1 이상의 수)를 주면 그 값을 넘지 않는 가장 높은 배율 단까지만 레일 가속을 쓴다. 생략하면 제한이 없다. 선택 인자 `params.physics_rate`(2 이상 4 이하의 정수)는 레일이 막힌 구간에서 쓸 물리 가속의 상한이며, 생략하면 물리 가속을 쓰지 않는다(D002).
  목표가 없거나 둘 이상이거나, 종류·범위가 맞지 않거나, 모르는 `to`나 모르는 인자이면 계획 제출이 `bad_request`이다.
- C3. 목표 UT는 단계가 시작된 물리 프레임에 한 번 정하고 그 뒤 바꾸지 않는다. `to`의 목표 UT는 그때의 활성 기체 궤도로 정한 다음 Ap·Pe의 UT에서 `lead_s`를 뺀 값이다. 도중에 추력이나 항력으로 궤도가 바뀌어도 따라가지 않는다. Ap·Pe에 닿기 전에 다른 천체의 영향권으로 들어가는지는 보지 않는다.
- C4. 단계가 시작될 때 활성 기체가 없거나, `to`를 쓰는데 활성 기체가 착지·착수·발사 전 상태(게임의 `Vessel.LandedOrSplashed`)이거나, `apoapsis`인데 Ap가 없거나 영향권 밖이거나(이심률 1 이상, 또는 Ap의 반지름이 영향권 반지름 이상), `periapsis`인데 열린 궤도에서 이미 Pe를 지났으면 단계가 실패해 계획이 `step_failed`로 끝난다. 이때도 배율은 C7대로 1이 된다(D007).
- C5. 단계는 레일 상한과 `max_rate` 안에서 남은 시간에 맞춰 레일 배율을 올리고 내린다. 게임이나 사람이 배율을 바꿔도 다시 맞춘다(D003). 시작할 때나 실행 중에 게임의 자동 가속이 켜져 있으면 끄고 이어받는다. 레일이 막힌 구간에서는 1배로 진행하고, `physics_rate`가 있으면 그 값 이하의 물리 배율을 쓴다(게임 설정 `GameParameters.Flight.CanTimeWarpLow`가 막으면 1배). 레일이 다시 가능해지면 스스로 레일 가속으로 돌아간다. 단계가 시작된 뒤 25 물리 프레임 동안은, 이미 레일 위가 아니면 레일 배율을 올리지 않는다(게임이 가속을 최근 20프레임의 평균으로 보아 방금 켠 추력이 아직 드러나지 않으므로. D017).
- C6. 단계는 배율이 1이고 활성 기체가 레일 위가 아닌 상태에서 UT가 목표 UT 이상인 첫 물리 프레임에 완료된다. 배율은 목표 UT 전에 1로 내려 가속 때문에 목표를 지나치지 않는다. 시작할 때 목표 UT가 이미 지났으면 실패하지 않고 같은 조건으로 바로 완료된다(시작할 때 사람이 켜 둔 가속이 있으면 1배로 내린 뒤).
- C7. 단계가 실행되는 동안(시작할 때 실패한 경우 포함) 계획이 어떤 사유로 끝나도(`planner_abort`·`step_failed`·`timeout`·`reload`·`vessel_change`·`scene_change`·`new_flight`와 안전 중단) 배율을 1로 되돌린다. `warp`가 실행 중이 아닐 때는 배율을 바꾸지 않는다. `timeout_s`는 다른 단계와 같이 게임 시간이다.
- C8. `warp`는 조종 입력을 넣지 않고 자세 유지를 바꾸지 않는다. 단계가 완료되면 스로틀이 시작할 때와 같고(다르면 완료되는 물리 프레임에 되돌린다. D005), 자세 유지는 풀리지 않으며 레일에서 내려온 뒤 그 목표로 이어진다(레일 위에서는 게임이 기체의 조종 입력을 처리하지 않는다. 디컴파일 `FlightInputHandler` 2409행). 게임은 레일에 오를 때 기체의 조종 상태를 비워 스로틀이 0이 된다(`Vessel.GoOnRails` 5560행). 내려올 때는 기체에 저장된 조종 상태를 조종 장치로 복사하는데(`Vessel.GoOffRails` 5965행, `FlightInputHandler.ResumeVesselCtrlState` 178행, `Vessel.GetControlState` 4570행), 저장된 조종 상태는 비워진 조종 상태와 같은 객체이므로(조종 그룹 재정의가 없을 때. `Vessel` 3297행) 0이다. 발사 전 상태의 기체는 게임 설정의 발사 전 기본 스로틀로 바꾼다(`Vessel.GoOffRails` 5961행, `FlightInputHandler.SetLaunchCtrlState` 203행). 그래서 레일을 쓴 뒤에는 상태와 상관없이 단계가 스로틀을 되돌려야 한다. 게임에서 스로틀이 실제로 어떻게 남는지는 미확인이다. 단계가 완료되지 못하고 계획이 끝났을 때는 스로틀을 되돌리지 않아 0(발사 전 상태의 기체는 발사 전 기본 스로틀)으로 남을 수 있다(C13, D010. 종료 사유에 따른 스로틀 내림은 docs/features/flight-control.md R3 그대로).
- C9. 안전 중단(docs/features/flight-condition.md)은 가속 중에도 매 물리 프레임에 판정된다. 다만 레일에 오른 뒤 단계가 완료될 때까지는 스로틀이 0(발사 전 상태의 기체는 발사 전 기본 스로틀)이므로(C8), 스로틀이 0인 동안 추력 없음은 성립하지 않는다. 단계에서 예외가 나면 그 계획만 `step_failed`로 끝나고 배율이 1로 돌아오며, 게임에 예외가 남거나 도구가 멈추지 않는다.
- C10. 실행 중에는 `get_plan`의 `step_progress`에 목표 UT, 남은 게임 시간(s, 0 미만이 되지 않음), 지금 배율과 방식, 레일 위 여부, 레일이 막혔으면 그 이유(막히지 않았으면 `null`)가 나온다. 단계가 완료되면 이벤트 `warp_completed`에 단계 번호, 목표 UT, 완료 UT, 둘의 차(s), 쓴 레일 배율의 최대와 물리 배율의 최대(쓰지 않았으면 각각 1), 걸린 실제 시간(s), 단계가 실행되는 동안 레일이 막혔던 이유의 목록(`blocked`, 같은 이유는 한 번만, 막힌 적이 없으면 빈 목록)이 남는다(D008). 실행 중에 계획이 끝나 배율을 되돌리면(되돌리기 전 배율이 1보다 클 때만) 이벤트 `warp_released`에 단계 번호, 종료 사유, 되돌리기 전 배율이 남는다.
- C11. `get_flight` 기본 응답의 `time_warp`에 배율(`rate`), 방식(`mode`: `none`·`rails`·`physics`. 배율이 1이면 `none`), 활성 기체가 레일 위인지(`on_rails`)가 들어간다(D004). 나머지 값은 바뀌지 않는다.
- C12. 실제 게임에서 MCP 응답(계측 포함)만으로 다음이 맞는다. 판정에는 계획을 제출하기 전이나 끝난 뒤의 응답만 쓴다(중단 항목에서 실행 중의 값을 보는 것은 예외). "정확히 도착"은 `warp_completed`의 완료 UT − 목표 UT가 0 이상 0.1초 이하이고, 계획이 끝난 뒤 `get_flight`의 `time_warp`가 배율 1·`none`·레일 위 아님인 것이다. 쓰는 craft는 무인 기체이며 어느 craft인지는 plan에서 정하고, 시험이 끝나면 게임을 되돌린다(항목마다 되돌리지 않아도 된다. D016). `debug_set_orbit`을 쓰는 항목은 `get_flight`의 `time_warp`가 레일 위 아님인 것을 본 뒤 계획을 제출한다(게임이 궤도를 지정한 기체를 잠시 레일 위에 두므로. D014).
  - 시간: `debug_set_orbit`으로 Kerbin Ap 120 km·Pe 90 km에 둔 기체에서 `warp`(`seconds` 600)가 `completed`로 끝나고 정확히 도착한다. 이벤트의 레일 배율 최대가 1보다 크고 물리 배율 최대가 1이며 걸린 실제 시간이 600초보다 짧다. Ap·Pe가 계획 전과 1% 안에서 같다.
  - 시각: 같은 궤도에서 `warp`(`ut` = `get_flight`의 UT + 1,000,000)가 `completed`로 끝나고 정확히 도착하며 레일 배율 최대가 시간 항목의 것보다 크다.
  - 최대 배율: `warp`(`seconds` 600, `max_rate` 30)가 정확히 도착하고 레일 배율 최대가 1보다 크고 30 이하이다.
  - Ap·Pe: 같은 궤도에서 `warp`(`to` apoapsis, `lead_s` 30)와 `warp`(`to` periapsis, `lead_s` 30)가 각각 정확히 도착하고, 끝난 뒤 `get_flight`의 Ap(Pe)까지 남은 시간 + (그 응답의 UT − 완료 UT)가 30초와 0.5초 안에서 같다.
  - 이미 지난 목표: `warp`(`ut` 1)가 `completed`로 끝나고 레일·물리 배율 최대가 모두 1이며, 그 단계의 끝 UT − 시작 UT가 0.1초 이하이다.
  - 짧은 목표: `warp`(`seconds` 1)가 정확히 도착한다.
  - 스로틀과 자세: 같은 궤도에서(엔진을 켜지 않고) 계획 `throttle` 0.3 → `attitude`(prograde) → `warp`(`seconds` 900) → `wait` 60초 → `measure_start` → `wait` 10초 → `measure_stop`이 `completed`로 끝난다. `warp`의 레일 배율 최대가 1보다 크고, 계측의 스로틀 최소·최대가 모두 0.3(0.001 안)이며, 계측 표본마다 기수 방향과 그 표본의 궤도 속도 방향 사이 각도가 3° 이하이다(계산은 docs/specs/기본-제어/spec.md C11의 식). 그 계획의 `hold_released` 이벤트는 사유 `completed` 하나뿐이다.
  - 발사대: 발사대에 서 있는 기체에서 계획 `throttle` 0.5 → `warp`(`seconds` 600)가 정확히 도착하고 레일 배율 최대가 1보다 크며, 끝난 뒤 스로틀이 0.5(0.001 안)이고 상황이 `PRELAUNCH`로 남으며 해발 고도가 계획 전과 1 m 안에서 같다.
  - 물리 가속: `debug_set_orbit`으로 Kerbin 대기 상한(70 km) 바로 위(Ap 75 km·Pe 71 km)에 둔 기체에서 역추진으로 속도를 줄여 해발 65 km 아래로 내려온 뒤의 `warp`(`seconds` 20, `physics_rate` 4)가 정확히 도착하고, 레일 배율 최대가 1, 물리 배율 최대가 1보다 크고 4 이하이며, 걸린 실제 시간이 20초보다 짧다(D015).
  - 중단: `warp`(`seconds` 1,000,000, `max_rate` 50)를 실행하고 `get_plan`의 `step_progress`가 레일 위를 보일 때까지 읽는다. 그 응답의 `step_progress`는 배율이 1보다 크고 방식이 `rails`이며 막힌 이유가 `null`이고, 목표 UT가 그 단계의 시작 UT + 1,000,000이며, 남은 게임 시간이 목표 UT − 그 응답의 UT(시작 UT + 단계 경과 시간)와 1초 안에서 같고, 이어서 읽은 `get_flight`의 `time_warp`가 배율 1보다 큼·`rails`·레일 위이다. 그 뒤 `abort_plan`하면, 이벤트 `warp_released`가 있고 그 배율이 1보다 크며, 이어서 읽은 `get_flight`의 `time_warp`가 배율 1이고 레일 위 아님이 된다(게임이 기체를 물리 계산으로 되돌린 뒤의 응답으로 본다). 같은 방법으로 실행 중에 `debug_revert`(종료 사유 `new_flight`), `debug_space_center`(`scene_change`), Mod 로직 다시 읽기(`reload`)를 한 뒤에도, 그 뒤의 비행 장면에서 `get_flight`의 `time_warp`가 배율 1·레일 위 아님이고 `bridge_flybywire_handlers`가 0이다.
  - 시간 초과: `warp`(`seconds` 1,000,000, `max_rate` 50, `timeout_s` 600)가 `timeout`으로 끝나고 이벤트 `warp_released`의 배율이 1보다 크며 `get_flight`의 배율이 1이다.
  - 실패 경로: 발사대에서 `warp`(`to` apoapsis)가 `step_failed`로 끝난다. 목표 없음, 목표 둘, `seconds` 0, 모르는 `to`, `to` 없는 `lead_s`, `physics_rate` 5, `max_rate` 0.5, 모르는 인자를 제출하면 `bad_request`이고 그 뒤 `get_status`가 정상으로 응답한다.

  사람의 조작(D003), 고도·영향권에 따른 레일 상한, 레일이 막힌 구간을 `physics_rate` 없이 1배로 진행하는 것, 레일이 다시 가능해져 돌아가는 것, 게임 설정이 막은 가속, 가속 중 안전 중단, 열린 궤도의 실패는 게임에서 일부러 만들지 않는다. 배율을 고르는 규칙(`max_rate`를 넘지 않는 가장 높은 단 포함), 막힌 구간의 진행과 돌아가기, 실패 조건, `time_warp`의 방식 `physics`는 단위 테스트로 확인하고, 게임에서의 발생은 확인하지 못한 것으로 남긴다.
- C13. docs/features/flight-control.md R2·R3에 예외를 둔다: `warp`가 레일을 쓰는 동안과 레일에서 내려온 뒤 그 단계가 완료될 때까지는 스로틀이 0(발사 전 상태의 기체는 발사 전 기본 스로틀)일 수 있고, `warp`가 완료되면 시작할 때의 값으로 돌아온다(D006). `warp`가 레일을 쓴 뒤 완료되지 못하고 계획이 끝나면 스로틀은 0(발사 전 상태의 기체는 발사 전 기본 스로틀)으로 남을 수 있다(D010). 그 밖의 flight-control 요구는 바뀌지 않는다.
