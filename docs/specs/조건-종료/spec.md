# 조건 종료 판정과 안전 중단

기능: flight-condition(새 기능) / flight-plan(기준 spec docs/features/flight-plan.md) / flight-control(기준 spec docs/features/flight-control.md)
이슈: GitHub #33

## 무엇을, 왜

지금 계획의 단계는 시간 경과(`wait`), 자세 정렬(`attitude`), 시작 즉시 완료되는 동작으로만 끝난다. "고도 10 km까지", "Ap가 80 km에 이를 때까지"처럼 상태 조건으로 단계를 끝낼 수 없어, 스로틀을 끄거나 스테이지를 작동할 시점을 Planner(LLM)가 MCP 호출로 맞춰야 한다. 또 비행 중에 이상이 생겨도 계획은 스스로 멈추지 않는다. 활성 기체가 파괴되어도 게임은 활성 기체를 곧바로 바꾸지 않는다(디컴파일한 `Vessel.Die`는 활성 기체이면 `FlightGlobals`에서 빼지 않고, `GameEvents.onVesselChange`는 `FlightGlobals.setActiveVessel`에서만 발생한다). 조종 가능한 다른 기체가 불러온 범위에 있으면 게임이 루트 부품의 파괴를 알린 뒤 0.5초 뒤에 그 기체로 바꿔 계획이 `vessel_change`로 끝나지만(디컴파일한 `FlightLogger.checkMissionEnd`·`AttemptVesselSwitch`), 없으면 계획이 파괴된 기체에서 계속 실행된다(`src/KspBridge.Mod.Core/Session/FlightSession.cs`의 `OnVesselChange`가 계획을 끝내는 유일한 기체 판정이다).

이 변경은 두 가지를 더한다.

1. 조건 종료(`wait_until`): 새 Mod 모듈과 MCP 서버 모듈 쌍(`condition`)이 단계 종류 `wait_until`을 더한다. 활성 기체의 값에 대한 비교 조건이 맞은 물리 프레임에 완료되고 조종 입력을 넣지 않는다(D001). 앞 단계가 바꾼 스로틀과 자세 유지는 그대로 이어진다(docs/features/flight-control.md R2·R6).
2. 안전 중단: 계획이 실행되는 동안 다음 이상이 일어나면 실행 중인 단계와 상관없이 계획을 끝낸다.
   - 기체 파괴(`vessel_destroyed`): 늘 켜져 있다(D002).
   - 부품 손실(`part_loss`): 기본으로 켜져 있고 계획에서 끌 수 있다(D002·D004).
   - 추력 없음(`no_thrust`): 계획에 적었을 때만 켜진다(D002).
   - 시간 초과(`timeout`): 이미 있는 단계의 `timeout_s`이며 바꾸지 않는다.

조건이 맞은 값과 중단 원인은 이벤트로 남아 `get_flight_events`로 조회된다.

범위 밖: 모든 단계에 붙는 공통 종료 조건(D001), Planner가 정하는 조건 중단(D003), 조건의 중첩, Δv 조건, 단계마다 다른 안전 중단 설정, 자동 스테이징(#36에서 결정), 중단 때 어보트 액션 그룹을 작동하거나 자세를 바꾸는 것, 시간 가속(#34), 이벤트 대기 도구(#44).

## 용어

- 활성 기체, 지표 기준 자세, 스테이지 번호: docs/features/flight-observation.md의 용어와 같다.
- 조건 값: `wait_until`이 비교하는 활성 기체의 값. C2의 표에 있는 이름만 쓴다.
- 추력 없음: 활성 기체의 스로틀이 0보다 크고, 꺼진 엔진이 하나 이상 있으며, 작동 중인 엔진이 하나도 없는 상태. 꺼진 엔진은 게임이 엔진의 꺼짐 상태(`ModuleEngines.flameout`)를 참으로 둔 엔진, 작동 중인 엔진은 게임의 `ModuleEngines.isOperational`(점화되어 있고 꺼짐이 아님)이 참인 엔진이다. 게임은 추진제가 모자라거나 흡입 공기가 모자라거나 물속에서 작동할 수 없을 때 꺼짐으로 두고, 추진제 요구량이 0이면(스로틀 0) 추진제로는 꺼짐 판정을 하지 않는다(디컴파일한 `ModuleEngines.Flameout`·`RequestPropellant`·`CalculateThrust`·`isOperational`). 꺼진 엔진은 점화 상태(`EngineIgnited`)와 상관없이 꺼짐 상태로 센다(stock 고체 부스터는 끌 수 없어 다 타도 점화 상태가 남는다. 디컴파일한 `ModuleEngines.Shutdown`, 게임 폴더 부품 설정의 `allowShutdown = False`). 그래서 엔진을 켜기 전, 다 탄 단을 떼어낸 뒤, 스로틀 0에서는 추력 없음이 아니다.
- 부품 폭발: 활성 기체의 부품이 게임에서 파괴되는 것(`GameEvents.onPartWillDie`). 스테이징·도킹 해제로 떨어지는 부품은 해당하지 않는다(디컴파일: 부품의 `Part.Die`를 부르는 곳은 `Part.explode`, 물 충돌(`PartBuoyancy`), 부모가 파괴될 때의 물리 없는 자식(`Part.Die`), 파괴된 채 저장된 부품을 불러올 때(`Part.Start`), 궤도 지정 치트가 발사 고정대를 없앨 때(`Vessel.KillPermanentGroundContact`)뿐이다. `debug_set_orbit`은 계획 실행 중에는 거절되므로(`src/KspBridge.Mod.Modules.Debug/DebugModule.cs:52`) 마지막 경우는 플레이어가 치트 메뉴를 쓸 때만 생긴다).
- 구조 파손: 활성 기체의 부품과 그 부모를 잇는 관절이 힘으로 부러지는 것(`GameEvents.onJointBreak`). 분리기·도킹 해제는 힘 0으로 관절을 없애 해당하지 않는다(디컴파일한 `Part.OnPartJointBreak`·`Part.decouple`·`PartJoint.DestroyJoint`).
- 부품 손실: 부품 폭발과 구조 파손(D004). 이미 활성 기체에서 떨어진 부품의 폭발·파손은 해당하지 않는다.
- 기체 파괴: 활성 기체가 게임에서 파괴되는 것(활성 기체의 `GameEvents.onVesselWillDestroy`). 루트 부품이 파괴되면 일어난다(디컴파일한 `Part.Die`·`Vessel.Die`).
- 손실 원인: 게임이 그 부품에 대해 같은 물리 프레임에 알린 파괴 원인(충돌 `crash`·`collision`, 물 충돌 `crash_splashdown`, 과열 `overheat`, 과중력 `over_g`, 과압 `over_pressure`). 알리지 않은 파괴(예: 부모가 파괴될 때 함께 파괴된 물리 없는 자식)는 `null`이다. 게임은 경로에 따라 파괴보다 먼저(과열·과중력·과압, 물 충돌, 지형 관통, `CollisionEnhancer`의 충돌) 또는 뒤에(`Part.HandleCollision`의 충돌) 원인을 알리므로, 알린 순서와 상관없이 잇는다(디컴파일한 `Part`의 과열·과중력·과압 처리와 `HandleCollision`, `PartBuoyancy`, `Vessel`의 지형 관통 처리, `CollisionEnhancer`).

## 완료 조건

- C1. 조건 종료가 새 Mod 모듈과 MCP 서버 모듈 쌍(`condition`)으로 더해지고 핫리로드로 반영된다. 단계 종류 `wait_until`이 `list_step_types`에 나오고, 설명에 조건의 형식, 조건 값 이름 전체와 단위, 비교 방법, 완료·실패 조건이 들어간다.
- C2. `wait_until`은 `params.all` 또는 `params.any` 가운데 하나로 조건 목록(하나 이상)을 받는다. 조건 하나는 `value`(조건 값 이름), `op`(비교), `to`(비교할 값)이다. `all`은 목록의 조건이 모두, `any`는 하나라도 맞으면 맞은 것이다.

  | 조건 값 | 뜻(단위) | 비교 |
  |---|---|---|
  | `altitude_m`, `terrain_altitude_m` | 해발 고도, 지형(바다 위는 수면) 위 고도(m) | `>=`, `<=` |
  | `surface_mps`, `orbital_mps`, `vertical_mps`, `horizontal_surface_mps` | 지표·궤도·수직(위가 +)·수평 지표 속도(m/s) | `>=`, `<=` |
  | `apoapsis_m`, `periapsis_m` | Ap·Pe 고도(해발, m) | `>=`, `<=` |
  | `time_to_apoapsis_s`, `time_to_periapsis_s` | Ap·Pe까지 남은 시간(s) | `>=`, `<=` |
  | `inclination_deg`, `eccentricity` | 경사각(°), 이심률 | `>=`, `<=` |
  | `pitch_deg`, `heading_deg`, `roll_deg` | 지표 기준 자세(°) | `>=`, `<=` |
  | `mass_t`, `thrust_kn`, `twr` | 기체 질량(t), 현재 추력 합(kN), 현재 TWR | `>=`, `<=` |
  | `elapsed_s` | 이 단계가 시작된 뒤 지난 게임 시간(s) | `>=`, `<=` |
  | `situation` | 상황(게임의 `Vessel.Situations` 이름) | `==` |
  | `no_thrust` | 추력 없음(용어 절, `true`/`false`) | `==` |

  `elapsed_s`·`no_thrust`를 뺀 조건 값은 같은 시점 `get_flight` 기본 응답의 같은 이름 값과 같은 계산이다. `all`과 `any`가 둘 다 있거나 둘 다 없거나, 목록이 비었거나, 모르는 조건 값·비교·상황 이름이거나, 조건 값에 맞지 않는 비교이거나, `to`의 종류가 맞지 않거나 빠지면 계획 제출이 `bad_request`이다.
- C3. `wait_until`은 조건이 맞은 물리 프레임에 완료되며, 시작한 물리 프레임에 이미 맞으면 그 프레임에 완료된다. 값이 없는 조건 값(열린 궤도의 `apoapsis_m`·`time_to_apoapsis_s`)의 조건은 맞지 않은 것이다. 조건이 맞지 않으면 계속 기다리고, 단계의 `timeout_s`가 지나면 계획이 `timeout`으로 끝난다. 조건이 맞은 프레임에 단계의 `timeout_s`도 지났으면 지금처럼 시간 초과가 먼저여서 `timeout`으로 끝난다(D007). 단계는 조종 입력을 넣지 않고 스로틀·자세 유지를 바꾸지 않는다. 활성 기체가 없으면 단계가 실패해 계획이 `step_failed`로 끝난다.
- C4. `wait_until`이 완료되면 이벤트 `condition_met`에 단계 번호와 그 프레임의 조건마다 값과 맞았는지가 남는다. 실행 중에는 `get_plan`의 단계 진행(`step_progress`)에 묶음(`all`/`any`)과 조건마다 지금 값, 맞았는지가 나온다.
- C5. `submit_plan`의 계획은 선택 필드 `abort_on`을 받는다. `part_loss`(생략하면 `true`)와 `no_thrust`(생략하면 `false`) 두 키만 있고 값은 참·거짓이다. 모르는 키이거나 참·거짓이 아니면 `bad_request`이다. `submit_wait`의 계획은 생략한 것과 같다. 적용된 `abort_on`이 계획 요약(`get_plan`의 `current`·`last`, 제출 응답, `get_status`)에 나온다.
- C6. 기체 파괴: 계획이 실행되는 동안 활성 기체가 파괴되면 계획이 `vessel_destroyed`로 끝난다. `abort_on`과 상관없다. 게임이 다른 기체로 자동 전환하는 것보다 먼저 끝난다. 활성 기체가 이미 파괴된 상태이면 계획 제출(`submit_plan`·`submit_wait`)이 이유와 함께 `not_ready`이다(D006). `get_status`·`debug_revert` 등 다른 도구는 그대로이다.
- C7. 부품 손실: `abort_on.part_loss`가 참인 계획이 실행되는 동안 부품 손실이 일어나면 계획이 `part_loss`로 끝난다. 계획이 실행되는 동안 일어난 부품 손실은 `abort_on.part_loss`와 상관없이 이벤트 `parts_lost`로 남는다. 이벤트는 물리 프레임마다 하나로 묶으며 손실된 부품 수와, 부품마다(최대 10개) 부품 이름, 종류(폭발 `exploded`·구조 파손 `broke_off`), 손실 원인이 들어간다.
- C8. 추력 없음: `abort_on.no_thrust`가 참인 계획이 실행되는 동안 추력 없음이 되면 계획이 `no_thrust`로 끝난다.
- C9. 안전 중단은 그 이상이 일어난 물리 프레임이나 그다음 물리 프레임에, 실행 중 단계의 시간 초과·완료·실패 판정보다 먼저 판정한다. 그래서 마지막 단계가 완료될 프레임이어도 안전 중단으로 끝나고, 아직 시작되지 않은 단계는 시작하지 않는다. 계획을 `completed`로 끝내기 직전에도 그때까지 일어난 이상을 판정한다. 그 밖의 사유로 끝날 때는 아직 판정하지 않은 부품 손실을 이벤트 `parts_lost`로 남기고 종료 사유는 바꾸지 않는다. 계획이 끝난 뒤의 이상은 그 계획의 것이 아니다(D005). 한 프레임에 여럿이면 기체 파괴, 부품 손실, 추력 없음 순으로 하나의 사유로 끝난다. 계획의 `end_detail`과 `plan_aborted` 이벤트의 `detail`에 원인(부품 손실·기체 파괴는 손실된 부품과 손실 원인, 추력 없음은 꺼진 엔진 수)이 들어간다. 게임 이벤트를 받는 중에 계획을 끝내거나 기체를 바꾸지 않는다.
- C10. 계획이 `part_loss`·`no_thrust`로 끝나면 `step_failed`와 같이 스로틀을 내린다(docs/features/flight-control.md R3: 그 계획에서 스로틀을 바꾼 `throttle` 단계가 있었고 종료 시점의 활성 기체가 같을 때 스로틀 0과 이벤트 `throttle_cut`). `vessel_destroyed`로 끝나면 스로틀을 바꾸지 않는다. 자세 유지는 지금처럼 모든 종료 사유로 풀린다.
- C11. 계획이 실행되지 않을 때는 부품 손실·기체 파괴가 일어나도 이벤트를 남기지 않고 아무것도 바꾸지 않으며, 그 손실은 뒤에 제출된 계획을 끝내지 않는다. 안전 중단의 판정이나 게임 이벤트 처리에서 예외가 나도 게임에 예외가 남거나 도구·계획이 멈추지 않으며, 판정이 예외로 실패하면 그 계획만 `step_failed`로 끝난다. 게임 이벤트 구독은 코어의 `Subscriptions`에 등록해 Mod 로직을 다시 읽을 때 모두 풀린다. 이것은 MCP 응답에 나오지 않으므로 구현 후 검토에서 코드로 확인한다(D008).
- C12. 실제 게임에서 MCP 응답(계측 포함)만으로 다음이 맞는다(물리 시간 가속 없이). 판정에는 계획을 제출하기 전이나 끝난 뒤의 응답만 쓴다. 쓰는 craft는 무인 기체이며 어느 craft인지는 plan에서 정하고, 판정 뒤 `debug_revert`로 되돌린다.
  - 고도 조건: 발사대에서 계획 `attitude`(피치 90°·헤딩 90°) → `throttle` 1 → `stage` → `measure_start` → `wait_until`(`all`: `altitude_m` >= 1000) → `measure_stop` → `throttle` 0이 `completed`로 끝난다. 이벤트 `condition_met`의 고도가 1000 m 이상이고, 계측의 고도 끝 값이 1000 m 이상이며 끝 값과 1000 m의 차가 계측 구간의 수직 속도 최대 × 물리 프레임 간격 최대 × 5 이하이다. 계측 구간의 스로틀이 최소·최대 모두 1이다.
  - 이미 맞은 조건과 여러 조건: 발사대에서 `wait_until`(`all`: `situation` == `PRELAUNCH`, `surface_mps` <= 1)이 바로 `completed`로 끝나고 이벤트 `condition_met`에 두 조건의 값이 있다. `wait_until`(`any`: `altitude_m` >= 100000, `elapsed_s` >= 5)이 `completed`로 끝나고 그 단계의 끝 UT − 시작 UT가 5초 이상 5.2초 이하이며 이벤트에 맞은 조건이 `elapsed_s`뿐이다.
  - 궤도 조건: `debug_set_orbit`으로 Kerbin Ap 120 km·Pe 90 km에 둔 기체에서 `wait_until`(`all`: `apoapsis_m` >= 115000, `periapsis_m` <= 95000, `inclination_deg` <= 1)이 `completed`로 끝나고 이벤트의 값이 계획 전 `get_flight`의 값과 1% 안에서 같다(경사각은 0.1° 안).
  - 시간 초과: 발사대에서 `wait_until`(`all`: `altitude_m` >= 100000, `timeout_s` 5)이 `timeout`으로 끝난다.
  - 추력 없음 조건과 스테이징: 다단 기체에서 `abort_on`을 생략한 계획 `throttle` 1 → `stage` → `wait_until`(`all`: `no_thrust` == true) → `stage` → `wait` 2초가 `completed`로 끝난다(단이 다 타고 분리되어도 중단되지 않음).
  - 추력 없음 중단: 같은 기체에서 `abort_on.no_thrust` 참인 계획 `throttle` 1 → `stage` → `wait` 600초가 `no_thrust`로 끝나고, `get_flight_events`에 `throttle_cut`이 있으며 `get_flight`의 스로틀이 0이다.
  - 분리는 손실이 아님: 궤도에 둔 다단 기체에서 `abort_on`을 생략하고 분리가 일어나는 `stage` → `wait` 2초로 끝나는 계획이 `completed`로 끝나고 그 계획의 `parts_lost` 이벤트가 없다.
  - 부품 손실: `abort_on`을 생략한 계획으로 기체를 띄운 뒤 스로틀을 끄고 지면에 떨어뜨리면, 계획의 종료 사유가 `part_loss`이고 이벤트 `parts_lost`에 손실된 부품과 손실 원인이 있다. 첫 충돌에서 루트 부품까지 파괴되는 경우를 피하도록 craft를 정한다.
  - 기체 파괴: 같은 기체를 `abort_on.part_loss` 거짓인 계획으로 떨어뜨리면 종료 사유가 `vessel_destroyed`이고 그 계획의 `parts_lost` 이벤트가 있다. 그 뒤 `get_status`가 정상으로 응답하고 `bridge_flybywire_handlers`가 0이다.
  - 계획 없는 손실: 같은 기체를 띄우고 스로틀을 끈 계획이 `completed`로 끝난 뒤 계획 없이 떨어져 파괴되면, 그 계획이 끝난 뒤의 `parts_lost` 이벤트가 없고 `submit_plan`이 `not_ready`이다. `debug_revert`로 되돌린 비행에서 계획 `wait` 5초가 `completed`로 끝난다. 기체가 일부만 부서진 뒤에 제출한 계획이 끝나지 않는 것은 단위 테스트로 확인한다.
  - 오류: 모르는 조건 값, `all`과 `any`를 함께 준 조건, `situation`에 `>=`, 빈 목록, `abort_on`의 모르는 키를 제출하면 `bad_request`이고 그 뒤 `get_status`가 정상으로 응답한다.

  구조 파손과 과열·과중력·과압 원인은 게임에서 일부러 만들지 않는다. 종류와 원인을 정하는 규칙은 단위 테스트로 확인하고, 게임에서의 발생은 확인하지 못한 것으로 남긴다.
