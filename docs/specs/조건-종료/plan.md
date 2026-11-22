# 조건 종료 판정과 안전 중단 계획

## 요약

새 Mod 모듈과 MCP 서버 모듈 쌍 `condition`이 단계 종류 `wait_until`과 안전 중단 감시 둘(손실, 추력 없음)을 더한다. 코어에는 기능에 매이지 않는 것만 더한다(D009).

- 계획 감시(`IPlanGuard`): 모듈이 등록하면 코어가 계획마다 시작·판정·끝을 알리고, 감시가 이상을 알리면 그 사유로 계획을 끝낸다. 계획의 `abort_on`은 등록된 감시가 알린 키로 검증한다.
- 단계가 계획 번호와 단계 번호를 안다(`StepContext.PlanId`·`StepIndex`).
- 이미 파괴된 활성 기체에는 계획 제출을 거절한다(D006).

조건 값은 flight 모듈의 읽는 코드를 나눠 `get_flight`와 함께 쓴다(D010). 게임에 닿지 않는 부분(조건의 검증과 판정, 손실 모음, 추력 없음 규칙, 감시 호출 순서)은 나눠 단위 테스트로 확인하고, 게임에 닿는 부분은 실제 게임에서 MCP 응답으로 확인한다.

이 문서에서 "디컴파일"은 게임 설치 폴더의 `KSP_x64_Data/Managed/Assembly-CSharp.dll`(KSP 1.12.5)을 `ilspycmd`로 풀어 읽은 것을 뜻한다.

## 바꿀 파일·구조

새 파일(Mod 모듈):

- src/KspBridge.Mod.Modules.Condition/KspBridge.Mod.Modules.Condition.csproj: `src/KspBridge.Mod.Modules.Control/KspBridge.Mod.Modules.Control.csproj`와 같은 구성(net472, 게임 어셈블리 참조, 세대 표식 이름, flight 모듈 참조, 테스트용 `InternalsVisibleTo`).
- src/KspBridge.Mod.Modules.Condition/ConditionModule.cs: 모듈 등록(단계 `wait_until`, 감시 둘).
- src/KspBridge.Mod.Modules.Condition/ConditionArgs.cs: `wait_until` 인자 검증(게임 상태에 닿지 않음).
- src/KspBridge.Mod.Modules.Condition/ConditionValues.cs: 조건 값 이름 표(이름, 비교 종류, 단위 설명, `FlightReport.Build` 응답에서의 위치)와 값 꺼내기(게임 상태에 닿지 않음).
- src/KspBridge.Mod.Modules.Condition/ConditionSet.cs: 조건 묶음의 판정과 요약(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Condition/WaitUntilStep.cs: 단계 `wait_until`.
- src/KspBridge.Mod.Modules.Condition/NoThrustRule.cs: 추력 없음 규칙(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Condition/EngineReader.cs: 활성 기체의 스로틀과 꺼진·작동 중인 엔진 수 읽기.
- src/KspBridge.Mod.Modules.Condition/LossLog.cs: 손실 모음(부품, 종류, 원인, 기체 파괴)과 이벤트·이유 문구(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Condition/LossGuard.cs: 기체 파괴·부품 손실 감시. 게임 이벤트를 구독해 `LossLog`에 넣는다.
- src/KspBridge.Mod.Modules.Condition/NoThrustGuard.cs: 추력 없음 감시.

새 파일(서버 모듈, 테스트, 기준 spec):

- src/KspBridge.Server.Modules.Condition/KspBridge.Server.Modules.Condition.csproj: `src/KspBridge.Server.Modules.Control/KspBridge.Server.Modules.Control.csproj`와 같은 구성.
- src/KspBridge.Server.Modules.Condition/ConditionModule.cs: `[assembly: KspBridgeModule("condition")]`만 둔다. MCP 도구는 없다.
- tests/KspBridge.Mod.Modules.Condition.Tests/KspBridge.Mod.Modules.Condition.Tests.csproj: `tests/KspBridge.Mod.Modules.Control.Tests/KspBridge.Mod.Modules.Control.Tests.csproj`와 같은 구성에 condition 모듈 참조.
- tests/KspBridge.Mod.Modules.Condition.Tests/ConditionArgsTests.cs, ConditionValuesTests.cs, ConditionSetTests.cs, NoThrustRuleTests.cs, LossLogTests.cs: 단위 테스트.
- tests/KspBridge.Mod.Core.Tests/CoordinatorGuardTests.cs: 감시와 `abort_on`의 단위 테스트.
- docs/features/flight-condition.md: 새 기능의 기준 spec(마지막 구현 단계에서 만든다).

고치는 기존 파일:

- src/KspBridge.Mod.Core/ModuleApi.cs: `IPlanGuard`·`GuardOption`·`GuardTrip`, `StepContext.PlanId`·`StepIndex`, `ModuleContext.AddGuard`를 더한다.
- src/KspBridge.Mod.Core/Router.cs: `StepRegistry` 옆에 `GuardRegistry`를 더한다.
- src/KspBridge.Mod.Core/Coordination/Plan.cs: 종료 사유 `vessel_destroyed`·`part_loss`·`no_thrust`, 계획의 `abort_on`과 요약.
- src/KspBridge.Mod.Core/Coordination/Coordinator.cs: `abort_on` 검증, 감시 시작·판정·끝, 단계 번호 채우기.
- src/KspBridge.Mod.Core/LogicEntry.cs: `GuardRegistry`를 만들어 Coordinator와 모듈에 넘긴다.
- src/KspBridge.Mod.Core/Session/FlightSession.cs: 파괴된 활성 기체의 계획 거절.
- src/KspBridge.Mod.Core/Status/CoreModule.cs: `get_status`의 계획 요약에 `abort_on`.
- src/KspBridge.Mod.Modules.Flight/FlightSnapshot.cs: `TakeBasic`을 나눠 클래스와 `TakeBasic`만 공개하고 `Take`·`IsStaged`는 내부 전용으로 둔다(D010·D012).
- src/KspBridge.Mod.Modules.Control/ThrottleRule.cs: 스로틀을 내리는 사유에 `part_loss`·`no_thrust`를 더한다.
- src/KspBridge.Mod.Modules.Control/ThrottleStep.cs: 단계 설명의 종료 사유.
- tests/KspBridge.Mod.Modules.Control.Tests/ThrottleRuleTests.cs: 새 종료 사유 셋.
- src/KspBridge.Server.Modules.Core/CoreTools.cs: `submit_plan`(`abort_on`), `get_plan`(종료 사유, `abort_on`), `get_flight_events`(새 이벤트), `get_status` 설명.
- KspBridge.slnx: 새 프로젝트 셋을 더한다.
- ReadMe.md: 단계 `wait_until`, 안전 중단과 `abort_on`, 모듈이 감시를 더하는 법을 더하고, `throttle` 행의 스로틀을 내리는 종료 사유를 고친다(D014). 이 파일은 다른 기준 spec(bridge-connection·debug-tools·deployment·editor·flight-measurement·server-hosting)의 `파일:` 줄에도 있지만, 그 기능들의 설명은 그대로여서 요구가 바뀌지 않는다.
- docs/features/flight-plan.md, docs/features/flight-control.md: 요구와 이력 반영(마지막 구현 단계).

`src/KspBridge.Mod.Modules.Flight/FlightSnapshot.cs`는 flight-observation의 파일이지만 읽는 값과 응답은 그대로이고 나누기만 하므로 그 기능의 요구는 바뀌지 않는다(D010). 배포 구성(`Directory.Build.targets`)은 모듈을 이름 패턴으로 모으므로 고치지 않는다. 새 패키지는 없다.

## 결정

- 코어에는 계획 감시 확장점만 두고 세 판정은 condition 모듈에 둔다: 계획의 시작·끝을 코어가 갖고 있어 시작·해제가 빠지는 경로가 없고, 다른 중단을 코어를 고치지 않고 더할 수 있다 / 검토한 대안: 모두 코어에 — 중단을 더할 때마다 코어를 고쳐야 해 고르지 않음 (D009)
- 기체 파괴와 부품 손실은 감시 하나(`LossGuard`)가 한다: 같은 게임 이벤트와 같은 모음을 쓰고 우선순위가 한곳에서 정해진다 / 검토한 대안: 감시를 둘로 나눔 — 같은 모음을 두 감시가 순서에 기대어 나눠 써야 해 고르지 않음
- 게임 이벤트를 받는 중에는 모으기만 하고 다음 Coordinator 판정에서 끝낸다: 게임이 부품을 없애는 중에 조종 통로를 떼거나 기체를 바꾸지 않고, 파괴 뒤에 알려지는 충돌 원인까지 모인다(spec C9) / 검토한 대안: 이벤트에서 바로 끝냄 — 원인이 빠지고 게임의 파괴 처리 중간에 끼어들어 고르지 않음
- 조건 값은 `FlightSnapshot.TakeBasic`과 `FlightReport.Build`로 읽는다: `get_flight`와 같은 코드라 값이 어긋나지 않는다 / 검토한 대안: 조건 모듈에 다시 씀 — 두 벌이 되어 고르지 않음 (D010)
- 엔진은 필요할 때만 센다(`no_thrust` 조건이 쓰일 때, 추력 없음 감시가 켜졌을 때) / 검토한 대안: 늘 셈 — 꺼진 감시가 매 프레임 부품을 훑어 고르지 않음
- `abort_on`의 키는 등록된 감시가 알린다: 코어가 감시의 이름을 모른다 / 검토한 대안: 코어에 키를 적음 — D009와 맞지 않아 고르지 않음
- 활성 기체의 파괴 여부(제출 거절)는 `FlightSession.PlanBlockReason`에서 `Vessel.state`를 읽는다: 모든 제출(`submit_plan`·`submit_wait`)이 지나는 곳이고 다른 도구의 준비 판정(`BlockReason`)은 바뀌지 않는다 / 검토한 대안: `BlockReason`에 넣음 — `get_status`의 `ready`와 다른 도구까지 막혀 D006과 맞지 않음 (D006)
- 서버 모듈은 도구 없이 이름만 둔다: 단계와 계획 필드만 더한다(control 모듈과 같은 방식) / 검토한 대안: 서버 모듈을 두지 않음 — C1의 모듈 쌍과 어긋남

## 인터페이스·데이터

### 게임 동작의 출처

- 부품 파괴: `Part.Die`는 자식을 떼고 자기를 끊기 전에 `GameEvents.onPartWillDie`를 발생시킨다. 그래서 그때의 `Part.vessel`이 그 부품이 있던 기체이다. 루트 부품이면 그 뒤 `Vessel.Die`를 부른다. 물리 있는 자식은 `decouple`로 떨어져 파괴되지 않는다(디컴파일 `Part` 11514~11668행).
- 기체 파괴: `Vessel.Die`는 첫 줄에서 `GameEvents.onVesselWillDestroy`를 발생시키고 `state`를 `DEAD`로 둔다(디컴파일 `Vessel` 8696~8730행).
- 구조 파손: `PartJoint.OnJointBreak`(Unity 콜백) → `Part.OnPartJointBreak(force)`가 루트가 아니고 힘이 0보다 크고 부모가 있으면 `GameEvents.onJointBreak`(`EventReport.origin`이 그 부품)를 발생시킨 뒤 `decouple`을 부른다. `decouple`이 그 부품과 자식을 새 기체로 옮기므로 부러진 부품의 그 뒤 폭발은 모이지 않고, 폭발한 부품은 부모가 비워져 부러지지 않는다. 그래서 한 부품은 한 번만 모인다. 분리기·도킹 해제는 `PartJoint.DestroyJoint`를 지나 이 이벤트가 없다(디컴파일 `PartJoint` 1110~1194행, `Part` 5852·6839~6885행, `Vessel.findVesselParts` 3856행, `Part.Die`).
- 원인: `GameEvents.onCrash`·`onCollision`·`onCrashSplashdown`·`onOverheat`·`onOverG`·`onOverPressure`의 `EventReport.origin`이 그 부품이다. 충돌 가운데 `Part.HandleCollision`은 `explode` 뒤에, 나머지는 파괴 전에 발생한다(디컴파일 `Part` 8780~8824·10685·10780·10877행, `PartBuoyancy` 1312·1886행, `Vessel` 7077행, `CollisionEnhancer` 302·363행). 손실과 그 원인이 늘 같은 판정에 모인다는 것은 미확인이다(위험 1).
- 엔진: `ModuleEngines.flameout`(공개 필드), `isOperational`(꺼짐이 아니고 점화됨), `Vessel.ctrlState.mainThrottle`(디컴파일 `ModuleEngines` 293·372행).
- 파괴된 활성 기체: `Vessel.Die`는 활성 기체이면 `FlightGlobals`에서 빼지 않고 게임 오브젝트도 없애지 않으므로 `FlightGlobals.ActiveVessel.state`로 읽을 수 있다(디컴파일 `Vessel` 8739~8775행).
- 물리 프레임: 코어의 `Coordinator.FixedUpdate`는 Loader의 `FixedUpdate`에서 불린다(`src/KspBridge.Mod.Loader/BridgeLoader.cs:80`).
- 계획 종료: `Finish`가 실행 중 단계의 `End` → hold 해제 → 종료 동작 → 이벤트 → `PlanEnded` 순으로 한다(`src/KspBridge.Mod.Core/Coordination/Coordinator.cs`). 다시 읽기는 `LogicEntry.Stop`이 `Abort(reload)`를 부른 뒤 `Subscriptions.ReleaseAll`을 부른다(`src/KspBridge.Mod.Core/LogicEntry.cs`).

### 코어(`IPlanGuard`)

- `GuardOption`: `Key`(`abort_on` 키), `Default`(생략했을 때 값).
- `GuardTrip`: `Reason`(계획 종료 사유), `Detail`(원인 설명).
- `IPlanGuard`: `Options`(키 목록, 없어도 됨), `Priority`(작은 것이 먼저), `Start(int planId)`, `GuardTrip Check(Func<string, bool> enabled)`(이상이 없으면 `null`), `Stop()`.
- `ModuleContext.AddGuard(guard)` → `GuardRegistry`. 같은 키를 둘이 알리면 등록할 때 예외이다.
- `Coordinator`(생성자에 `GuardRegistry`를 선택 인자로 받는다. 생략하면 감시가 없다):
  - 제출: `abort_on`이 객체가 아니거나, 모르는 키이거나, 값이 참·거짓이 아니면(`null` 포함) `bad_request`이다. `abort_on` 자체가 없거나 `null`이면 생략한 것이다. 적용된 값(모든 키)을 계획에 둔다. 제출을 받아들이면 감시마다 `Start(계획 번호)`를 부른다.
  - 판정: `FixedUpdate`의 맨 앞(조종 예외·단계 시작·시간 초과보다 먼저)과, 마지막 단계가 완료로 판정되어 `completed`로 끝내기 직전에 감시를 우선순위 순으로 모두 `Check`하고, 가장 앞선 `GuardTrip`의 사유와 설명으로 계획을 끝낸다. 완료 직전 판정에서 끝나면 그 단계의 상태는 `aborted`이다.
  - 예외: `Check`의 예외는 나머지 감시를 부른 뒤, 알려진 이상이 없으면 지금 단계의 실패(`guard error: …`)가 된다. `Start`의 예외는 다음 판정에서 같은 실패가 된다. `Stop`의 예외는 이벤트 `plan_guard_error`로 남고 계획 종료를 막지 않는다.
  - 끝: `Finish`가 종료 동작을 부른 뒤, 계획 종료 이벤트를 남기기 전에 감시마다 `Stop`을 부른다. `Stop`은 아직 판정하지 않은 것을 이벤트로 남길 수 있다.
- 계획 요약(`current`·`last`·제출 응답)에 `abort_on`(키와 값)이 들어간다. `get_status`의 계획 요약에도 넣고, 이전 로직에서 넘어온 요약에 없으면 `null`이다.
- `StepContext.PlanId`·`StepIndex`는 코어가 채운다.

### `wait_until`

```json
{ "type": "wait_until", "params": { "all": [ { "value": "altitude_m", "op": ">=", "to": 10000 }, { "value": "situation", "op": "==", "to": "FLYING" } ] }, "timeout_s": 120 }
```

- 인자: `all`과 `any` 가운데 하나. `to`는 수치 조건이면 수, `situation`이면 문자열(`Vessel.Situations` 이름), `no_thrust`이면 참·거짓이다. 조건 항목의 모르는 필드는 무시하지 않고 `bad_request`이다.
- 판정: `Judge`마다 `ConditionSet`이 값을 한 번 읽어 조건마다 값과 맞았는지를 낸다. 값이 없거나(`null`) 수가 아니면(NaN) 맞지 않은 것이다. 맞으면 이벤트 `condition_met`(`index`, `mode`, `conditions`: `value`·`op`·`to`·`actual`·`met`)을 계획 번호와 함께 남기고 완료한다.
- 진행: `mode`와 `conditions`(같은 모양).
- 활성 기체가 없으면 단계가 실패한다(`there is no active vessel`).

### 감시

- `LossGuard`(우선순위 0, 키 `part_loss` 기본 참): `Start`에서 활성 기체를 기억하고 모음을 비운다. 구독은 등록할 때 한 번 하고(`context.Subscriptions`), 계획이 없으면(`Start`와 `Stop` 사이가 아니면) 아무것도 모으지 않는다. `onPartWillDie`·`onJointBreak`는 부품의 기체가 기억한 기체일 때만, `onVesselWillDestroy`는 그 기체일 때만 모은다. 원인 이벤트는 부품으로 모아 두었다가 손실과 잇는다. 처리기의 예외는 잡아 두었다가 다음 `Check`에서 던진다.
  - `Check`: 모은 것을 비우고, 손실이 있으면 이벤트 `parts_lost`(`count`, `parts`: `name`·`kind`·`cause`, 최대 10개)를 남긴다. 파괴되었으면 `vessel_destroyed`, 손실이 있고 `part_loss`가 켜졌으면 `part_loss`를 낸다.
  - `Stop`: 남은 손실이 있으면 `parts_lost`를 남기고 기억한 기체를 잊는다.
  - 부품 이름은 `Part.partInfo.name`이다. 부품을 가르는 열쇠는 `Object.GetInstanceID()`이다.
- `NoThrustGuard`(우선순위 1, 키 `no_thrust` 기본 거짓): 켜졌을 때만 활성 기체의 엔진을 세어 `NoThrustRule`로 판정하고 `no_thrust`를 낸다(설명에 꺼진 엔진 수).

### 종료 사유와 스로틀

- `EndReasons`에 `vessel_destroyed`·`part_loss`·`no_thrust`를 더한다. `ThrottleRule.ShouldCut`은 `part_loss`·`no_thrust`에 참, `vessel_destroyed`에 거짓이다.

## 오류·위험

1. 손실과 충돌 원인이 같은 판정에 모인다는 것은 미확인이다. 어긋나면 원인이 `null`인 손실이 나온다. 알아채는 방법: C12 부품 손실 시험의 `cause`.
2. 첫 충돌에서 루트 부품까지 같은 물리 프레임에 파괴되면 `part_loss` 대신 `vessel_destroyed`로 끝난다. 시험 기체는 루트를 맨 위에 두고 자세 유지로 세워 떨어뜨린다. 자세 유지가 낙하 중에 기체를 세워 둔다는 것은 미확인이다. 알아채는 방법: C12 부품 손실 시험의 종료 사유와 `parts_lost`의 부품. 어긋나면 시험 기체를 고친다(판정을 고치지 않는다).
3. 시험 기체의 부품 이름과 고체 부스터의 연소 시간은 미확인이다. 알아채는 방법: 만들 때 `editor_list_parts`로 이름을, `check_ship`으로 연소 시간과 TWR을 본다.
4. `no_thrust`는 흡입 공기 부족으로 잠깐 꺼진 제트 엔진에도 참이 된다(spec 용어). 켠 계획에서만 중단되고, 종료 설명의 꺼진 엔진 수로 알아챈다.
5. 파괴된 활성 기체에서 hold와 단계의 `Control`이 다음 판정 전에 불릴 수 있는지는 미확인이다. 예외가 나도 다음 프레임의 감시 판정이 조종 예외보다 먼저라 `vessel_destroyed`로 끝난다. 알아채는 방법: C12 기체 파괴 시험의 종료 사유.
6. `FlightSnapshot`을 나누다 `get_flight` 값이 바뀔 위험. 알아채는 방법: 발사대에서 게시 전과 뒤의 `get_flight`(`include` 셋 포함)를 비교한다(발사대에서 잡음이 큰 세 값과 시각, 탐사선이 쓰는 `ElectricCharge` 현재량은 빼고, 세 값은 궤도에서 따로 본다. 검증 전략 C2 값, D013).
7. 폭발한 부품의 `partInfo`가 없을 가능성은 미확인이다. 없으면 이름을 `null`로 둔다. 알아채는 방법: C12 부품 손실 시험의 부품 이름.

## 검증 전략

빌드와 단위 테스트:

- `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과한다.
- C2: `ConditionArgsTests` — 묶음 조합, 빈 목록, 모르는 이름·비교·상황, 종류가 맞지 않는 `to`, 빠진 필드, 모르는 필드. `ConditionValuesTests` — 이름마다 값이 `FlightReport.Build`의 같은 이름 값과 같음, 열린 궤도의 `null`.
- C3·C4: `ConditionSetTests` — `all`·`any`, 경계(같음 포함), `null`·NaN, 요약. 시간 초과가 먼저인 것은 `CoordinatorGuardTests`에 겹칠 때 하나를 둔다.
- C5·C6·C9·C11: `CoordinatorGuardTests` — `abort_on` 기본값·검증·요약, 우선순위, 시간 초과·완료보다 먼저, 시작되지 않은 단계를 시작하지 않음, 완료 직전 판정, `Check`·`Start`·`Stop`의 예외, `Stop`이 한 번 불림, 계획마다 `Start`, 단계 번호.
- C7·C11: `LossLogTests` — 종류·원인 잇기(앞·뒤 순서), 10개 제한, 비우기, 시작 전과 끝난 뒤에는 모으지 않음, 앞 손실이 다음 계획에 넘어가지 않음.
- C8: `NoThrustRuleTests` — 스로틀 0, 꺼진 엔진 없음, 작동 중인 엔진 있음, 추력 없음.
- C10: `ThrottleRuleTests` — 새 종료 사유 셋.

실제 게임(실제 `mcp__kspbridge__*` 호출, 판정은 응답만으로):

- 배포: `dotnet publish src/KspBridge.Mod.Core -c Release`와 `dotnet publish src/KspBridge.Server.Modules.Condition -c Release`로 게시하고 핫리로드로 반영한다. 게임 장면을 바꾸기 전에 `get_status`로 다른 비행이 없는지 본다.
- 시험 기체: 에디터 도구로 무인 2단 기체 `C33 Loss Test`를 만들어 저장한다. 위에서부터 탐사선 코어(루트), 반작용 휠, 배터리, 고체 부스터(위 단), 분리기, 고체 부스터(아래 단)이며, 아래 단 점화 → 분리기와 위 단 점화 순으로 스테이지를 둔다.
- C1: `get_status`의 서버·Mod 모듈에 `condition`이 있고 `list_step_types`에 `wait_until`과 조건 값 전체·단위·비교·완료·실패 조건이 나온다.
- C2 값: 발사대에서 게시 전과 뒤의 `get_flight`를 비교하고(위험 6), 조건 값 전체를 하나씩 넣은 `any` 조건(맞지 않는 값)과 `elapsed_s` >= 1의 `wait_until`이 끝난 뒤 `condition_met`의 값이 이어서 읽은 `get_flight`의 값과 맞는지 본다(움직이지 않는 발사대에서 값마다 1% 또는 0.1 안. `elapsed_s`·`no_thrust`와, 서 있는 기체에서도 읽을 때마다 달라질 수 있는 `time_to_apoapsis_s`·`heading_deg`·`roll_deg` 제외. `time_to_apoapsis_s`는 Ap 근처라 0과 주기 사이를 오가고(디컴파일 `Orbit` 700~734행) 나머지 둘은 미세한 기울기로 정해진다(`src/KspBridge.Mod.Modules.Flight/Attitude.cs`)). 세 값은 궤도(Kerbin Ap 120 km·Pe 90 km)에서 계획 `attitude`(피치 30°·헤딩 45°·롤 0°) → `wait_until`(`any`: 세 값의 맞지 않는 조건과 `elapsed_s` >= 1)을 실행해, `condition_met`의 헤딩이 45°, 롤이 0°와 3° 안이고, Ap까지 남은 시간이 이어서 읽은 `get_flight`의 값에 두 시각의 UT 차를 더한 값과 1% 안에서 같은지 본다(D011).
- C4 진행: 계획 `wait_until`(`all`: `altitude_m` >= 100000)을 붙잡아 두고 `get_plan`의 `step_progress`를 본 뒤 `abort_plan`이 그 계획을 `planner_abort`로 끝내는지 본다.
- C5 요약: 제출 응답·`get_plan`의 `current`와 `last`·`get_status`에 `abort_on`이 나온다.
- C12: spec의 항목을 그 순서대로 한다. 떨어뜨리는 계획은 `attitude`(피치 90°·헤딩 90°) → `throttle` 1 → `stage` → `wait_until`(`all`: `no_thrust` == true) → `throttle` 0 → `wait` 600초이며, 계획의 종료는 `get_plan`을 읽어 안다. 계획 없는 손실은 `get_status`의 고도가 두 번 이어 읽어 같고 500 m 아래이면 파괴된 것으로 보고 판정한다(그 전에는 계획을 제출하지 않는다).
- C6의 "자동 전환보다 먼저", C9의 완료 직전 판정, C11의 예외는 게임에서 일으킬 수단이 없어 단위 테스트로 확인한다. C11의 구독 등록은 구현 후 검토에서 코드로 확인한다(D008).
- 구조 파손과 과열·과중력·과압 원인은 게임에서 만들지 않는다(spec C12 끝).

## 원칙 점검

- 적용 규칙:
  - `workflow/spec-driven.md`와 하위 문서, `workflow/repository-content.md`(저장소 기준 상대 경로, 개인 경로 없음)
  - `git/github-access.md`·`git/github-issue.md`(이슈 #33), `git/commit-message.md`
  - `dotnet/project-creation.md`(`src/`·`tests/` 배치, `{Solution}.{역할}` 이름, MSTest, 솔루션에 포함), `dotnet/nuget-package-selection.md`(새 패키지 없음)
  - `shell/powershell-windows.md`(명령 실행), `workflow/conversation-language.md`(대화 언어)
  - 전역 규칙 MCP 도구 검증(게임 검증은 실제 `mcp__kspbridge__*` 호출로만 판정)
  - 저장소 규칙 파일은 없다. `ReadMe.md` "기능 모듈 추가"는 기능을 코어 코드를 고치지 않고 모듈로 더한다고 한다.
- 충돌:
  - `dotnet/project-creation.md` 1번(`net10.0`)을 Mod 모듈과 그 테스트는 따르지 않고 `net472`를 쓴다. 게임(KSP 1.12.5, Unity Mono)이 읽는 어셈블리여야 하고 기존 Mod 프로젝트가 모두 그렇다.
  - `ReadMe.md`의 "코어 코드는 고치지 않습니다"와 달리 코어 일곱 파일을 고친다. 계획을 끝내는 일은 계획의 수명을 가진 코어만 할 수 있고 사용자가 D009에서 정했다. 더하는 것은 기능에 매이지 않는 감시 확장점과 종료 사유 이름, 파괴된 기체의 제출 거절, `get_status`의 표시뿐이며 판정은 모듈에 있다.
