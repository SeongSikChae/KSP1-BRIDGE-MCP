# flight-control

파일: src/KspBridge.Mod.Modules.Control/KspBridge.Mod.Modules.Control.csproj, src/KspBridge.Mod.Modules.Control/ControlModule.cs, src/KspBridge.Mod.Modules.Control/ControlArgs.cs, src/KspBridge.Mod.Modules.Control/ThrottleStep.cs, src/KspBridge.Mod.Modules.Control/ThrottleRule.cs, src/KspBridge.Mod.Modules.Control/AttitudeSteps.cs, src/KspBridge.Mod.Modules.Control/AttitudeTarget.cs, src/KspBridge.Mod.Modules.Control/AttitudeLaw.cs, src/KspBridge.Mod.Modules.Control/AttitudeHold.cs, src/KspBridge.Mod.Modules.Control/VesselReader.cs, src/KspBridge.Mod.Modules.Control/StageStep.cs, src/KspBridge.Mod.Modules.Control/StageOutcome.cs, src/KspBridge.Mod.Modules.Control/ActionGroupStep.cs, src/KspBridge.Server.Modules.Control/KspBridge.Server.Modules.Control.csproj, src/KspBridge.Server.Modules.Control/ControlModule.cs, tests/KspBridge.Mod.Modules.Control.Tests/KspBridge.Mod.Modules.Control.Tests.csproj, tests/KspBridge.Mod.Modules.Control.Tests/ControlArgsTests.cs, tests/KspBridge.Mod.Modules.Control.Tests/ThrottleRuleTests.cs, tests/KspBridge.Mod.Modules.Control.Tests/AttitudeTargetTests.cs, tests/KspBridge.Mod.Modules.Control.Tests/AttitudeLawTests.cs, tests/KspBridge.Mod.Modules.Control.Tests/StageOutcomeTests.cs, ReadMe.md

Controller 기본 제어. 스로틀·자세·스테이징·액션 그룹을 계획 단계 종류로 더한다. 별도 MCP 도구는 없고 Planner가 `submit_plan`으로 계획에 넣는다. 계획 범위 hold와 계획 종료 동작은 docs/features/flight-plan.md의 요구이다. 상태 조건으로 단계를 끝내는 것, 시간 가속, 기동 노드, 임의 벡터 방향, 자동 스테이징, 기준 방향에 대한 롤 지정은 하지 않는다. 조종 불가인 기체의 `stage`·`action_group`과 부분 조종인 기체의 단계는 막지 않고, 자세 단계가 완료된 뒤의 자세 유지 중에 조종 불가가 되어도 자세 유지는 풀리지 않는다(D023). 괄호 안 D번호는 docs/specs/기본-제어/decisions.md의 결정이다.

## 용어

- 활성 기체, 지표 기준 자세(피치·헤딩·롤), 마지막으로 작동한 스테이지, 다음 스테이지: docs/features/flight-observation.md의 용어와 같다.
- 자세 유지: 자세 단계가 시작된 뒤 풀릴 때까지 Mod가 활성 기체의 기수를 목표 방향으로 조종하는 상태. 한 번에 하나만 있다.
- 조종 불가: 활성 기체의 `Vessel.IsControllable`이 거짓인 상태(게임의 `Vessel.CurrentControlLevel`이 `NONE`. 부분 조종 `PARTIAL_UNMANNED`·`PARTIAL_MANNED`는 조종 불가가 아님). 이때 게임은 플레이어의 기체 조작 입력을 잠그고 엔진은 스로틀 값을 받지 않는다. 조종 입력(`Vessel.ctrlState`)을 직접 읽는 부품은 Mod의 입력에 계속 움직인다(D023).
- 정렬: 기수 방향과 목표 방향 사이 각도가 허용각 이하이고(롤을 지정했으면 롤 오차도 허용각 이하), 기체 각속도 크기가 정지 기준 이하인 상태. 허용각의 기본값은 2°, 정지 기준의 기본값은 0.5°/s이다(D016).
- 스로틀을 내리는 종료 사유: `planner_abort`·`step_failed`·`timeout`·`reload`(D005·D006).

## 요구(완료 조건)

- R1. 기본 제어가 Mod 모듈과 MCP 서버 모듈 쌍(`control`)으로 더해지고 핫리로드로 반영된다. 단계 종류 `throttle`·`attitude`·`attitude_off`·`stage`·`action_group`이 `list_step_types`에 나오고, 각 설명에 인자(이름·범위·기본값)와 완료·실패 조건이 들어간다.
- R2. `throttle`은 `params.value`(0 이상 1 이하)를 받아 그 단계가 시작된 물리 프레임에 활성 기체의 스로틀을 그 값으로 바꾸고 같은 프레임에 완료된다. 바꾼 값은 뒤 단계(조종 입력을 넣지 않는 `wait` 포함) 동안과 계획이 `completed`로 끝난 뒤에도 그대로 남는다(D001). 단계가 시작될 때 활성 기체가 조종 불가이면 스로틀을 바꾸지 않고 단계가 실패해 계획이 `step_failed`로 끝난다(D022). 값이 없거나 범위를 벗어나거나 수가 아니면 계획 제출이 `bad_request`이다.
- R3. 계획이 스로틀을 내리는 종료 사유로 끝나면, 그 계획에서 스로틀을 바꾼 `throttle` 단계가 하나라도 있었고 종료 시점의 활성 기체가 그 계획을 실행하던 기체와 같을 때 스로틀을 0으로 내리고 이벤트 `throttle_cut`을 남긴다(D005·D006). `completed`·`new_flight`·`scene_change`·`vessel_change`로 끝나거나 스로틀을 바꾼 `throttle` 단계가 없는 계획이면 스로틀을 바꾸지 않는다.
- R4. `attitude`는 목표로 `params.pitch`(−90~90°)와 `params.heading`(0 이상 360 미만°)을 함께 받거나, `params.direction`(`prograde`·`retrograde`·`normal`·`antinormal`·`radial_out`·`radial_in`·`surface_prograde`·`surface_retrograde`) 하나를 받는다. `params.roll`(−180~180°)은 피치·헤딩 목표일 때만 받는다. 허용각(`tolerance_deg`)·정지 기준(`settle_rate_dps`)은 0보다 커야 한다. 목표가 둘 다 있거나 둘 다 없거나, 피치·헤딩 가운데 하나만 있거나, 모르는 방향 이름이거나, 범위를 벗어나거나, `direction`과 `roll`을 함께 주면 계획 제출이 `bad_request`이다.
- R5. `attitude`가 시작되면 그 프레임에 SAS가 켜져 있으면 끄고(D003), 자세 유지를 그 목표로 시작한다(이미 자세 유지 중이면 목표를 바꾼다). 단계는 정렬된 물리 프레임에 완료되며(D002), 정렬되지 않으면 단계의 `timeout_s`로 계획이 `timeout`으로 끝난다. 기준 방향은 기체가 움직이는 동안 매 물리 프레임의 방향을 따르고, 방향을 정할 수 없으면(속도 1 m/s 미만 등) 정렬되지 않은 상태로 회전만 멈추며 기다린다(D014). 실행 중에는 `get_plan`의 단계 진행(`step_progress`)에 목표, 기수 방향의 각도 오차(°, 방향을 정할 수 없으면 `null`), 롤 오차(롤 지정 때만, °), 각속도 크기(°/s), 허용각, 정지 기준이 나온다. 단계가 시작될 때 활성 기체가 없거나 조종 불가이면 SAS와 자세 유지를 바꾸지 않고, 정렬을 기다리는 동안 조종 불가가 되면 그 물리 프레임에, 단계가 실패해 계획이 `step_failed`로 끝난다(D022).
- R6. 자세 유지는 `attitude` 단계가 완료된 뒤에도 같은 계획의 뒤 단계(조종 입력을 넣지 않는 단계 포함) 동안 이어진다. 다른 `attitude` 단계, `attitude_off` 단계, SAS 켜기(R9), 계획 종료(사유와 상관없이) 가운데 먼저 일어난 것으로 풀리고, 풀린 사유가 이벤트 `hold_released`로 남는다. 풀린 뒤 Mod는 피치·요·롤 입력을 넣지 않는다. `attitude_off`는 그 단계가 시작된 프레임에 완료되며, 자세 유지가 없을 때도 실패하지 않는다. 자세 유지가 풀려도 SAS는 꺼진 채로 둔다(D003).
- R7. `get_plan`의 실행 중 계획(`current`)의 `hold`에 지금 유지 중인 자세 목표(목표 종류와 값, 자세 유지가 없으면 `null`)가 나온다. 제출 응답의 계획 요약에도 같은 필드가 들어가며 `null`이다(D011). 끝난 계획(`last`)과 `get_status`의 계획 요약에는 더하지 않는다.
- R8. `stage`는 그 단계가 시작된 물리 프레임에 다음 스테이지를 그 번호로 작동하고(D015) 같은 프레임에 완료된다. 다음 스테이지가 없거나, 게임이 거절했거나(스테이징 쿨다운 등), 그 스테이지의 부품이 하나도 작동하지 않았으면 단계가 실패해 계획이 `step_failed`로 끝난다(D010).
- R9. `action_group`은 `params.group`(`1`~`10`, `gear`, `light`, `brakes`, `abort`, `sas`, `rcs`)과 `params.state`(`on`·`off`·`toggle`)를 받아 그 단계가 시작된 물리 프레임에 그 그룹을 게임의 전환 함수와 같이 바꾸고(그룹 값을 바꾸고 그 그룹에 걸린 부품 동작을 실행함, D013), 바뀐 뒤의 그룹 상태를 이벤트 `action_group_changed`로 남긴 뒤 완료된다. SAS를 켜는 경우(`on`, 또는 꺼져 있을 때 `toggle`) 기체가 SAS를 쓸 수 없으면 그룹을 바꾸지 않고 단계가 실패해 계획이 `step_failed`로 끝나며(D009. 켠 뒤의 상태로 판정하지 않는다. SAS를 쓸 수 없는 조종 가능한 기체의 SAS는 게임이 렌더 프레임에 다시 끈다, D024), 쓸 수 있으면 자세 유지를 먼저 풀고 SAS를 켠다(D004). RCS에는 실패 조건이 없다. 모르는 그룹이나 상태, 빠진 인자는 계획 제출이 `bad_request`이다.
- R10. 계획 실행 중 Mod 로직을 다시 읽으면 다시 읽은 뒤 자세 유지가 없고 이전 로직의 조종 입력 처리기가 남지 않으며(`get_status`의 `bridge_flybywire_handlers` 0), R3대로 스로틀이 0이 된다. 어느 단계든 실행 중 예외가 나면 그 계획만 `step_failed`로 끝나고 게임에 예외가 남거나 도구가 멈추지 않는다.
- R11. 실제 게임에서 MCP 응답(계측 포함)만으로 다음이 맞는다(물리 시간 가속 없이). 판정에는 계획을 제출하기 전이나 끝난 뒤의 응답만 쓴다.
  - 발사: 발사대의 stock `Kerbal X`(스테이징 전)에서 계획 `measure_start` → `attitude`(피치 90°·헤딩 90°) → `throttle` 1 → `stage` → `wait` 10초 → `throttle` 0 → `wait` 2초 → `measure_stop`이 `completed`로 끝난다. 계측에서 `stage` 뒤 `wait` 10초 구간의 스로틀이 최소·최대 모두 1이고 추력 최소가 0보다 크며 피치 최소가 85° 이상이다. 마지막 `wait` 2초 구간의 추력 끝 값이 0이다. 계측의 스테이지 끝 값이 처음 값과 다르다.
  - 기준 방향: `debug_set_orbit`으로 Kerbin 적도 궤도(경사각 0°, Ap 120 km·Pe 90 km)에 둔 `Kerbal X`에서 기준 방향 8개마다 계획 `attitude`(그 방향) → `measure_start` → `wait` 20초 → `measure_stop`이 `completed`로 끝나고, 계측 표본(개수 상한 20)마다 기수 방향과 목표 방향 사이 각도가 3° 이하이다. 목표 방향은 같은 표본의 속도로 계산한다(식은 docs/specs/기본-제어/spec.md C11, D007·D008).
  - 피치·헤딩·롤: 같은 궤도에서 계획 `attitude`(피치 30°·헤딩 45°·롤 0°) → `measure_start` → `wait` 20초 → `measure_stop`이 `completed`로 끝나고, 계측 표본(개수 상한 20)마다 기수 방향과 목표 방향 사이 각도가 3° 이하이고 롤이 목표와 3° 이하로 맞는다.
  - SAS: SAS를 켠 상태에서 `attitude`를 시작한 계획이 끝나면 `get_flight`의 SAS가 꺼져 있다. 계획 `attitude`(prograde) → `action_group`(sas, on) → `wait` 5초가 `completed`로 끝나면 `get_flight_events`에 자세 유지가 SAS로 풀렸다는 이벤트가 있고 `get_flight`의 SAS가 켜져 있다. `action_group`(rcs, toggle)을 두 번 실행한 계획이 `completed`로 끝나고 두 그룹 상태 이벤트의 첫 번째 상태가 계획 전 RCS와 반대, 두 번째 상태가 그와 같으며, 그 뒤 `get_flight`의 RCS가 계획 전과 같다.
  - 스로틀 유지·내림: 계획 `throttle` 0.5가 `completed`로 끝난 뒤 `get_flight`의 스로틀이 0.5이다. 계획 `throttle` 0.3 → `wait` 600초를 `abort_plan`으로 중단한 뒤 `get_flight`의 스로틀이 0이고 `get_flight_events`에 스로틀을 내린 이벤트가 있다.
  - 다시 읽기: 계획 `attitude`(prograde) → `throttle` 0.2 → `wait` 600초 실행 중 Mod 로직을 다시 읽은 뒤 `get_plan`의 마지막 계획 종료 사유가 `reload`, `get_status`의 `bridge_flybywire_handlers`가 0, `get_flight`의 스로틀이 0이다.
  - 실패 경로: 다음 스테이지가 `null`인 기체(`C8 Probe Only`)에서 계획 `stage`가 `step_failed`로 끝난다. SAS를 쓸 수 없는 기체(`C9 No SAS`)에서 계획 `action_group`(sas, on)이 `step_failed`로 끝나고 `get_flight`의 SAS가 꺼져 있다. 범위 밖 스로틀, 목표 없는 `attitude`, 모르는 그룹을 제출하면 `bad_request`이고, 그 뒤 `get_status`가 정상으로 응답한다.

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-22 | 기본-제어 | R1~R11 추가(기준 spec 처음 만듦) | docs/specs/기본-제어/ |

표에 없는 이전 변경: 기록 없음
