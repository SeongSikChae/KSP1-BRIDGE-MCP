# 시간 가속 단계 계획

## 요약

새 Mod 모듈과 MCP 서버 모듈 쌍 `warp`가 단계 종류 `warp`를 더한다. 코어는 고치지 않는다. 단계는 매 물리 프레임(코어의 `Judge`)에 목표와 영향권 전환까지 남은 게임 시간과 게임의 레일 상한으로 배율을 고르고, 목표 전에 1배로 내려 레일에서 내려온 뒤 완료한다. 단계가 완료 아닌 사유로 끝나면 `End`에서 1배로 되돌린다.

게임에 닿는 부분(`GameWarp`)과 닿지 않는 부분(인자, 목표 UT, 배율 규칙, 단계의 진행)을 나눈다. 닿지 않는 부분은 가짜 게임으로 단위 테스트하고, 닿는 부분은 실제 게임에서 MCP 응답으로 확인한다.

가속 상태(`time_warp`)는 flight 모듈이 읽어 `get_flight`에 넣고, `warp` 모듈은 같은 코드를 쓴다.

이 문서에서 "디컴파일"은 spec.md 용어 절과 같다.

## 바꿀 파일·구조

새 파일(Mod 모듈):

- src/KspBridge.Mod.Modules.Warp/KspBridge.Mod.Modules.Warp.csproj: `src/KspBridge.Mod.Modules.Condition/KspBridge.Mod.Modules.Condition.csproj`와 같은 구성(flight 모듈 참조, 테스트용 `InternalsVisibleTo`).
- src/KspBridge.Mod.Modules.Warp/WarpModule.cs: 모듈 등록(단계 `warp`).
- src/KspBridge.Mod.Modules.Warp/WarpArgs.cs: 인자 검증(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Warp/WarpTarget.cs: 목표 UT 계산과 시작 실패 판정(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Warp/WarpRule.cs: 목표와 영향권 전환까지 남은 시간, 레일 상한으로 배율 단을 고르고 명령을 정하는 규칙(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Warp/IWarpGame.cs: 단계가 게임에 하는 일(읽기, 배율 설정, 자동 가속 끄기, 스로틀)의 인터페이스와 값 형식.
- src/KspBridge.Mod.Modules.Warp/WarpSession.cs: 단계 하나의 진행(시작, 프레임마다, 끝). `IWarpGame`만 쓴다(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Warp/GameWarp.cs: `IWarpGame`의 게임 구현.
- src/KspBridge.Mod.Modules.Warp/WarpStep.cs: 단계 `warp`(설명, 코어와 `WarpSession` 연결, 이벤트).

새 파일(서버 모듈, 테스트, 기준 spec):

- src/KspBridge.Server.Modules.Warp/KspBridge.Server.Modules.Warp.csproj: `src/KspBridge.Server.Modules.Condition/KspBridge.Server.Modules.Condition.csproj`와 같은 구성.
- src/KspBridge.Server.Modules.Warp/WarpModule.cs: `[assembly: KspBridgeModule("warp")]`만 둔다. MCP 도구는 없다.
- tests/KspBridge.Mod.Modules.Warp.Tests/KspBridge.Mod.Modules.Warp.Tests.csproj: `tests/KspBridge.Mod.Modules.Condition.Tests/KspBridge.Mod.Modules.Condition.Tests.csproj`와 같은 구성에 warp 모듈 참조.
- tests/KspBridge.Mod.Modules.Warp.Tests/FakeWarpGame.cs: 게임의 시간 진행, 배율이 1초에 걸쳐 바뀌는 것과 그 시작값(앞 목표 배율), 지금 배율에 따라 달라지는 영향권 상한을 포함한 레일 상한, 게임이 스스로 내리는 것, 게임과 단계의 `FixedUpdate` 순서(두 가지), 레일에서 내려오는 지연을 흉내 내는 가짜.
- tests/KspBridge.Mod.Modules.Warp.Tests/WarpArgsTests.cs, WarpTargetTests.cs, WarpRuleTests.cs, WarpSessionTests.cs: 단위 테스트.
- docs/features/flight-warp.md: 새 기능의 기준 spec(마지막 구현 단계에서 만든다).

고치는 기존 파일:

- src/KspBridge.Mod.Modules.Flight/FlightData.cs: `WarpState`(배율, 물리 여부, 레일 위)와 `FlightData.Warp`를 더한다.
- src/KspBridge.Mod.Modules.Flight/FlightSnapshot.cs: `Warp(Vessel)`(공개)로 가속 상태를 읽고 `TakeBasic`에서 채운다.
- src/KspBridge.Mod.Modules.Flight/FlightReport.cs: 기본 응답에 `time_warp`, `TimeWarp(WarpState)`(공개).
- tests/KspBridge.Mod.Modules.Flight.Tests/FlightReportTests.cs: 기본 응답의 키와 `time_warp`의 방식.
- src/KspBridge.Server.Modules.Flight/FlightTools.cs: `get_flight` 설명에 `time_warp`.
- src/KspBridge.Server.Modules.Core/CoreTools.cs: `get_flight_events` 설명에 `warp_completed`·`warp_released`. flight-plan의 파일이지만 요구는 바뀌지 않는다.
- src/KspBridge.Mod.Modules.Control/ThrottleStep.cs: 단계 설명에 `warp` 뒤 스로틀(C13).
- KspBridge.slnx: 새 프로젝트 셋을 더한다.
- ReadMe.md: 단계 `warp`와 `get_flight`의 `time_warp`. 이 파일은 다른 기준 spec의 `파일:` 줄에도 있지만 그 기능들의 설명은 그대로여서 요구가 바뀌지 않는다.
- docs/features/flight-observation.md, docs/features/flight-control.md: 요구와 이력 반영(마지막 구현 단계).

배포 구성(`Directory.Build.targets`)은 모듈을 이름 패턴으로 모으므로 고치지 않는다. 새 패키지는 없다.

## 결정

- 코어를 고치지 않는다: 시작·판정·끝이 이미 단계에 있고, 계획이 어떤 사유로 끝나도 실행 중 단계의 `End`가 불린다(`src/KspBridge.Mod.Core/Coordination/Coordinator.cs`의 `Finish`) / 검토한 대안: 계획 감시(`IPlanGuard`)로 해제 — 단계가 실행 중일 때만 해제하면 되므로 쓰지 않음
- 배율은 `TimeWarp.SetRate(단, 즉시, 화면 메시지 없음)`로 바꾸고, 방식은 공개 필드 `TimeWarp.Mode`로 바꾼다: 게임의 잠금 처리와 레일 상한 제한이 그대로 적용된다 / 검토한 대안: `TimeWarp.WarpTo` — 멈출 때 UT를 되돌려 맞추고 레일 가속만 해서 쓰지 않음(spec 용어)
- 올릴 때는 즉시가 아니게(게임의 1초 전환), 내릴 때는 즉시 바꾼다: 올리는 동안의 배율은 늘 목표 단 이하여서 지나치지 않고, 내릴 때 1초 전환을 기다리면 지나칠 수 있다 / 검토한 대안: 늘 즉시 — 올릴 때 갑자기 바뀔 이유가 없어 고르지 않음
- 배율 단은 목표까지 남은 시간으로 고른다: 남은 시간이 `여유 + 10프레임 × 0.02초 × 배율` 이상인 가장 높은 단. 여유는 레일 2초, 물리 0.2초. 레일은 영향권 전환까지 남은 시간도 본다(배율 규칙의 `RailsWanted`, D011) / 검토한 대안: 게임의 감속 거리 식 — 1초 전환을 전제로 해 즉시 내리는 방식과 맞지 않음
- 레일 상한과 막힌 이유는 게임의 비공개 함수 `TimeWarp.getMaxOnRailsRateIdx`를 리플렉션으로 불러 얻는다: 게임이 `SetRate`에서 쓰는 바로 그 판정이라 어긋나지 않는다 / 검토한 대안: 조건을 다시 씀 — 대기·고도 판정을 두 벌로 갖게 되어 고르지 않음(영향권만은 게임보다 먼저 내리려고 여유를 더해 따로 센다. D011). `SetRate`를 시도해 결과를 봄 — 막힌 이유를 알 수 없고 시도할 때마다 게임 이벤트가 발생해 고르지 않음
- 단계의 진행을 `WarpSession`으로 나누고 `IWarpGame`으로 게임을 가린다: 도착, 다시 맞춤, 해제, 스로틀, 막힌 이유를 게임 없이 시험한다 / 검토한 대안: 단계에 모두 둠 — spec C12 끝의 단위 테스트 항목을 할 수 없음
- 스로틀 쓰기는 warp 모듈에 따로 둔다(두 줄): control 모듈의 쓰기는 조종 가능 검사와 종료 동작에 묶여 있고 내부 전용이다 / 검토한 대안: control 모듈을 참조 — 그 모듈의 공개 범위를 넓혀야 해 고르지 않음
- 가속 상태는 flight 모듈이 읽는다: `get_flight`와 `step_progress`가 같은 값을 보인다 / 검토한 대안: warp 모듈이 읽고 flight가 참조 — `get_flight`가 단계 모듈에 기대게 되어 고르지 않음 (D004)
- 목표 UT를 시작할 때 한 번 정하고, 완료할 때 스로틀을 되돌린다 / 검토한 대안: decisions.md의 선택지 (D001, D005)
- 서버 모듈은 도구 없이 이름만 둔다(control·condition 모듈과 같은 방식) / 검토한 대안: 서버 모듈을 두지 않음 — C1의 모듈 쌍과 어긋남

- 영향권 전환 전에는 단계가 게임보다 먼저 즉시 내린다: 게임의 영향권 상한은 지금 배율로 정해져 내리면 다시 풀리고, 게임이 올리는 도중에 1초에 걸쳐 내리면 첫 프레임의 배율이 앞 목표 배율이 된다 / 검토한 대안: 게임이 내린 뒤 전환까지 50배로 묶음 — 첫 프레임 문제가 남음 (D011)
- 배율을 바꿀 때 멈춘 상태를 지킨다(앞의 `Time.timeScale`이 0이었으면 바꾼 뒤 다시 0): 일시정지 중에도 중단·다시 읽기가 실행된다 / 검토한 대안: 일시정지 중에는 즉시가 아니게 내림 — 재개 전까지 옛 배율이 보임 (D012)

## 인터페이스·데이터

### 게임 동작의 출처

- 배율 단과 경계: 게임 설치 폴더의 `KSP_x64_Data/level5`·`level7`·`sharedassets8.assets` 세 곳에 `TimeWarp`의 저장된 값이 있고 모두 같다(파일에서 배율 단의 바이트 열을 찾아 이어지는 필드를 읽음). 레일 배율 단 1·5·10·50·100·1000·10000·100000, 고도 제한 계수 0·0.05·0.05·0.1·0.2·0.4·0.8·1, 물리 배율 단 1·2·3·4, `maxPhysicsRate_index` 0, `maxModeSwitchRate_index` 0(필드 순서는 디컴파일 `TimeWarp` 35~47행). 그래서 레일에 오르는 경계값은 1이다. 구현은 이 값을 적어 두지 않고 게임에서 읽는다.
- 배율 설정: `TimeWarp.SetRate`(공개 정적, 1204행)는 `setRate`(1227행)를 불러 높은 시간 가속이면 `getMaxOnRailsRateIdx`(1440행)로 줄인 뒤 적용한다. 즉시이면 그 자리에서 `updateRate`(1110행)가 `Planetarium.TimeScale`·`Time.timeScale`·`Time.fixedDeltaTime`을 바꾸고, 아니면 `TimeWarp.FixedUpdate`(1037행)가 1초에 걸쳐 바꾼다. 전환의 시작값은 지금 배율이 아니라 바로 앞 목표 배율(`last_rate = tgt_rate`)이다(`assumeWarpRate`). 그래서 전환 중에는 올리지 않는다.
- 게임이 스스로 낮춤: `TimeWarp.FixedUpdate`는 대기·추락 이유면 즉시, 다른 이유(영향권 등)면 1초에 걸쳐 내린다(`setRate`의 `instantIfLower`). 1초에 걸쳐 내릴 때 같은 `FixedUpdate`가 시작값(앞 목표 배율)을 배율로 적용하므로, 올리는 도중이었으면 첫 프레임의 배율이 앞 목표 배율이 된다.
- 영향권 상한: 게임 설정 `ORBIT_WARP_DOWN_AT_SOI`가 켜져 있고 `Vessel.PatchedConicsAttached`이며 `Orbit.patchEndTransition`이 `FINAL`이 아니면, 단 3보다 높은 단은 전환(`Orbit.EndUT`)까지 남은 시간이 `(지금 배율 + 단 3의 배율) ÷ 2 + 50`초보다 짧을 때 단 3으로 줄인다. 지금 배율이 단 3의 배율과 같으면 앞 항은 0이다(`getMaxOnRailsRateIdx`의 끝부분, `ClampRateToOrbitTransitions` 1947행, `getRateChangeTravel`). 그래서 50배로 내려오면 상한이 다시 풀린다.
- 일시정지: `FlightDriver.SetPause(true)`는 `Time.timeScale`을 0으로 한 번 쓰고(990행), 풀 때 `SetRate(CurrentRateIndex, 즉시)`를 부른다(1013행). `updateRate`는 일시정지를 보지 않고 `Time.timeScale`을 쓴다(1110행). `abort_plan`과 다시 읽기는 `BridgeLoader.Update`에서 처리되어 일시정지 중에도 실행된다.
- 방식의 자동 전환: `TimeWarp.Update`(520행)는 배율 단이 `maxModeSwitchRate_index`(0) 이하이고 전환이 끝났을 때만 방식을 바꾼다. 그래서 단이 1 이상이면 방식이 그대로다.
- 자동 가속: 시작 예약은 공개 필드 `setAutoWarp`, 진행 중은 `CancelAutoWarp()`(2154행)로 끈다. 진행 중이 아니면 `CancelAutoWarp`는 아무것도 하지 않고 거짓을 돌려준다.
- 레일 이탈: `OrbitPhysicsManager.LateUpdate`(270행)가 렌더 프레임마다 `Vessel.GoOffRails`(5624행)를 부른다. 기체가 생긴 뒤 25(착지 75) 렌더 프레임이 지나지 않았거나(`PhysicsHoldExpired` 3476행), `holdVesselUnpack`이 켜져 있거나, 착지 상태에서 기준 좌표계가 돌고 있지 않으면 미뤄진다. 실제로 걸리는 시간은 미확인이다(위험 1).
- 스로틀: `FlightInputHandler.state.mainThrottle`이 프레임 사이에 이어지고 `FlightInputHandler`는 매 프레임 그 값으로 스로틀을 다시 만든다(`FlightInputHandler` 1212·1988행). 그래서 이 값과 `Vessel.ctrlState.mainThrottle`을 함께 쓰면 유지된다(`src/KspBridge.Mod.Modules.Control/ThrottleStep.cs`의 `Write`와 같다).
- Ap·Pe까지 남은 시간: 닫힌 궤도는 `Orbit.timeToAp`·`timeToPe`가 다음 지점까지의 시간(0 이상)이고, 열린 궤도는 `timeToPe`가 Pe를 지난 뒤 음수다(디컴파일 `Orbit` 700~740행).
- 물리 프레임: `Coordinator.FixedUpdate`는 Loader의 `FixedUpdate`에서 불린다(`src/KspBridge.Mod.Loader/BridgeLoader.cs`). UT는 `Planetarium.FixedUpdate`(323행)에서 물리 프레임마다 `fixedDeltaTime × TimeScale`만큼 간다. 두 `FixedUpdate`의 순서는 미확인이며, 배율을 바꾼 효과가 한 프레임 늦어도 여유 10프레임 안이다.

### `warp` 인자

```json
{ "type": "warp", "params": { "to": "apoapsis", "lead_s": 30, "max_rate": 1000, "physics_rate": 2 }, "timeout_s": 4000 }
```

- 허용 키는 `seconds`·`ut`·`to`·`lead_s`·`max_rate`·`physics_rate`뿐이다. 목표 셋 가운데 정확히 하나. 수는 유한해야 한다. `physics_rate`는 정수.

### 목표(`WarpTarget`)

- 입력: 종류, 값, 시작 UT, 착지·착수 여부, 이심률, Ap 반지름, 영향권 반지름, `timeToAp`, `timeToPe`.
- `seconds`: 시작 UT + 값. `ut`: 값. `apoapsis`: 시작 UT + `timeToAp` − `lead_s`. `periapsis`: 시작 UT + `timeToPe` − `lead_s`.
- 실패(C4): 착지·착수이면서 `to`, 이심률 1 이상이거나 Ap 반지름이 영향권 반지름 이상인 `apoapsis`, 이심률 1 이상이고 `timeToPe`가 음수인 `periapsis`, 값이 수가 아닌 경우. 활성 기체가 없는 실패는 `WarpSession`이 한다.

### 배율 규칙(`WarpRule`)

- `RailsWanted(남은 시간, 영향권 전환까지 남은 시간, 배율 단, max_rate)`: `max_rate` 이하이면서 남은 시간이 `2 + 10 × 0.02 × 배율` 이상이고, 단이 3보다 높으면 영향권 전환까지 남은 시간이 `(배율 + 단 3의 배율) ÷ 2 + 50 + 10 × 0.02 × 배율` 이상인 가장 높은 단. 없으면 0. 전환까지 남은 시간은 위 영향권 상한의 세 조건이 모두 맞을 때만 있고, 없으면 그 조건을 보지 않는다. 남은 시간은 줄기만 하므로 한 번 낮아진 단은 전환 전에 다시 올라가지 않는다(D011).
- `PhysicsWanted(남은 시간, 물리 배율 단, physics_rate)`: `physics_rate` 이하이면서 남은 시간이 `0.2 + 10 × 0.02 × 배율` 이상인 가장 높은 단. 없으면 0.
- `Decide`: 지금 방식·단·배율, 전환이 끝났는지, 레일 상한(물어본 경우), 막혔는지로 명령(없음, 1배로, 방식을 바꾸고 올림, 즉시 내림, 올림)을 낸다.
  1. 막혔으면 목표는 물리 `PhysicsWanted`(게임 설정이 막거나 `physics_rate`가 없으면 0), 아니면 레일 `min(RailsWanted, 레일 상한)`이다.
  2. 목표 단이 0이면, 단이 0보다 크거나 배율이 1보다 클 때 즉시 단 0으로 내린다.
  3. 방식이 다르면 먼저 단 0으로 즉시 내리고 방식을 바꾼 뒤 목표 단으로 올린다.
  4. 단이 목표보다 높거나 배율이 목표 단의 배율보다 크면 즉시 목표 단으로 내린다.
  5. 단이 목표보다 낮고 전환이 끝났으면 목표 단으로 올린다.
- 레일 가속 중이면서 단이 `RailsWanted` 이상이면 레일 상한을 묻지 않는다(막히지 않은 것). 그 밖에는 `max(1, RailsWanted)`단을 물어 0이면 막힌 것이다. 게임 설정 `CanTimeWarpHigh`가 막으면 묻지 않고 막힌 것이며 이유는 `game_setting`이다.
- 단계가 시작된 뒤 25 물리 프레임(`WarpSession.FirstFrames`) 동안은, 막히지 않았고 레일 위가 아니면 목표 단을 0으로 한다(막혔으면 위와 같이 물리 배율). 게임은 `geeForce`를 최근 20프레임 가속도의 평균으로 정하고(디컴파일 `Vessel` 249행 `accelSmoothingLength`, 7558행) 레일 위에서는 0으로 두므로(7649행), 추력을 켠 바로 뒤에 레일에 오르면 막힘이 드러나지 않고 연소가 멈춘다. 20프레임에 5프레임을 더한 값이다(D017).

### 단계의 진행(`WarpSession`)

- `Start(UT)`: 시작할 때의 스로틀과 실제 시각을 기억하고 목표 UT를 정한다. 실패면 다음 `Tick`이 실패를 낸다.
- `Tick(UT)`(매 물리 프레임): 활성 기체가 없으면 실패. 자동 가속을 끈다. 배율 1·단 0·레일 위 아님·UT ≥ 목표 UT이면 스로틀이 시작 때와 다르면 되돌리고 완료. 아니면 배율·방식의 최대와 막힌 이유를 적고 `Decide`의 명령을 실행한다.
- `Stop(사유)`: 완료가 아니면 자동 가속을 끄고, 단이 0보다 크거나 배율이 1보다 크면 단 0으로 즉시 내린다. 내리기 전 배율이 1보다 컸으면 그 값을 돌려준다. `TimeWarp.fetch`가 없으면 아무것도 하지 않는다.
- 배율 설정(`GameWarp`): 앞의 `Time.timeScale`이 0이었으면 `SetRate` 직후 다시 0으로 둔다(D012).
- 막힌 이유의 이름: 게임의 `ClearToSaveStatus` 이름에서 앞의 `NOT_WHILE_`·`NOT_`을 빼고 소문자로 쓴다(`in_atmosphere`, `under_acceleration`, `moving_over_surface`, `about_to_crash`, `on_a_ladder`). 게임 설정이면 `game_setting`, 이유 없이 0이면 `unknown`.

### 이벤트와 진행

- `warp_completed`: `index`, `target_ut`, `ut`, `error_s`, `max_rails_rate`, `max_physics_rate`, `real_s`, `blocked`(이름 목록).
- `warp_released`: `index`, `reason`, `rate`.
- `step_progress`: `target_ut`, `remaining_s`, `time_warp`(`rate`·`mode`·`on_rails`), `blocked`(지금 막힌 이유, 없으면 `null`).
- `get_flight`의 `time_warp`: `rate`, `mode`(배율이 1이면 `none`, 아니면 높은 시간 가속은 `rails`, 물리는 `physics`), `on_rails`.

### 단계(`WarpStep`)

- `Begin`에서 계획·단계 번호를 기억하고 `Start`, `Judge`에서 `Tick`, `End`에서 `Stop`을 부른다. `Control`은 아무것도 하지 않는다. `End`의 이벤트는 되돌리기 전 배율이 1보다 클 때만 남긴다.
- `Begin`이 예외를 던져도 코어가 `End`를 부르므로(`Coordinator.Finish`의 `SafeEnd`, 단계가 `running`일 때) 배율이 1로 돌아온다.

## 오류·위험

1. 1배로 내린 뒤 2초 안에 레일에서 내려온다는 것은 미확인이다. 더 걸리면 목표를 지나 완료된다(중단되지는 않는다). 알아채는 방법: C12 각 항목의 `error_s`(0.1초 초과). 어긋나면 여유를 실측에 맞게 고친다(plan 의도 변경).
2. 되돌린 스로틀이 유지되는지, 레일에서 스로틀이 0이 되는지는 게임에서 미확인이다. 알아채는 방법: C12 "스로틀과 자세"·"발사대" 항목의 스로틀.
3. 발사대에서 레일을 쓴 뒤 기체가 다시 놓이며(`GoOffRails`의 지면 배치) 움직일 수 있다. 미확인. 알아채는 방법: C12 발사대 항목의 해발 고도와 상황.
4. 레일에서 내려온 뒤 자세 유지가 이어지는지는 미확인이다(게임이 각속도를 어떻게 되살리는지 보지 않았다). 알아채는 방법: C12 "스로틀과 자세"의 각도.
5. 장면 전환이 요청된 때(`onGameSceneLoadRequested`)와 기체가 바뀌는 때(`onVesselChange`)에 `SetRate`를 부르는 것이 안전한지는 미확인이다(`SetRate`가 부르는 `FlightLogger.IgnoreGeeForces`는 대상이 없으면 바로 돌아온다, 디컴파일 `FlightLogger` 420행). 알아채는 방법: C12 중단 항목의 `debug_revert`·`debug_space_center` 뒤 응답. `vessel_change`는 게임에서 확인하지 않는다.
6. `getMaxOnRailsRateIdx`를 리플렉션으로 찾지 못하면 단계가 예외로 실패한다(`step_failed`). 알아채는 방법: C12 시간 항목.
7. 추력을 끈 직후 게임의 가속도 값(`geeForce`)이 가라앉기 전에는 `under_acceleration`으로 막힌다. 막힌 동안 1배로 진행하고 풀리면 올리므로 결과에 영향이 없고 `blocked`에 남는다. 가라앉는 시간은 미확인이다. 알아채는 방법: C12의 `blocked`.
8. 탐사선이 레일 가속 중에도 전기를 써서 C12 "시각"(1,000,000초)에서 전기가 다 떨어질 수 있다(미확인). 시간 가속은 조종 가능을 요구하지 않으므로 도착 판정에는 영향이 없다. 자세 항목은 되돌린 비행에서 따로 한다. 알아채는 방법: `get_flight`의 자원.
9. 추력을 켠 직후의 기다림(25프레임)은 게임에서 미확인이다. 알아채는 방법: 궤도에서 `stage` → `warp`(`seconds` 8, `physics_rate` 4)의 배율 최대.
10. `get_flight`에 값을 더하다 다른 값이 바뀔 위험. 알아채는 방법: `FlightReportTests`와, 발사대에서 게시 전과 뒤의 `get_flight` 비교.
11. 일시정지 중의 중단·다시 읽기에서 멈춘 상태가 지켜지는지는 게임에서 확인하지 못한다(일시정지를 만드는 MCP 도구가 없다). 구현 후 검토에서 코드로 확인한다.
11. 영향권 전환이 게임의 계산과 다르게 오거나(`Orbit.EndUT`의 오차), 전환 때 새 궤도의 값이 한 프레임 늦게 바뀌는지는 미확인이다. 게임의 상한이 그 뒤에도 적용되므로 50배를 넘지 않는다고 보지만 게임에서 확인하지 않는다(spec C12 끝).

## 검증 전략

빌드와 단위 테스트:

- `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과한다.
- C2: `WarpArgsTests` — 목표 조합, 범위 경계, 종류, 모르는 인자.
- C3·C4: `WarpTargetTests` — 목표 종류별 UT, `lead_s`, 실패 조건. 활성 기체 없음은 `WarpSessionTests`.
- C5: `WarpRuleTests` — `max_rate`를 넘지 않는 가장 높은 단, 남은 시간의 경계, 물리 단, 막힘, 방식 바꿈, 즉시 내림, 전환 중에는 올리지 않음, 영향권 전환까지 남은 시간의 경계. `WarpSessionTests` — 게임·사람이 배율을 내리거나 올린 뒤 다시 맞춤, 자동 가속 끄기, 막힌 뒤 레일로 돌아감, 시작 뒤 25프레임의 기다림(추력을 켠 직후, 막히지 않을 때, 이미 레일 위일 때), 게임 설정이 막은 레일·물리, 목표가 영향권 전환 뒤일 때 여러 남은 시간(500초부터 30만 초)과 두 실행 순서에서 전환을 단 3 이하로 넘고 전환 전에 배율을 다시 올리지 않음.
- C6·C7: `WarpSessionTests` — 여러 남은 시간(1초부터 1억 초)에서 완료 때의 오차가 한 물리 프레임 이하, 이미 지난 목표, 사람이 켜 둔 가속에서 시작, 레일에서 늦게 내려옴, 시작 실패·중단에서 1배로 되돌림과 되돌리기 전 배율, 완료에서는 되돌리지 않음.
- C8: `WarpSessionTests` — 스로틀이 달라졌으면 완료할 때 되돌림, 같으면 쓰지 않음, 중단에서는 쓰지 않음.
- C9: 단계의 예외가 그 계획만 `step_failed`로 끝내는 것은 코어의 기존 시험(`tests/KspBridge.Mod.Core.Tests/CoordinatorTests.cs`의 `BeginException_FailsOnlyThatPlan`)이 다루고, 그때 `End`가 불리는 것은 `Coordinator.Finish`의 코드로 확인한다. `WarpSessionTests`는 `Tick`의 예외 뒤 `Stop`이 1배로 되돌리는 것을 본다.
- C10: `WarpSessionTests` — 배율 최대, 막힌 이유의 목록(한 번만), 남은 시간이 0 미만이 되지 않음.
- C11: `FlightReportTests` — 기본 응답의 키, `none`·`rails`·`physics`.

실제 게임(실제 `mcp__kspbridge__*` 호출, 판정은 응답만으로):

- 준비: `get_status`로 다른 비행이 없는지 보고 `debug_save` 이름 `before-34`로 저장한다. 게시 전 발사대의 `get_flight`(`include` 셋 포함)를 적는다.
- 배포: `dotnet publish src/KspBridge.Mod.Core -c Release`와 `dotnet publish src/KspBridge.Server.Modules.Warp -c Release`로 게시하고 핫리로드로 반영한다.
- 시험 기체: 모든 항목에 무인 `C33 Loss Test`(탐사선 코어·반작용 휠·배터리·고체 부스터 2단, docs/features/flight-condition.md R10)를 쓴다.
- C1: `get_status`의 서버·Mod 모듈에 `warp`가 있고 `list_step_types`의 `warp` 설명에 C1의 내용이 있다.
- C11: 게시 전과 뒤의 `get_flight`를 비교한다(시각과 전기 현재량, 서 있는 기체에서 잡음이 큰 `time_to_apoapsis_s`·`heading_deg`·`roll_deg` 제외).
- C12: 시험이 끝나면 `debug_load` `before-34`로 되돌린다. 한 비행에서 여러 항목을 이어 해도 된다(D016).
  - 궤도 항목(시간, 시각, 최대 배율, Ap·Pe, 이미 지난 목표, 짧은 목표, 스로틀과 자세, 중단, 시간 초과)은 `C33 Loss Test`를 발사대에 올려 `debug_set_orbit`하고, `get_flight`의 `time_warp`가 레일 위 아님인 것을 본 뒤 제출한다(D014). "스로틀과 자세"는 전기가 남은 비행에서 한다.
  - 중단의 `debug_space_center` 뒤에는 `debug_launch`로 다시 발사대에 올려 `get_flight`를 본다. 다시 읽기는 `reload`를 쓴다.
  - 발사대와 실패 경로는 `C33 Loss Test`로 한다.
  - 물리 가속: `C33 Loss Test`를 발사대에 올려 `debug_set_orbit`(Ap 75 km·Pe 71 km)하고 레일 위 아님을 본 뒤, 계획 `attitude`(retrograde) → `throttle` 1 → `stage` → `wait_until`(`all`: `altitude_m` <= 65000, `timeout_s` 300) → `warp`(`seconds` 20, `physics_rate` 4)를 제출한다. 역추진과 낙하는 기체를 70 km 아래로 내리는 준비이며 판정하지 않는다(D015).
- C13: 기준 spec 반영을 구현 후 검토에서 확인하고, 동작은 C12 "스로틀과 자세"·"중단"으로 본다. 중단 뒤의 스로틀(`throttle` 단계 없는 계획)은 `get_flight`로 읽어 기록한다.
- spec C12 끝의 항목은 게임에서 만들지 않는다.

## 원칙 점검

- 적용 규칙:
  - `workflow/spec-driven.md`와 하위 문서, `workflow/repository-content.md`(저장소 기준 상대 경로, 개인 경로 없음)
  - `git/github-access.md`·`git/github-issue.md`(이슈 #34), `git/commit-message.md`
  - `dotnet/project-creation.md`(`src/`·`tests/` 배치, `{Solution}.{역할}` 이름, MSTest, 솔루션에 포함), `dotnet/nuget-package-selection.md`(새 패키지 없음)
  - `shell/powershell-windows.md`(명령 실행), `workflow/conversation-language.md`(대화 언어)
  - 전역 규칙 MCP 도구 검증(게임 검증은 실제 `mcp__kspbridge__*` 호출로만 판정)
  - 저장소 규칙 파일은 없다. `ReadMe.md` "기능 모듈 추가"는 기능을 코어 코드를 고치지 않고 모듈로 더한다고 한다. 이 계획은 코어(`KspBridge.Mod.Core`)를 고치지 않는다.
- 충돌: `dotnet/project-creation.md` 1번(`net10.0`)을 Mod 모듈과 그 테스트는 따르지 않고 `net472`를 쓴다. 게임(KSP 1.12.5, Unity Mono)이 읽는 어셈블리여야 하고 기존 Mod 프로젝트가 모두 그렇다.
