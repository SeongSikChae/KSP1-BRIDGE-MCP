# flight-plan

파일: src/KspBridge.Mod.Core/ModuleApi.cs, src/KspBridge.Mod.Core/Coordination/Plan.cs, src/KspBridge.Mod.Core/Coordination/Coordinator.cs, src/KspBridge.Mod.Core/Coordination/EventLog.cs, src/KspBridge.Mod.Core/Control/Controller.cs, src/KspBridge.Mod.Core/Session/FlightSession.cs, src/KspBridge.Mod.Core/Status/CoreModule.cs, src/KspBridge.Mod.Modules.Wait/KspBridge.Mod.Modules.Wait.csproj, src/KspBridge.Mod.Modules.Wait/WaitModule.cs, src/KspBridge.Server.Modules.Core/CoreTools.cs, src/KspBridge.Server.Modules.Wait/KspBridge.Server.Modules.Wait.csproj, src/KspBridge.Server.Modules.Wait/WaitTools.cs, tests/KspBridge.Mod.Core.Tests/KspBridge.Mod.Core.Tests.csproj, tests/KspBridge.Mod.Core.Tests/CoordinatorTests.cs, tests/KspBridge.Mod.Core.Tests/EventLogTests.cs, tests/KspBridge.Mod.Core.Tests/WaitProgressTests.cs, ReadMe.md

Planner(LLM)가 비행 계획을 제출·조회·중단하는 기능. 계획은 게임 안 Coordinator가 물리 프레임마다 단계를 차례로 판정해 실행하고, Controller가 단계의 조종 입력을 기체에 넣는다. 단계 종류는 기능 모듈이 더한다. 괄호 안 D번호는 docs/specs/관측/decisions.md의 결정이다.

## 요구(완료 조건)

- R1. Planner가 계획을 제출(`submit_plan`)·조회(`get_plan`)·중단(`abort_plan`)할 수 있다. 제출한 계획은 Coordinator가 단계를 차례로 완료 처리하고 완료로 끝낸다. 단계 시작·완료·계획 종료가 이벤트로 남아 `get_flight_events`로 조회된다. 계획은 비행 장면이 준비됐을 때만 제출되며(아니면 `not_ready`), 실행 중인 계획이 있으면 `replace=true` 없이 새 계획은 `bad_request`이다.
- R2. 새 비행으로 바뀌거나(발사·되돌리기·씬 변경) 활성 기체가 바뀌면 이전 계획은 사유(`new_flight`·`scene_change`·`vessel_change`)와 함께 끝나고, `get_status`에 이전 비행의 계획 상태가 남지 않는다.
- R3. 계획 실행 중에 Mod 로직을 다시 읽으면 실행 중이던 계획은 사유 `reload`와 함께 끝난 상태로 남고, 이전 로직이 걸어 둔 기체 제어 입력이 남지 않는다.
- R4. 새 단계 종류와 그에 대응하는 MCP 도구를 코어 코드를 고치지 않고 별도 모듈로 더할 수 있다. 시간 대기 단계(`wait`)와 `submit_wait`가 이 방식으로 구현되어 있다.
- R5. `get_plan`의 실행 중 계획(`current`)과 `submit_plan`·`submit_wait` 응답의 계획 요약에 계획 경과 시간(제출부터, 게임 시간 s), 실행 중 단계의 경과 시간, timeout까지 남은 시간(timeout이 없으면 `null`), 그 단계가 보고한 진행(보고하지 않는 단계는 `null`)이 들어간다. 현재 단계가 아직 시작되지 않았으면(제출 직후, 앞 단계가 끝나고 다음 단계가 시작되기 전) 단계 값은 `null`이고 계획 경과 시간은 값이 있다. 끝난 계획(`last`)과 `get_status`의 계획 요약에는 이 값들을 더하지 않는다(D002·D023).
- R6. 단계 종류는 코어를 바꾸지 않고 진행을 선택적으로 보고할 수 있다(`IStepProgress`). 진행 보고가 예외를 던지면 계획은 그대로 실행되고, 진행은 `null`과 오류 설명(`step_progress_error`)으로 나타난다. `wait` 단계는 목표 시간(s)과 남은 시간(s, 0 미만이 되지 않음)을 보고한다(D002).
- R7. 계획 도구의 오류 동작: `get_plan`·`get_flight_events`·`list_step_types`·`get_status`는 장면과 상관없이 오류를 내지 않는다(`get_plan`은 장면 전환으로 끝난 계획도 보여 준다). `abort_plan`은 실행 중인 계획이 없으면 `bad_request`이다. `submit_plan`·`submit_wait`는 장면 로딩·비행 장면 아님에 `not_ready`, 인자 오류에 `bad_request`이다(D021·D022·D023).

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-18 | 골격 | R1~R4에 해당하는 계획 실행 골격(기준 spec 생기기 전) | docs/specs/골격/ |
| 2026-11-21 | 관측 | R1~R4 추가(기준 spec 처음 만듦, 골격의 요구를 옮김), R5·R6·R7 추가 | docs/specs/관측/ |

표에 없는 이전 변경: 기록 없음
