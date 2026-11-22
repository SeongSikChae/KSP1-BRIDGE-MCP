# flight-plan

파일: src/KspBridge.Mod.Core/ModuleApi.cs, src/KspBridge.Mod.Core/Router.cs, src/KspBridge.Mod.Core/LogicEntry.cs, src/KspBridge.Mod.Core/Coordination/Plan.cs, src/KspBridge.Mod.Core/Coordination/Coordinator.cs, src/KspBridge.Mod.Core/Coordination/EventLog.cs, src/KspBridge.Mod.Core/Control/Controller.cs, src/KspBridge.Mod.Core/Session/FlightSession.cs, src/KspBridge.Mod.Core/Status/CoreModule.cs, src/KspBridge.Mod.Modules.Wait/KspBridge.Mod.Modules.Wait.csproj, src/KspBridge.Mod.Modules.Wait/WaitModule.cs, src/KspBridge.Server.Modules.Core/CoreTools.cs, src/KspBridge.Server.Modules.Wait/KspBridge.Server.Modules.Wait.csproj, src/KspBridge.Server.Modules.Wait/WaitTools.cs, tests/KspBridge.Mod.Core.Tests/KspBridge.Mod.Core.Tests.csproj, tests/KspBridge.Mod.Core.Tests/CoordinatorTests.cs, tests/KspBridge.Mod.Core.Tests/CoordinatorGuardTests.cs, tests/KspBridge.Mod.Core.Tests/EventLogTests.cs, tests/KspBridge.Mod.Core.Tests/WaitProgressTests.cs, ReadMe.md

Planner(LLM)가 비행 계획을 제출·조회·중단하는 기능. 계획은 게임 안 Coordinator가 물리 프레임마다 단계를 차례로 판정해 실행하고, Controller가 단계의 조종 입력을 기체에 넣는다. 단계 종류는 기능 모듈이 더한다. 괄호 안 D번호는 docs/specs/관측/decisions.md의 결정이다.

## 요구(완료 조건)

- R1. Planner가 계획을 제출(`submit_plan`)·조회(`get_plan`)·중단(`abort_plan`)할 수 있다. 제출한 계획은 Coordinator가 단계를 차례로 완료 처리하고 완료로 끝낸다. 단계 시작·완료·계획 종료가 이벤트로 남아 `get_flight_events`로 조회된다. 계획은 비행 장면이 준비됐을 때만 제출되며(아니면 `not_ready`), 실행 중인 계획이 있으면 `replace=true` 없이 새 계획은 `bad_request`이다.
- R2. 새 비행으로 바뀌거나(발사·되돌리기·씬 변경) 활성 기체가 바뀌면 이전 계획은 사유(`new_flight`·`scene_change`·`vessel_change`)와 함께 끝나고, `get_status`에 이전 비행의 계획 상태가 남지 않는다.
- R3. 계획 실행 중에 Mod 로직을 다시 읽으면 실행 중이던 계획은 사유 `reload`와 함께 끝난 상태로 남고, 이전 로직이 걸어 둔 기체 제어 입력이 남지 않는다.
- R4. 새 단계 종류와 그에 대응하는 MCP 도구를 코어 코드를 고치지 않고 별도 모듈로 더할 수 있다. 시간 대기 단계(`wait`)와 `submit_wait`가 이 방식으로 구현되어 있다.
- R5. `get_plan`의 실행 중 계획(`current`)과 `submit_plan`·`submit_wait` 응답의 계획 요약에 계획 경과 시간(제출부터, 게임 시간 s), 실행 중 단계의 경과 시간, timeout까지 남은 시간(timeout이 없으면 `null`), 그 단계가 보고한 진행(보고하지 않는 단계는 `null`)이 들어간다. 현재 단계가 아직 시작되지 않았으면(제출 직후, 앞 단계가 끝나고 다음 단계가 시작되기 전) 단계 값은 `null`이고 계획 경과 시간은 값이 있다. 끝난 계획(`last`)과 `get_status`의 계획 요약에는 이 값들을 더하지 않는다(D002·D023).
- R6. 단계 종류는 코어를 바꾸지 않고 진행을 선택적으로 보고할 수 있다(`IStepProgress`). 진행 보고가 예외를 던지면 계획은 그대로 실행되고, 진행은 `null`과 오류 설명(`step_progress_error`)으로 나타난다. `wait` 단계는 목표 시간(s)과 남은 시간(s, 0 미만이 되지 않음)을 보고한다(D002).
- R7. 계획 도구의 오류 동작: `get_plan`·`get_flight_events`·`list_step_types`·`get_status`는 장면과 상관없이 오류를 내지 않는다(`get_plan`은 장면 전환으로 끝난 계획도 보여 준다). `abort_plan`은 실행 중인 계획이 없으면 `bad_request`이다. `submit_plan`·`submit_wait`는 장면 로딩·비행 장면 아님에 `not_ready`, 인자 오류에 `bad_request`이다(D021·D022·D023).
- R8. 단계는 자기가 끝난 뒤에도 계획이 끝날 때까지 이어질 조종 입력(hold)을 계획에 하나 걸 수 있다(`StepContext.Plan`, `IPlanHold`). hold는 활성 기체의 조종 입력 통로에서 실행 중 단계보다 먼저, 단계 사이의 프레임에도 불린다. 다른 hold를 걸거나 풀거나 계획이 끝나면(사유와 상관없이) 풀리고, 풀릴 때마다 사유와 함께 이벤트 `hold_released`가 남는다. `get_plan`의 `current`와 제출 응답의 계획 요약에 `hold`(없으면 `null`)와 `hold_error`가 나오고, `last`와 `get_status`에는 더하지 않는다. hold가 예외를 던지면 그 계획만 `step_failed`로 끝난다(기본-제어 D001·D011).
- R9. 단계는 계획이 끝날 때 할 일을 등록할 수 있다(`StepContext.Plan.AtEnd`). 계획이 끝나면 hold를 푼 뒤, 계획 종료 이벤트를 남기기 전에 종료 사유와 함께 한 번 불린다. 같은 이름은 처음 것만 남고, 예외는 이벤트 `plan_end_error`로 남으며 계획 종료를 막지 않는다(기본-제어 D001).
- R10. 단계의 시작이나 판정이 게임 이벤트를 일으켜 그 계획이 끝나면(예: 스테이징으로 활성 기체가 바뀜) 그 단계를 완료로 처리하지 않는다. 조종 입력의 예외가 단계 사이에 나면 다음 단계는 시작하지 않고 실패한다.
- R11. 모듈은 코어를 고치지 않고 계획 감시(`IPlanGuard`)를 더할 수 있다. 감시는 계획마다 시작과 끝을 알림받고, 매 물리 프레임에 실행 중 단계의 조종 예외·시작·시간 초과·완료·실패 판정보다 먼저, 그리고 계획을 `completed`로 끝내기 직전에 판정된다. 이상을 알리면 그 사유와 설명으로 계획이 끝나고(`end_reason`·`end_detail`), 아직 시작되지 않은 단계는 시작하지 않는다. 여럿이면 우선순위가 앞선 하나의 사유이며, 이때도 모든 감시가 판정된다. 다른 사유로 끝난 계획에서 아직 판정하지 않은 것은 감시가 이벤트로 남길 수 있고 종료 사유는 바뀌지 않으며, 계획이 끝난 뒤의 이상은 그 계획의 것이 아니다. 감시의 예외는 그 계획만 `step_failed`로 끝내고, 끝날 때의 예외는 이벤트 `plan_guard_error`로 남으며 계획 종료를 막지 않는다(조건-종료 D005·D009). 안전 중단의 감시는 docs/features/flight-condition.md의 요구이다.
- R12. `submit_plan`의 계획은 선택 필드 `abort_on`을 받는다. 키는 등록된 감시가 알린 것(`part_loss` 기본 `true`, `no_thrust` 기본 `false`)이고 값은 참·거짓이다. 모르는 키이거나 참·거짓이 아니면 `bad_request`이다. `submit_wait`의 계획은 생략한 것과 같다. 적용된 `abort_on`이 계획 요약(`get_plan`의 `current`·`last`, 제출 응답, `get_status`)에 나온다.
- R13. 활성 기체가 이미 파괴된 상태이면 계획 제출(`submit_plan`·`submit_wait`)이 이유와 함께 `not_ready`이다. 다른 도구의 준비 판정은 바뀌지 않는다(조건-종료 D006).
- R14. 단계는 자기가 속한 계획 번호와 단계 번호를 안다(`StepContext.PlanId`·`StepIndex`).

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-18 | 골격 | R1~R4에 해당하는 계획 실행 골격(기준 spec 생기기 전) | docs/specs/골격/ |
| 2026-11-21 | 관측 | R1~R4 추가(기준 spec 처음 만듦, 골격의 요구를 옮김), R5·R6·R7 추가 | docs/specs/관측/ |
| 2026-11-22 | 기본-제어 | R8·R9·R10 추가 | docs/specs/기본-제어/ |
| 2026-11-22 | 조건-종료 | R11·R12·R13·R14 추가 | docs/specs/조건-종료/ |

표에 없는 이전 변경: 기록 없음
