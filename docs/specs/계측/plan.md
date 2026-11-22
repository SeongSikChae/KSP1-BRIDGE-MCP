# 게임 검증용 계측 계획

## 요약

계측 기능을 새 Mod 모듈과 MCP 서버 모듈 쌍(`measure`)으로 더한다. 코어와 기존 모듈의 파일은 바꾸지 않는다.

- Mod 모듈은 게임에 닿지 않는 부분(계측 상태 기계, 요약·표본 조회, 각속도 부호 변환, 인자 검증)과 게임에 닿는 부분(표본 읽기, 타이밍 훅, 게임 이벤트)으로 나눈다. 앞쪽은 단위 테스트로 확인하고 뒤쪽은 실제 게임에서 MCP 응답으로 확인한다.
- 표본은 게임의 타이밍 훅(`TimingManager.FixedUpdateAdd`)으로 물리 프레임마다 모은다(D009).
- 자세와 스테이지 계산은 flight 모듈의 공개 형식을 참조해 쓴다(D010).

이 문서에서 "디컴파일"은 게임 설치 폴더의 `KSP_x64_Data/Managed/Assembly-CSharp.dll`(KSP 1.12.5)을 `ilspycmd -t <형식> <dll>`로 풀어 읽은 것을 뜻한다.

## 바꿀 파일·구조

새 파일:

- src/KspBridge.Mod.Modules.Measure/KspBridge.Mod.Modules.Measure.csproj: Mod 계측 모듈 프로젝트. `src/KspBridge.Mod.Modules.Flight/KspBridge.Mod.Modules.Flight.csproj`와 같은 구성(net472, 게임 어셈블리 참조, 세대 표식 이름)에 flight 모듈 프로젝트 참조와 테스트용 `InternalsVisibleTo`를 더한다.
- src/KspBridge.Mod.Modules.Measure/MeasureModule.cs: 모듈 등록. 요청 처리(`start`·`stop`·`get`), 단계 종류 등록, 타이밍 훅 걸기·떼기, 게임 이벤트 구독.
- src/KspBridge.Mod.Modules.Measure/MeasureSteps.cs: 계획 단계 종류 `measure_start`·`measure_stop`.
- src/KspBridge.Mod.Modules.Measure/Recorder.cs: 계측 상태 기계(게임에 닿지 않음). 실행 중 계측과 가장 최근에 끝난 계측, 번호, 표본 상한, 종료 사유.
- src/KspBridge.Mod.Modules.Measure/Sample.cs: 표본 한 벌의 값과 항목 표(이름, 수치/이산 구분, 값 읽는 법).
- src/KspBridge.Mod.Modules.Measure/MeasureReport.cs: 조회 응답 만들기(게임에 닿지 않음). 범위 고르기, 항목별 요약, 표본 추리기, 조회 인자 검증.
- src/KspBridge.Mod.Modules.Measure/MeasureArgs.cs: 표본 상한 인자 검증, 준비 판정, 게임 이벤트에서 종료 사유를 정하는 규칙(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Measure/AngularRate.cs: 게임 각속도 벡터를 피치·요·롤 각속도(°/s, D008 부호)로 바꾸는 계산(게임에 닿지 않음).
- src/KspBridge.Mod.Modules.Measure/SampleReader.cs: 활성 기체에서 표본을 읽는다(게임에 닿음).
- src/KspBridge.Server.Modules.Measure/KspBridge.Server.Modules.Measure.csproj: 서버 계측 모듈 프로젝트. `src/KspBridge.Server.Modules.Wait/KspBridge.Server.Modules.Wait.csproj`와 같은 구성.
- src/KspBridge.Server.Modules.Measure/MeasureTools.cs: MCP 도구 `measure_start`·`measure_stop`·`get_measurement`.
- tests/KspBridge.Mod.Modules.Measure.Tests/KspBridge.Mod.Modules.Measure.Tests.csproj: 테스트 프로젝트. `tests/KspBridge.Mod.Modules.Flight.Tests/KspBridge.Mod.Modules.Flight.Tests.csproj`와 같은 구성에 계측·flight 모듈 참조.
- tests/KspBridge.Mod.Modules.Measure.Tests/RecorderTests.cs, MeasureReportTests.cs, MeasureArgsTests.cs, AngularRateTests.cs: 단위 테스트.
- docs/features/flight-measurement.md: 새 기능의 기준 spec(마지막 구현 단계에서 만든다).

고치는 기존 파일:

- KspBridge.slnx: 새 프로젝트 셋을 더한다.
- ReadMe.md: 도구 표에 세 도구를 더하고, 계획 단계 `measure_start`·`measure_stop`과 표본 상한을 짧게 설명한다. 이 파일은 bridge-connection·debug-tools·deployment·editor·flight-plan·server-hosting 기준 spec의 `파일:` 줄에도 있다. 이번 수정은 도구 표의 행과 계측 설명만 더하고, deployment R9(게시 방법 설명)와 server-hosting R3(서비스 설명)이 요구하는 부분을 포함해 기존 내용을 바꾸지 않으므로 그 기능들의 요구는 바뀌지 않는다.

배포 구성(`Directory.Build.targets`)은 `src/KspBridge.Mod.Modules.*`와 `src/KspBridge.Server.Modules.*`를 이름 패턴으로 모으므로(`Directory.Build.targets`의 `_KspModLogicProject`·`_KspServerModuleProject`) 고치지 않는다. 새 패키지는 없어 `Directory.Packages.props`도 고치지 않는다.

## 결정

- 물리 프레임 콜백은 계측 모듈이 게임의 `TimingManager.FixedUpdateAdd`에 직접 건다: 코어를 바꾸지 않고 모듈만으로 더한다 / 검토한 대안: 코어 `ModuleContext`에 물리 프레임 구독을 더함 — 코어 파일과 flight-plan 기능이 바뀌어 고르지 않음 (D009)
- 자세·스테이지 계산은 flight 모듈을 프로젝트 참조해 `Attitude`·`StageRef`·`Vec`를 쓰고, 스테이징 아이콘 판정 한 줄은 계측 모듈에 같은 식으로 둔다: 계산이 한 곳에 있어 `get_flight`와 값이 어긋나지 않고 flight 모듈 파일을 바꾸지 않는다 / 검토한 대안: `IsStaged`를 공개로 바꿈 — flight-observation 파일이 바뀌어 고르지 않음. 계산을 복제 — 같은 계산이 두 벌이 되어 고르지 않음 (D010)
- 타이밍 단계는 `BetterLateThanNever`를 쓴다: 한 물리 프레임 안에서 가장 늦은 단계라는 이름이므로 그 프레임의 조종 입력 적용과 물리 계산 뒤의 값을 읽을 것으로 본다(실행 순서 값은 미확인, "오류·위험" 1) / 검토한 대안: `Normal` — 조종 입력 적용보다 먼저 읽을 수 있어 고르지 않음
- 표본은 값 형식(struct)의 목록에 담는다: 표본 180,000개를 객체로 두면 객체마다 머리 정보가 붙어 메모리가 늘고 가비지 수집 대상이 된다 / 검토한 대안: 항목별 배열 — 코드가 늘어나는 데 비해 얻는 것이 적어 고르지 않음
- 표본 목록 응답은 열 이름 한 번과 행 배열로 낸다: 표본마다 항목 이름을 되풀이하면 2,000개 조회 응답이 몇 배로 커진다 / 검토한 대안: 표본마다 이름 붙인 객체 — 응답 크기 때문에 고르지 않음
- 게임 시각이 직전 표본보다 진행하지 않은 물리 프레임에는 표본을 만들지 않는다: spec C5의 "표본의 UT는 순증가"를 지킨다. 게임 시각은 물리 프레임마다 진행하므로(아래 출처) 정상 진행 중에는 프레임마다 표본이 하나다 / 검토한 대안: 같은 UT의 표본을 그대로 넣음 — 프레임 간격 0이 요약에 섞여 고르지 않음
- 준비 판정은 계측 모듈에 따로 둔다: flight 모듈의 `FlightReadiness.Judge`는 이유 문장에 `get_flight`를 적어(`src/KspBridge.Mod.Modules.Flight/FlightReadiness.cs` 30행) 그대로 쓸 수 없다 / 검토한 대안: flight 모듈의 판정을 고쳐 함께 씀 — flight-observation 파일이 바뀌어 고르지 않음

## 인터페이스·데이터

### 게임 동작의 출처

- 타이밍 훅: `TimingManager.FixedUpdateAdd(TimingStage stage, UpdateAction action)`과 `FixedUpdateRemove`가 있고, 단계에 `BetterLateThanNever`가 있다. `TimingManager.Instance`가 없으면 `FixedUpdateAdd`는 아무것도 하지 않고 돌아온다(디컴파일 `TimingManager`).
- 게임 시각: `Planetarium.FixedUpdate`가 `time += fixedDeltaTime * timeScale`로 물리 프레임마다 시각을 진행시키고, `pause`이면 진행시키지 않는다(디컴파일 `Planetarium`). 그래서 물리 프레임 사이의 요청 처리 시점에 읽은 UT는 가장 최근 물리 프레임의 UT와 같다.
- 조종 입력: `FlightInputHandler.FixedUpdate`가 `vessel.SetControlState(state)` 뒤 `vessel.FeedInputFeed()`를 부르고, `Vessel.FeedInputFeed`는 `OnPreAutopilotUpdate`·`OnAutopilotUpdate`·`OnPostAutopilotUpdate`·`OnFlyByWire`에 `ctrlState`를 차례로 넘긴다(디컴파일 `FlightInputHandler`, `Vessel`). 그래서 `Vessel.ctrlState`의 `mainThrottle`·`pitch`·`yaw`·`roll`은 그 프레임에 기체에 적용된 값이다.
- 조종 입력의 부호: 키 `PITCH_UP`이 `pitch = 1f`, `PITCH_DOWN`이 `pitch = -1f`이고, 트림은 `PITCH_UP`·`YAW_RIGHT`·`ROLL_RIGHT`가 +쪽, `PITCH_DOWN`·`YAW_LEFT`·`ROLL_LEFT`가 −쪽이다(디컴파일 `FlightInputHandler.FixedUpdate`). 게임 값의 부호가 D008의 기준과 같으므로 그대로 낸다.
- 각속도: `VesselPrecalculate.CalculatePhysicsStats`가 `QuaternionD.Inverse(referenceTransform.rotation)`을 곱한 부품 각속도의 질량 가중 평균을 `vessel.angularVelocity`에 넣는다(디컴파일 `VesselPrecalculate`). 즉 기준 변환의 로컬 축 기준 각속도(rad/s)다. 기준 변환의 로컬 축은 기수가 +Y, 기체 위쪽이 −Z다(`src/KspBridge.Mod.Modules.Flight/FlightSnapshot.cs` 47~48행이 `reference.up`을 기수, `-reference.forward`를 위쪽으로 쓴다).
- 세대 안의 참조: 세대 로더가 `AppDomain.AssemblyResolve`에서 어셈블리 이름으로 지금 세대의 어셈블리를 돌려준다(`src/KspBridge.Mod.Loader/GenerationLoader.cs` `Resolve`, 102~106행).
- 계획 단계: `Coordinator.FixedUpdate`가 단계가 시작되는 물리 프레임에 `Begin`을 부르고 같은 호출에서 `Judge`를 불러, `Judge`가 완료를 돌려주면 그 프레임에 단계가 완료된다. `Begin`·`Judge`의 예외는 단계 실패(`step_failed`)가 된다. 계획 제출 때 `IStepType.Create`의 `BridgeException`은 제출 오류가 된다(`src/KspBridge.Mod.Core/Coordination/Coordinator.cs` 88~96행, 141~177행).
- 요청 처리 시점: 요청은 로더의 `Update`에서 처리된다(`src/KspBridge.Mod.Loader/BridgeLoader.cs` `Update`→`DrainRequests`).

### 각속도 부호 변환

기준 변환 로컬 축의 각속도 ω(rad/s)에서, 점 r의 속도는 v = ω × r이다. 기수 끝(0, 1, 0)의 속도는 (−ωz, 0, ωx)이고 오른쪽 날개 끝(1, 0, 0)의 속도는 (0, ωz, −ωy)다. D008 기준으로:

- 피치 각속도(기수가 위쪽 (0, 0, −1)으로 가는 방향이 +) = −ωx
- 요 각속도(기수가 오른쪽 (1, 0, 0)으로 가는 방향이 +) = −ωz
- 롤 각속도(오른쪽 날개가 아래쪽 (0, 0, 1)으로 가는 방향이 +) = −ωy

여기에 180/π를 곱해 °/s로 낸다. `AngularRate`는 이 식을 두고, 테스트는 `Vec`로 ω × r을 직접 계산해 세 방향의 부호를 확인한다. 게임의 각속도가 v = ω × r을 따른다는 전제는 미확인이다("오류·위험" 3).

### 계측 상태 기계(`Recorder`)

- 상태: 실행 중 계측(없을 수 있음), 가장 최근에 끝난 계측(없을 수 있음), 다음 계측 번호(1부터).
- `Start(시작 UT, 기체 이름, 표본 상한)`: 실행 중 계측이 있으면 `bad_request`. 없으면 끝난 계측을 버리고 새 계측을 실행 중으로 둔다.
- `Add(표본)`: 실행 중일 때만 넣는다. UT가 직전 표본의 UT 이하이면 넣지 않는다. 넣은 뒤 표본 수가 상한에 이르면 사유 `sample_limit`, 끝 UT를 그 표본의 UT로 끝낸다.
- `Stop(사유, 끝 UT, 오류 설명)`: 실행 중 계측을 끝난 계측으로 옮긴다. 실행 중 계측이 없으면 아무것도 하지 않고 없었다고 알린다(도구·단계는 이때 `bad_request`를 낸다).
- 종료 사유: `stopped`, `sample_limit`, `new_flight`, `scene_change`, `vessel_change`, `error`.
- 세대가 바뀌면 `Recorder`는 새로 만들어지므로 계측이 남지 않는다(spec C9).

### 표본 항목

| 응답 이름 | 뜻 | 종류 | 읽는 곳 |
|---|---|---|---|
| `ut` | 게임 시각(s) | 시각 | `Planetarium.GetUniversalTime()` |
| `t_s` | 경과 시간(s) | 시각 | `ut` − 계측 시작 UT |
| `throttle` | 스로틀(0~1) | 수치 | `Vessel.ctrlState.mainThrottle` |
| `sas`, `rcs` | 켜짐 여부 | 이산 | `Vessel.ActionGroups[KSPActionGroup.SAS / RCS]` |
| `pitch_input`, `yaw_input`, `roll_input` | 조종 입력(−1~1) | 수치 | `Vessel.ctrlState.pitch / yaw / roll` |
| `mass_t` | 질량(t) | 수치 | `Vessel.GetTotalMass()` |
| `thrust_kn` | 추력 합(kN) | 수치 | 부품의 `ModuleEngines.finalThrust` 합 |
| `pitch_deg`, `heading_deg`, `roll_deg` | 지표 기준 자세(°) | 수치 | `Attitude.Compute` |
| `pitch_rate_dps`, `yaw_rate_dps`, `roll_rate_dps` | 각속도(°/s) | 수치 | `Vessel.angularVelocity`를 `AngularRate`로 변환 |
| `stage` | 마지막으로 작동한 스테이지 | 이산 | `StageRef.Judge(...).LastActivated` |
| `altitude_m` | 해발 고도(m) | 수치 | `Vessel.altitude` |
| `vertical_speed_mps`, `surface_speed_mps`, `orbital_speed_mps` | 속도(m/s) | 수치 | `Vessel.verticalSpeed`, `srfSpeed`, `obt_speed` |

스로틀·질량·추력·자세·고도·속도·SAS·RCS를 읽는 곳은 `get_flight`와 같다(`src/KspBridge.Mod.Modules.Flight/FlightSnapshot.cs` `Take`). 스테이징 아이콘 판정은 `FlightSnapshot.IsStaged`와 같은 식을 계측 모듈에 두고 출처를 주석에 적는다(D010).

### Mod 요청(`POST /api/measure/{op}`)

- `start` 인자 `sample_limit`(선택). 응답: `id`, `start_ut`, `sample_limit`, `vessel`.
- `stop` 인자 없음. 응답: `id`, `start_ut`, `end_ut`, `sample_count`, `end_reason`.
- `get` 인자 `from_s`·`to_s`(선택), `samples`(선택, 기본 false), `items`(선택), `max_samples`(선택, 기본 200). 응답:

```json
{
  "measurement": {
    "id": 3, "state": "ended", "vessel": "Kerbal X",
    "start_ut": 100.0, "end_ut": 105.04, "end_reason": "stopped", "error": null,
    "sample_count": 252, "sample_limit": 30000,
    "range": { "from_s": null, "to_s": null, "sample_count": 252 },
    "frame_dt_s": { "min": 0.02, "max": 0.02 },
    "summary": {
      "throttle": { "first": 0, "last": 0, "min": 0, "min_t_s": 0.02, "max": 0, "max_t_s": 0.02, "mean": 0 },
      "stage": { "first": null, "last": null, "changes": [] }
    },
    "samples": { "stride": 26, "columns": ["t_s", "ut", "throttle"], "rows": [[0.02, 100.02, 0]] }
  }
}
```

- 계측이 하나도 없으면 `{"measurement": null}`이다.
- `state`는 `running` 또는 `ended`다. 실행 중이면 `end_ut`·`end_reason`은 `null`이다.
- `sample_count`가 spec의 "계측 전체 표본 수", `range.sample_count`가 "범위 안 표본 수"다.
- 범위는 `from_s` ≤ `t_s` ≤ `to_s`인 표본이다(생략한 쪽은 제한 없음). 범위에 표본이 없으면 `summary`와 `frame_dt_s`는 `null`이다. 범위 안 표본이 하나면 `frame_dt_s`는 `null`이다.
- 수치 항목의 최소·최대가 여러 표본에서 같으면 가장 이른 표본의 경과 시간을 낸다.
- `changes`는 범위 안에서 직전 표본과 값이 달라진 표본마다 `{"t_s", "value"}`다.
- `samples`는 `samples`가 true일 때만 있다. `columns`는 `t_s`, `ut`, 그다음 고른 항목 순이다. 범위 안 표본 수 N이 `max_samples` 이하이면 `stride`는 1, 넘으면 `stride`는 N ÷ `max_samples`의 올림이고 범위의 0번째, `stride`번째, 2 × `stride`번째… 표본을 낸다.
- 인자 검증(`bad_request`): `sample_limit`이 정수가 아니거나 1 미만이거나 180,000 초과. `items`에 모르는 이름(`ut`·`t_s`는 늘 나가므로 항목 이름으로 받지 않는다). `max_samples`가 정수가 아니거나 1 미만이거나 2,000 초과. `from_s` > `to_s`.

### 계획 단계

- `measure_start`: `params.sample_limit`(선택). `Create`에서 상한을 검증해 틀리면 제출이 `bad_request`가 된다. `Begin`에서 활성 기체가 없으면 예외로 단계가 실패하고, 있으면 `Recorder.Start`를 불러 훅을 건다(실행 중 계측이 있으면 `bad_request` 예외로 단계 실패). `Judge`는 곧바로 완료를 돌려준다. `Control`은 입력을 넣지 않는다.
- `measure_stop`: `Begin`에서 `Recorder.Stop("stopped", …)`을 부르고 실행 중 계측이 없었으면 예외로 단계가 실패한다. `Judge`는 곧바로 완료를 돌려준다.

### 흐름

- 시작(도구): 준비 판정(게임이 없음, 코어의 장면 차단 이유, 비행 장면 아님(장면 이름), 활성 기체 없음 → `not_ready`) → `Recorder.Start` → `TimingManager.FixedUpdateAdd(BetterLateThanNever, 콜백)`. 시작 UT는 그 시점의 `Planetarium.GetUniversalTime()`이다.
- 시작(단계): 시작 UT는 단계 문맥의 UT(`StepContext.UT`)다.
- 콜백(물리 프레임마다): 실행 중이 아니면 훅을 떼고 돌아온다. 활성 기체가 없으면 사유 `error`로 끝낸다. 표본을 읽어 `Recorder.Add`에 넘긴다. 예외는 모두 잡아 사유 `error`와 예외 종류·메시지로 끝내며 게임으로 던지지 않는다. 계측이 끝나면 `FixedUpdateRemove`로 훅을 뗀다.
- 게임 이벤트: `GameEvents.onGameSceneLoadRequested`에서 지금 비행 장면이고 요청 장면도 비행이면 `new_flight`, 아니면 `scene_change`로 끝낸다(코어 `FlightSession.OnLoadRequested`와 같은 규칙, `src/KspBridge.Mod.Core/Session/FlightSession.cs` 55~62행). `GameEvents.onVesselChange`에서 `vessel_change`로 끝낸다. 이벤트 종류와 장면 값을 받아 사유를 돌려주는 규칙은 `MeasureArgs.cs`의 게임에 닿지 않는 함수로 두고, `MeasureModule`은 이벤트에서 값을 읽어 그 함수에 넘기기만 한다(D011).
- 해제: 모듈 등록 때 `context.Subscriptions`에 `FixedUpdateRemove` 호출과 두 게임 이벤트를 등록해, 세대가 멈출 때 함께 풀린다.

### MCP 도구(`MeasureTools`)

- `measure_start(sample_limit?: int)` → Mod `measure/start`
- `measure_stop()` → Mod `measure/stop`
- `get_measurement(from_s?: number, to_s?: number, samples?: bool, items?: string[], max_samples?: int)` → Mod `measure/get`. 읽기 전용 표시를 단다.

도구 설명에 항목 이름과 단위, 각속도·조종 입력의 + 방향, 헤딩·롤 평균이 각도 넘김을 보정하지 않는다는 사실, 표본 상한의 기본값과 최대값을 적는다.

## 오류·위험

1. 타이밍 단계의 실행 순서는 미확인이다(디컴파일에 순서 값이 없다). 설계는 `BetterLateThanNever` 콜백이 그 프레임의 조종 입력 적용 뒤에 실행된다고 본다. 짐작과 다르면 표본의 조종 입력·스로틀이 한 프레임 앞선 값이 된다. 알아채는 방법: 이번 작업에서는 확인할 수단이 없고(조종 입력을 바꾸는 도구가 없음), 제어 기능의 게임 검증(#32)에서 입력 변화와 표본의 프레임 차이로 드러난다. 코어의 단계 실행과 이 콜백 사이의 순서도 미확인이며, 순서에 따라 첫 표본이 시작한 프레임 또는 다음 프레임이 된다. spec의 완료 조건(C5, C11 구간 경계)은 어느 쪽이든 채워진다.
2. 비행 장면에서 `TimingManager.Instance`가 있다는 것은 미확인이다. 없으면 훅이 걸리지 않고 표본이 하나도 모이지 않는다. 알아채는 방법: spec C11 구간 경계의 "계측 전체 표본 수가 2 이상" 확인이 실패한다.
3. 게임 각속도가 v = ω × r을 따른다는 전제와 그에 따른 부호(−ωx, −ωz, −ωy)는 미확인이다. 다르면 각속도의 부호가 뒤집힌다. 알아채는 방법: 이번 작업에서는 정지 상태의 크기만 확인하므로 드러나지 않고, 제어 기능의 게임 검증(#32)에서 조종 입력의 부호와 각속도의 부호가 반대로 나오면 드러난다.
4. 모듈 사이 프로젝트 참조는 이 저장소에서 처음이다. 세대 로더가 이름으로 풀어 주게 돼 있지만 게임(Unity Mono)에서 실제로 풀리는지는 미확인이다. 알아채는 방법: 배포 뒤 `get_status`의 세대 정보와 `list_step_types`에 `measure_start`가 나오는지, `measure_start` 호출이 형식 로드 오류 없이 응답하는지로 확인한다(spec C1).
5. 표본 180,000개의 메모리는 추정(수십 MB)이며 실측하지 않는다. 게임 검증은 작은 상한으로만 한다.
6. 물리 프레임마다 모든 부품의 엔진 모듈과 스테이징 아이콘을 훑는다. 부품이 많은 기체에서 프레임 시간이 늘 수 있다. 훑을 때 목록을 새로 만들지 않도록 부품의 모듈 목록을 직접 돈다. 알아채는 방법: 계측 요약의 프레임 간격은 게임 시간이라 드러나지 않으므로, 이번 작업에서는 확인하지 않는다.
7. 계측 중 Mod를 다시 읽으면 데이터가 사라진다(spec C9대로). 검증 중 배포를 겹치지 않는다.

## 검증 전략

단위 테스트와 빌드:

- `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`가 통과한다.
- C2·C3·C9(상한·교체·사유): `RecorderTests` — 시작·끝, 실행 중 재시작 거절, 끝난 계측 교체, 상한 도달 시 `sample_limit`와 표본 수, 진행하지 않은 UT 건너뜀.
- C7·C8: `MeasureReportTests` — 전체·범위 요약, 빈 범위, 이산 항목의 변화 목록, 프레임 간격, 표본 추리기와 `stride`, 항목 선택, 계측 없음.
- C10(인자): `MeasureArgsTests` — 표본 상한의 경계값, 준비 판정의 이유 문장. `MeasureReportTests` — 조회 인자 오류.
- C9(이벤트와 사유의 연결): `MeasureArgsTests` — 비행 장면에서 비행 장면 요청은 `new_flight`, 그 밖의 장면 요청은 `scene_change`, 활성 기체 변경은 `vessel_change`.
- C6(각속도 부호): `AngularRateTests`.

실제 게임(MCP 응답만으로, `mcp__kspbridge__*` 도구 호출):

- 배포: Mod 세트(`dotnet publish src/KspBridge.Mod.Core -c Release`)와 서버 모듈(`dotnet publish src/KspBridge.Server.Modules.Measure -c Release`)을 게시하고 핫리로드로 반영한다.
- C1: 클라이언트에 `measure_start`·`measure_stop`·`get_measurement` 도구가 보이고, `list_step_types`에 두 단계가 나온다. 계측 전후 `get_plan`의 계획 상태가 계측 도구 호출로 바뀌지 않는다.
- C2~C5, C7~C11: spec C11의 항목을 그 순서대로 한다(구간 경계, 정지 상태 값, 변하는 값, 표본 조회, 자동 종료, 표본 상한, 오류). 판정은 계획이 끝난 뒤의 응답으로만 한다.
- C6: 정지 상태 값 확인에서 모든 항목이 응답에 있는지 본다. 스로틀·추력·스테이지가 변하는 구간과 각속도·조종 입력의 부호는 조종 도구가 없어 게임에서 확인하지 못한다.
- C4의 단계 실패와 제출 거절(D011): 판정은 계획이 끝난 뒤의 `get_plan`·`get_measurement` 응답으로 한다.
  - 계획 `measure_start` → `measure_start`가 사유 `step_failed`로 끝나고, 그 뒤 `get_measurement`의 계측은 실행 중이다.
  - 실행 중인 계측이 없을 때 계획 `measure_stop` 하나가 사유 `step_failed`로 끝난다.
  - 단계 `measure_start`의 `sample_limit`을 0으로 준 `submit_plan`이 `bad_request`이고, 그 뒤 `get_plan`에 실행 중인 계획이 없다.
- C9의 장면 전환(D011): 계측을 실행한 채 `debug_space_center`를 한 뒤 `get_measurement`가 끝난 계측과 사유 `scene_change`를 보인다.
- C9의 활성 기체 변경(D012): `debug_launch`(`Kerbal X`, 발사대) → `debug_space_center` → `debug_launch`(`Aeris 3A`, 활주로)로 로드된 소유 기체 둘을 만든다(같은 절차가 docs/specs/디버그-보강/tasks.md T021에 있다). `debug_list_vessels`로 두 기체가 로드됐고 소유임을 확인한 뒤, 계측을 실행한 채 활성이 아닌 기체로 `debug_switch_vessel`을 하면 `get_measurement`가 끝난 계측과 사유 `vessel_change`를 보인다.
- C9의 Mod 다시 읽기: 계측을 실행한 채 `reload`한 뒤 `get_measurement`가 계측 `null`을 보이는지 확인한다.
- C10의 수집 중 예외(사유 `error`)는 게임에서 일으킬 수단이 없어 단위 테스트(`RecorderTests`의 `Stop("error", …)`와 조회 응답의 `error`)로만 확인한다.

## 원칙 점검

- 적용 규칙:
  - `workflow/spec-driven.md`와 하위 문서(문서·기준점·검토), `workflow/repository-content.md`(저장소 기준 상대 경로, 개인 경로 없음)
  - `git/github-access.md`·`git/github-issue.md`(이슈 #46), `git/commit-message.md`(커밋 메시지 형식)
  - `dotnet/project-creation.md`(새 프로젝트: `src/`·`tests/` 배치, `{Solution}.{역할}` 이름, MSTest, 솔루션에 포함)
  - `dotnet/nuget-package-selection.md`(새 패키지 없음)
  - `shell/powershell-windows.md`(명령 실행), `workflow/conversation-language.md`(대화 언어)
  - 전역 규칙 MCP 도구 검증(게임 검증은 실제 `mcp__kspbridge__*` 호출로만 판정)
  - 저장소 규칙 파일은 없다.
- 충돌: `dotnet/project-creation.md` 1번(`net10.0`)을 Mod 계측 모듈과 그 테스트 프로젝트는 따르지 않고 `net472`를 쓴다. 게임(KSP 1.12.5, Unity Mono)이 읽는 어셈블리여야 하고 기존 Mod 프로젝트와 그 테스트가 모두 `net472`이기 때문이다. 서버 계측 모듈은 `Directory.Build.props`의 `net10.0`을 따른다. 그 밖의 충돌은 없다.
