# flight-condition

파일: src/KspBridge.Mod.Modules.Condition/KspBridge.Mod.Modules.Condition.csproj, src/KspBridge.Mod.Modules.Condition/ConditionModule.cs, src/KspBridge.Mod.Modules.Condition/ConditionArgs.cs, src/KspBridge.Mod.Modules.Condition/ConditionValues.cs, src/KspBridge.Mod.Modules.Condition/ConditionSet.cs, src/KspBridge.Mod.Modules.Condition/WaitUntilStep.cs, src/KspBridge.Mod.Modules.Condition/NoThrustRule.cs, src/KspBridge.Mod.Modules.Condition/EngineReader.cs, src/KspBridge.Mod.Modules.Condition/LossLog.cs, src/KspBridge.Mod.Modules.Condition/LossGuard.cs, src/KspBridge.Mod.Modules.Condition/NoThrustGuard.cs, src/KspBridge.Server.Modules.Condition/KspBridge.Server.Modules.Condition.csproj, src/KspBridge.Server.Modules.Condition/ConditionModule.cs, tests/KspBridge.Mod.Modules.Condition.Tests/KspBridge.Mod.Modules.Condition.Tests.csproj, tests/KspBridge.Mod.Modules.Condition.Tests/ConditionArgsTests.cs, tests/KspBridge.Mod.Modules.Condition.Tests/ConditionValuesTests.cs, tests/KspBridge.Mod.Modules.Condition.Tests/ConditionSetTests.cs, tests/KspBridge.Mod.Modules.Condition.Tests/NoThrustRuleTests.cs, tests/KspBridge.Mod.Modules.Condition.Tests/LossLogTests.cs, ReadMe.md

상태 조건으로 단계를 끝내는 단계 종류 `wait_until`과, 계획을 스스로 멈추는 안전 중단(기체 파괴·부품 손실·추력 없음). 별도 MCP 도구는 없고 Planner가 `submit_plan`의 단계와 `abort_on`으로 쓴다. 계획 감시(`IPlanGuard`)와 `abort_on`의 검증·표시, 판정 순서는 docs/features/flight-plan.md의 요구이다. 모든 단계에 붙는 공통 종료 조건, Planner가 정하는 조건 중단, 조건의 중첩, Δv 조건, 단계마다 다른 안전 중단 설정, 자동 스테이징, 중단 때 어보트 액션 그룹을 작동하거나 자세를 바꾸는 것은 하지 않는다. 괄호 안 D번호는 docs/specs/조건-종료/decisions.md의 결정이다.

## 용어

- 활성 기체, 지표 기준 자세, 스테이지 번호: docs/features/flight-observation.md의 용어와 같다.
- 조건 값: `wait_until`이 비교하는 활성 기체의 값. R2의 표에 있는 이름만 쓴다.
- 추력 없음: 활성 기체의 스로틀이 0보다 크고, 꺼진 엔진이 하나 이상 있으며, 작동 중인 엔진이 하나도 없는 상태. 꺼진 엔진은 게임의 `ModuleEngines.flameout`이 참인 엔진(점화 상태와 상관없이 센다), 작동 중인 엔진은 `ModuleEngines.isOperational`이 참인 엔진이다. 엔진을 켜기 전, 다 탄 단을 떼어낸 뒤, 스로틀 0에서는 추력 없음이 아니다.
- 부품 폭발: 활성 기체의 부품이 게임에서 파괴되는 것(`GameEvents.onPartWillDie`). 스테이징·도킹 해제로 떨어지는 부품은 해당하지 않는다.
- 구조 파손: 활성 기체의 부품과 그 부모를 잇는 관절이 힘으로 부러지는 것(`GameEvents.onJointBreak`). 분리기·도킹 해제는 해당하지 않는다.
- 부품 손실: 부품 폭발과 구조 파손(D004). 이미 활성 기체에서 떨어진 부품의 폭발·파손은 해당하지 않는다.
- 기체 파괴: 활성 기체가 게임에서 파괴되는 것(활성 기체의 `GameEvents.onVesselWillDestroy`).
- 손실 원인: 게임이 그 부품에 대해 같은 물리 프레임에 알린 파괴 원인(충돌 `crash`·`collision`, 물 충돌 `crash_splashdown`, 과열 `overheat`, 과중력 `over_g`, 과압 `over_pressure`). 알리지 않은 파괴는 `null`이다. 알린 순서와 상관없이 잇는다.

## 요구(완료 조건)

- R1. 조건 종료와 안전 중단이 Mod 모듈과 MCP 서버 모듈 쌍(`condition`)으로 더해지고 핫리로드로 반영된다. 단계 종류 `wait_until`이 `list_step_types`에 나오고, 설명에 조건의 형식, 조건 값 이름 전체와 단위, 비교 방법, 완료·실패 조건이 들어간다(D001·D009).
- R2. `wait_until`은 `params.all` 또는 `params.any` 가운데 하나로 조건 목록(하나 이상)을 받는다. 조건 하나는 `value`(조건 값 이름), `op`(비교), `to`(비교할 값)이다. `all`은 목록의 조건이 모두, `any`는 하나라도 맞으면 맞은 것이다.

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

  `elapsed_s`·`no_thrust`를 뺀 조건 값은 같은 시점 `get_flight` 기본 응답의 같은 이름 값과 같은 계산이다(D010). `all`과 `any`가 둘 다 있거나 둘 다 없거나, 목록이 비었거나, 모르는 조건 값·비교·상황 이름이거나, 조건 값에 맞지 않는 비교이거나, `to`의 종류가 맞지 않거나 빠지면 계획 제출이 `bad_request`이다.
- R3. `wait_until`은 조건이 맞은 물리 프레임에 완료되며, 시작한 물리 프레임에 이미 맞으면 그 프레임에 완료된다. 값이 없는 조건 값(열린 궤도의 `apoapsis_m`·`time_to_apoapsis_s`)의 조건은 맞지 않은 것이다. 조건이 맞지 않으면 계속 기다리고, 단계의 `timeout_s`가 지나면 계획이 `timeout`으로 끝난다. 조건이 맞은 프레임에 `timeout_s`도 지났으면 시간 초과가 먼저이다(D007). 단계는 조종 입력을 넣지 않고 스로틀·자세 유지를 바꾸지 않는다. 활성 기체가 없으면 단계가 실패해 계획이 `step_failed`로 끝난다.
- R4. `wait_until`이 완료되면 이벤트 `condition_met`에 단계 번호와 그 프레임의 조건마다 값과 맞았는지가 남는다. 실행 중에는 `get_plan`의 단계 진행(`step_progress`)에 묶음(`all`/`any`)과 조건마다 지금 값, 맞았는지가 나온다.
- R5. 기체 파괴: 계획이 실행되는 동안 활성 기체가 파괴되면 계획이 `vessel_destroyed`로 끝난다. `abort_on`과 상관없다(D002). 게임이 다른 기체로 자동 전환하는 것보다 먼저 끝난다.
- R6. 부품 손실: `abort_on.part_loss`(생략하면 `true`, D002)가 참인 계획이 실행되는 동안 부품 손실이 일어나면 계획이 `part_loss`로 끝난다. 계획이 실행되는 동안 일어난 부품 손실은 `abort_on.part_loss`와 상관없이 이벤트 `parts_lost`로 남는다. 이벤트는 물리 프레임마다 하나로 묶으며 손실된 부품 수와, 부품마다(최대 10개) 부품 이름, 종류(폭발 `exploded`·구조 파손 `broke_off`), 손실 원인이 들어간다.
- R7. 추력 없음: `abort_on.no_thrust`(생략하면 `false`, D002)가 참인 계획이 실행되는 동안 추력 없음이 되면 계획이 `no_thrust`로 끝난다.
- R8. 한 프레임에 여럿이면 기체 파괴, 부품 손실, 추력 없음 순으로 하나의 사유로 끝난다. 계획의 `end_detail`과 `plan_aborted` 이벤트의 `detail`에 원인(부품 손실·기체 파괴는 손실된 부품과 손실 원인, 추력 없음은 꺼진 엔진 수)이 들어간다. 게임 이벤트를 받는 중에 계획을 끝내거나 기체를 바꾸지 않는다.
- R9. 계획이 실행되지 않을 때는 부품 손실·기체 파괴가 일어나도 이벤트를 남기지 않고 아무것도 바꾸지 않으며, 그 손실은 뒤에 제출된 계획을 끝내지 않는다. 게임 이벤트 처리에서 예외가 나도 게임에 예외가 남지 않고 그 계획만 `step_failed`로 끝난다. 게임 이벤트 구독은 코어의 `Subscriptions`에 등록해 Mod 로직을 다시 읽을 때 모두 풀린다(MCP 응답에 나오지 않으므로 코드로 확인한다, D008).
- R10. 실제 게임에서 MCP 응답(계측 포함)만으로 다음이 맞는다(물리 시간 가속 없이). 판정에는 계획을 제출하기 전이나 끝난 뒤의 응답만 쓴다. 부품 손실과 기체 파괴의 기체는 `C33 Hop Test`이고 나머지(계획 없는 손실 포함)의 기체는 `C33 Loss Test`이다(D015). `C33 Loss Test`는 무인 2단 고체 부스터 기체(위에서부터 탐사선 코어(루트)·반작용 휠·배터리·부스터·분리기·부스터)이고, `C33 Hop Test`는 같은 구성에서 아래 부스터의 연료를 20으로 줄인 기체이다(`C33 Loss Test`는 높이 떠 초속 200 m로 떨어져 첫 충돌 프레임에 루트 부품까지 파괴된다).
  - 고도 조건: 발사대에서 계획 `attitude`(피치 90°·헤딩 90°) → `throttle` 1 → `stage` → `measure_start` → `wait_until`(`all`: `altitude_m` >= 1000) → `measure_stop` → `throttle` 0이 `completed`로 끝난다. 이벤트 `condition_met`의 고도가 1000 m 이상이고, 계측의 고도 끝 값이 1000 m 이상이며 끝 값과 1000 m의 차가 계측 구간의 수직 속도 최대 × 물리 프레임 간격 최대 × 5 이하이다. 계측 구간의 스로틀이 최소·최대 모두 1이다.
  - 이미 맞은 조건과 여러 조건: 발사대에서 `wait_until`(`all`: `situation` == `PRELAUNCH`, `surface_mps` <= 1)이 바로 `completed`로 끝나고 이벤트에 두 조건의 값이 있다. `wait_until`(`any`: `altitude_m` >= 100000, `elapsed_s` >= 5)이 `completed`로 끝나고 그 단계의 끝 UT − 시작 UT가 5초 이상 5.2초 이하이며 이벤트에 맞은 조건이 `elapsed_s`뿐이다.
  - 궤도 조건: `debug_set_orbit`으로 Kerbin Ap 120 km·Pe 90 km에 둔 기체에서 `wait_until`(`all`: `apoapsis_m` >= 115000, `periapsis_m` <= 95000, `inclination_deg` <= 1)이 `completed`로 끝나고 이벤트의 값이 계획 전 `get_flight`의 값과 1% 안에서 같다(경사각은 0.1° 안).
  - 시간 초과: 발사대에서 `wait_until`(`all`: `altitude_m` >= 100000, `timeout_s` 5)이 `timeout`으로 끝난다.
  - 추력 없음 조건과 스테이징: `abort_on`을 생략한 계획 `throttle` 1 → `stage` → `wait_until`(`all`: `no_thrust` == true) → `stage` → `wait` 2초가 `completed`로 끝난다.
  - 추력 없음 중단: `abort_on.no_thrust` 참인 계획 `throttle` 1 → `stage` → `wait` 600초가 `no_thrust`로 끝나고, `get_flight_events`에 `throttle_cut`이 있으며 `get_flight`의 스로틀이 0이다.
  - 분리는 손실이 아님: 궤도에서 `abort_on`을 생략하고 분리가 일어나는 `stage` → `wait` 2초로 끝나는 계획이 `completed`로 끝나고 그 계획의 `parts_lost` 이벤트가 없다.
  - 부품 손실: `abort_on`을 생략한 계획으로 기체를 띄운 뒤 스로틀을 끄고 지면에 떨어뜨리면, 계획의 종료 사유가 `part_loss`이고 이벤트 `parts_lost`에 손실된 부품과 손실 원인이 있다.
  - 기체 파괴: 같은 기체를 `abort_on.part_loss` 거짓인 계획으로 떨어뜨리면 종료 사유가 `vessel_destroyed`이고 그 계획의 `parts_lost` 이벤트가 있다. 그 뒤 `get_status`가 정상으로 응답하고 `bridge_flybywire_handlers`가 0이다.
  - 계획 없는 손실: `C33 Loss Test`를 띄우고 스로틀을 끈 계획이 `completed`로 끝난 뒤 계획 없이 떨어져 파괴되면, 그 계획이 끝난 뒤의 `parts_lost` 이벤트가 없고 `submit_plan`이 `not_ready`이다. `debug_revert`로 되돌린 비행에서 계획 `wait` 5초가 `completed`로 끝난다.
  - 오류: 모르는 조건 값, `all`과 `any`를 함께 준 조건, `situation`에 `>=`, 빈 목록, `abort_on`의 모르는 키를 제출하면 `bad_request`이고 그 뒤 `get_status`가 정상으로 응답한다.

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-22 | 조건-종료 | R1~R10 추가(기준 spec 처음 만듦) | docs/specs/조건-종료/ |

표에 없는 이전 변경: 기록 없음
