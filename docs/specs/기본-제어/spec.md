# Controller 기본 제어

기능: flight-control(새 기능) / flight-plan(기준 spec docs/features/flight-plan.md)
이슈: GitHub #32

## 무엇을, 왜

골격의 Controller는 계획이 실행되는 동안 활성 기체의 조종 입력 통로(`OnFlyByWire`)에 붙고 떨어지기만 하고, 실제로 조종 입력을 넣는 단계 종류는 없다(`src/KspBridge.Mod.Core/Control/Controller.cs`의 입력 통로, 등록된 단계 종류는 `src/KspBridge.Mod.Modules.Wait/WaitModule.cs:18`의 `wait`와 `src/KspBridge.Mod.Modules.Measure/MeasureModule.cs:28-29`의 `measure_start`·`measure_stop`뿐). 상승(#36)·기동 노드(#37)·귀환(#42) 같은 비행 기능은 스로틀·자세·스테이징·액션 그룹을 바탕으로 하므로, 이 기본 제어를 계획 단계 종류로 먼저 만든다.

새 Mod 모듈과 MCP 서버 모듈 쌍(`control`)이 다음 단계 종류를 더한다. Planner(LLM)는 별도 MCP 도구 없이 `submit_plan`으로 이 단계들을 계획에 넣는다.

1. 스로틀(`throttle`): 스로틀을 0~1 값으로 바꾼다. 플레이어가 스로틀을 바꾼 것처럼 게임의 스로틀 값을 바꾸므로, 뒤 단계 동안과 계획이 끝난 뒤에도 그 값이 남는다(D001). 다만 계획이 스로틀을 내리는 종료 사유(용어 절)로 끝나면 그 계획에서 스로틀을 바꾼 스로틀 단계가 있었을 때 스로틀을 0으로 내린다(D005·D006).
2. 자세(`attitude`): 기수를 목표 방향으로 돌려 유지한다. 목표는 지표 기준 피치·헤딩(롤은 선택) 또는 기준 방향(궤도 기준 프로그레이드·레트로그레이드·노멀·안티노멀·래디얼 아웃·래디얼 인, 지표 속도 기준 프로그레이드·레트로그레이드)이다. 기준 방향은 기체가 움직이는 동안 매 물리 프레임의 방향을 따른다. 단계는 정렬되면 완료되고(D002), 그 뒤에도 같은 계획의 뒤 단계 동안 자세를 계속 유지한다(D001). 자세 제어가 시작될 때 SAS를 끄고 Mod가 직접 조종한다(D003).
3. 자세 해제(`attitude_off`): 유지 중인 자세 제어를 푼다.
4. 스테이징(`stage`): 다음 스테이지를 작동한다.
5. 액션 그룹(`action_group`): 사용자 액션 그룹 1~10과 기어·조명·브레이크·어보트, SAS·RCS를 켬·끔·전환한다. 자세 유지 중에 SAS를 켜면 자세 유지가 풀리고 SAS가 켜진다(D004).

자세 유지는 다른 자세 단계(목표를 바꿈), 자세 해제 단계, SAS 켜기(D004), 계획 종료(사유와 상관없이) 가운데 먼저 일어난 것으로 풀린다. 풀린 뒤에는 Mod가 피치·요·롤 입력을 넣지 않는다. 계획 도구(`get_plan`)에서 지금 유지 중인 자세 목표를 볼 수 있어 Planner가 계획 상태를 알 수 있다.

범위 밖: 상태 조건으로 단계를 끝내는 것과 그 밖의 안전 중단(#33. 자세 단계가 완료된 뒤의 자세 유지 중에 조종 불가가 되는 경우 포함. 이때 자세 유지는 풀리지 않고 Mod가 계속 조종 입력을 넣는다), 시간 가속(#34), 상승·원궤도화(#36), 기동 노드(#37), 이벤트 대기(#44), 조종 불가인 기체의 `stage`·`action_group`과 부분 조종인 기체의 단계를 막는 것(D023), 임의 벡터로 주는 방향(피치·헤딩으로 대신함), 자동 스테이징, 기준 방향에 대한 롤 지정, 계측 기능의 미확인 동작(조종 입력의 프레임 순서·각속도 부호, docs/specs/계측/plan.md "오류·위험" 1·3) 확인(이번 게임 검증에서 드러나면 계측 기능의 결함으로 따로 다룬다).

## 용어

- 활성 기체, 지표 기준 자세(피치·헤딩·롤), 마지막으로 작동한 스테이지, 다음 스테이지: docs/features/flight-observation.md의 용어와 같다.
- 자세 유지: 자세 단계가 시작된 뒤 풀릴 때까지 Mod가 활성 기체의 기수를 목표 방향으로 조종하는 상태. 한 번에 하나만 있다.
- 조종 불가: 활성 기체의 `Vessel.IsControllable`이 거짓인 상태(게임의 `Vessel.CurrentControlLevel`이 `NONE`. 부분 조종 `PARTIAL_UNMANNED`·`PARTIAL_MANNED`는 조종 불가가 아님). 이때 게임은 플레이어의 기체 조작 입력을 잠그고(`Vessel.CheckControllable`이 `ALL_SHIP_CONTROLS`를 잠금), 엔진은 스로틀 값을 받지 않는다(`ModuleEngines`). 조종 입력(`Vessel.ctrlState`)을 직접 읽는 부품은 Mod의 입력에 계속 움직인다(`ModuleReactionWheel`). 모두 디컴파일로 확인했고, 리액션 휠이 움직이는 것은 게임에서도 확인했다(2026-11-22, 조종 부품 없이 배터리와 리액션 휠만 있는 기체의 `attitude`가 정렬됨).
- 정렬: 기수 방향과 목표 방향 사이 각도가 허용각 이하이고(롤을 지정했으면 롤 오차도 허용각 이하), 기체 각속도 크기가 정지 기준 이하인 상태. 허용각과 정지 기준은 단계 인자로 받으며 생략하면 기본값을 쓴다.
- 스로틀을 내리는 종료 사유: `planner_abort`·`step_failed`·`timeout`·`reload`. `completed`와, 기체가 없거나 바뀌는 `new_flight`·`scene_change`·`vessel_change`는 해당하지 않는다(D005·D006).

## 완료 조건

- C1. 기본 제어가 새 Mod 모듈과 MCP 서버 모듈 쌍(`control`)으로 더해지고 핫리로드로 반영된다. 단계 종류 `throttle`·`attitude`·`attitude_off`·`stage`·`action_group`이 `list_step_types`에 나오고, 각 설명에 인자(이름·범위·기본값)와 완료·실패 조건이 들어간다.
- C2. `throttle`은 `params.value`(0 이상 1 이하)를 받아 그 단계가 시작된 물리 프레임에 활성 기체의 스로틀을 그 값으로 바꾸고 같은 프레임에 완료된다. 바꾼 값은 뒤 단계(조종 입력을 넣지 않는 `wait` 포함) 동안과 계획이 `completed`로 끝난 뒤에도 그대로 남는다. 단계가 시작될 때 활성 기체가 조종 불가이면 스로틀을 바꾸지 않고 단계가 실패해 계획이 `step_failed`로 끝난다(D022). 값이 없거나 범위를 벗어나거나 수가 아니면 계획 제출이 `bad_request`이다.
- C3. 계획이 스로틀을 내리는 종료 사유(`planner_abort`·`step_failed`·`timeout`·`reload`)로 끝나면, 그 계획에서 스로틀을 바꾼 `throttle` 단계가 하나라도 있었고 종료 시점의 활성 기체가 그 계획을 실행하던 기체와 같을 때 스로틀을 0으로 내리고 그 사실을 이벤트로 남긴다(D005·D006). 계획이 `completed`·`new_flight`·`scene_change`·`vessel_change`로 끝나거나 스로틀을 바꾼 `throttle` 단계가 없는 계획이면 스로틀을 바꾸지 않는다.
- C4. `attitude`는 목표로 `params.pitch`(−90~90°)와 `params.heading`(0 이상 360 미만°)을 함께 받거나, `params.direction`(`prograde`·`retrograde`·`normal`·`antinormal`·`radial_out`·`radial_in`·`surface_prograde`·`surface_retrograde`) 하나를 받는다. `params.roll`(−180~180°)은 피치·헤딩 목표일 때만 받는다. 허용각·정지 기준 인자는 0보다 커야 한다. 목표가 둘 다 있거나 둘 다 없거나, 피치·헤딩 가운데 하나만 있거나, 모르는 방향 이름이거나, 범위를 벗어나거나, `direction`과 `roll`을 함께 주면 계획 제출이 `bad_request`이다.
- C5. `attitude`가 시작되면 그 프레임에 SAS가 켜져 있으면 끄고(D003), 자세 유지를 그 목표로 시작한다(이미 자세 유지 중이면 목표를 바꾼다). 단계는 정렬된 물리 프레임에 완료되며, 정렬되지 않으면 단계의 `timeout_s`로 계획이 `timeout`으로 끝난다. 실행 중에는 `get_plan`의 단계 진행(`step_progress`)에 목표와 기수 방향의 각도 오차(°), 롤 오차(롤 지정 때만, °), 각속도 크기(°/s), 허용각, 정지 기준이 나온다. 단계가 시작될 때 활성 기체가 없거나 조종 불가이면 SAS와 자세 유지를 바꾸지 않고, 정렬을 기다리는 동안 조종 불가가 되면 그 물리 프레임에, 단계가 실패해 계획이 `step_failed`로 끝난다(D022).
- C6. 자세 유지는 `attitude` 단계가 완료된 뒤에도 같은 계획의 뒤 단계(조종 입력을 넣지 않는 단계 포함) 동안 이어진다. 다른 `attitude` 단계, `attitude_off` 단계, SAS 켜기(C9), 계획 종료(사유와 상관없이) 가운데 먼저 일어난 것으로 풀리고, 풀린 사유가 이벤트로 남는다. 풀린 뒤 Mod는 피치·요·롤 입력을 넣지 않는다. `attitude_off`는 그 단계가 시작된 프레임에 완료되며, 자세 유지가 없을 때도 실패하지 않는다. 자세 유지가 풀려도 SAS는 꺼진 채로 둔다(D003).
- C7. `get_plan`의 실행 중 계획(`current`)에 지금 유지 중인 자세 목표(목표 종류와 값, 자세 유지가 없으면 `null`)가 나온다. `submit_plan`·`submit_wait` 응답의 계획 요약에도 같은 필드가 들어가며 제출 직후라 `null`이다(docs/features/flight-plan.md R5의 같은 요약). 끝난 계획(`last`)과 `get_status`의 계획 요약에는 더하지 않는다.
- C8. `stage`는 그 단계가 시작된 물리 프레임에 다음 스테이지를 작동하고 같은 프레임에 완료된다. 다음 스테이지(용어 절)가 없으면 단계가 실패해 계획이 `step_failed`로 끝난다. 게임이 실제로 스테이지를 작동하지 않았으면(스테이징 쿨다운처럼 게임이 조용히 거절하는 경우 포함, 디컴파일한 게임 코드 `StageManager.ActivateStage`의 거절 분기 확인) 완료로 보지 않고 단계가 실패해 계획이 `step_failed`로 끝난다(D010).
- C9. `action_group`은 `params.group`(`1`~`10`, `gear`, `light`, `brakes`, `abort`, `sas`, `rcs`)과 `params.state`(`on`·`off`·`toggle`)를 받아 그 단계가 시작된 물리 프레임에 그 그룹을 게임의 전환 함수(`ActionGroupList.SetGroup`·`ToggleGroup`)와 같이 바꾸고(그룹 값을 바꾸고 그 그룹에 걸린 부품 동작을 실행함, 디컴파일 확인, D013), 바뀐 뒤의 그룹 상태를 이벤트로 남긴 뒤 완료된다. SAS를 켜는 경우(`on`, 또는 꺼져 있을 때 `toggle`) 자세 유지 중이면 자세 유지를 먼저 풀고 SAS를 켠다(D004). SAS를 켜려 할 때 기체가 SAS를 쓸 수 없으면(게임의 `VesselAutopilot.VesselSAS.CanEngageSAS`와 같은 판정) 그룹을 바꾸지 않고 단계가 실패해 계획이 `step_failed`로 끝난다(D009). 게임의 전환 함수는 SAS를 쓸 수 있는지 판정하지 않고(디컴파일 확인, `ActionGroupList.ToggleGroup`) SAS를 쓸 수 없는(조종 가능한) 기체의 SAS는 렌더 프레임에 `VesselAutopilot.Update`가 다시 끄므로(디컴파일 확인), 켠 뒤의 상태로 판정하지 않는다. RCS에는 실패 조건이 없다. 모르는 그룹이나 상태, 빠진 인자는 계획 제출이 `bad_request`이다.
- C10. 계획 실행 중 Mod 로직을 다시 읽으면(계획 종료 사유 `reload`) 다시 읽은 뒤 자세 유지가 없고 이전 로직의 조종 입력 처리기가 남지 않으며(`get_status`의 `bridge_flybywire_handlers` 0), C3대로 스로틀이 0이 된다. 어느 단계든 실행 중 예외가 나면 그 계획만 `step_failed`로 끝나고 게임에 예외가 남거나 도구가 멈추지 않는다.
- C11. 실제 게임에서 MCP 응답(계측 포함)만으로 다음이 맞는다(물리 시간 가속 없이). 판정에는 계획을 제출하기 전이나 끝난 뒤의 응답만 쓴다.
  - 발사: 발사대의 stock `Kerbal X`(스테이징 전)에서 계획 `measure_start` → `attitude`(피치 90°·헤딩 90°) → `throttle` 1 → `stage` → `wait` 10초 → `throttle` 0 → `wait` 2초 → `measure_stop`이 `completed`로 끝난다. 계측에서 `stage` 뒤 `wait` 10초 구간의 스로틀이 최소·최대 모두 1이고 추력 최소가 0보다 크며 피치 최소가 85° 이상이다. 마지막 `wait` 2초 구간의 추력 끝 값이 0이다. 계측의 스테이지 끝 값이 처음 값과 다르다. 판정 뒤 `debug_revert`로 되돌린다.
  - 기준 방향: `debug_set_orbit`으로 Kerbin 적도 궤도(경사각 0°, Ap 120 km·Pe 90 km)에 둔 `Kerbal X`에서 기준 방향 8개마다 계획 `attitude`(그 방향, `timeout_s` 있음) → `measure_start` → `wait` 20초 → `measure_stop`이 `completed`로 끝난다. 그 계측을 표본 개수 상한 20으로 조회해, 표본마다 피치·헤딩으로 만든 기수 방향과 목표 방향 사이 각도가 3° 이하이다(D007·D008, 자세가 뒤 단계 동안 유지됨). 목표 방향은 같은 표본의 속도로 계산한다. γ = atan2(수직 속도, √(궤도 속도² − 수직 속도²)), σ = atan2(수직 속도, √(지표 속도² − 수직 속도²))라 할 때 `prograde`는 피치 γ·헤딩 90°, `retrograde`는 피치 −γ·헤딩 270°, `surface_prograde`는 피치 σ·헤딩 90°, `surface_retrograde`는 피치 −σ·헤딩 270°, `radial_out`은 피치 90° − |γ|·헤딩(γ ≥ 0이면 270°, γ < 0이면 90°), `radial_in`은 `radial_out`의 반대 방향(피치 |γ| − 90°·헤딩은 γ ≥ 0이면 90°, γ < 0이면 270°), `normal`은 피치 0°·헤딩 0°, `antinormal`은 피치 0°·헤딩 180°이다. 헤딩 값은 경사각 0° 궤도가 동쪽으로 돈다는 것에 근거한다(이전 게임 검증 2026-11-22, `debug_set_orbit` 궤도에서 `prograde` 정렬 뒤 `get_flight` 헤딩 90.00°).
  - 피치·헤딩·롤: 같은 궤도에서 계획 `attitude`(피치 30°·헤딩 45°·롤 0°, `timeout_s` 있음) → `measure_start` → `wait` 20초 → `measure_stop`이 `completed`로 끝나고, 그 계측을 표본 개수 상한 20으로 조회해 표본마다 기수 방향과 목표 방향 사이 각도가 3° 이하이고 롤이 목표와 3° 이하로 맞는다.
  - SAS: SAS를 켠 상태에서 `attitude`를 시작한 계획이 끝나면 `get_flight`의 SAS가 꺼져 있다. 계획 `attitude`(prograde) → `action_group`(sas, on) → `wait` 5초가 `completed`로 끝나면 `get_flight_events`에 자세 유지가 SAS로 풀렸다는 이벤트가 있고 `get_flight`의 SAS가 켜져 있다. `action_group`(rcs, toggle)을 두 번 실행한 계획이 `completed`로 끝나고 `get_flight_events`의 두 그룹 상태 이벤트에서 첫 번째 상태가 계획 전 `get_flight`의 RCS와 반대이며 두 번째 상태가 그와 같고, 그 뒤 `get_flight`의 RCS가 계획 전과 같다.
  - 스로틀 유지·내림: 계획 `throttle` 0.5가 `completed`로 끝난 뒤 `get_flight`의 스로틀이 0.5이다. 계획 `throttle` 0.3 → `wait` 600초를 제출하고 `abort_plan`으로 중단한 뒤 `get_flight`의 스로틀이 0이고 `get_flight_events`에 스로틀을 내린 이벤트가 있다.
  - 다시 읽기: 계획 `attitude`(prograde) → `throttle` 0.2 → `wait` 600초 실행 중 Mod 로직을 다시 읽은 뒤 `get_plan`의 마지막 계획 종료 사유가 `reload`, `get_status`의 `bridge_flybywire_handlers`가 0, `get_flight`의 스로틀이 0이다.
  - 실패 경로: 저장된 craft `C8 Probe Only`를 발사대에 올려 `get_flight`의 다음 스테이지가 `null`인 것을 확인한 뒤 계획 `stage`를 제출하면 계획이 `step_failed`로 끝난다. SAS를 쓸 수 없는 기체(게임의 `VesselAutopilot.VesselSAS.CanEngageSAS` 판정이 거짓인 craft, 어느 craft인지는 plan에서 정함)를 발사대에 올려 계획 `action_group`(sas, on)을 제출하면 계획이 `step_failed`로 끝나고 `get_flight`의 SAS가 꺼져 있다(D009). C2·C4·C9의 `bad_request` 경우 가운데 범위 밖 스로틀, 목표 없는 `attitude`, 모르는 그룹을 제출하면 `bad_request`이고, 그 뒤 `get_status`가 정상으로 응답한다.
