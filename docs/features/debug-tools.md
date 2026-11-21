# debug-tools

파일: src/KspBridge.Mod.Modules.Debug/KspBridge.Mod.Modules.Debug.csproj, src/KspBridge.Mod.Modules.Debug/DebugModule.cs, src/KspBridge.Mod.Modules.Debug/OrbitRequest.cs, src/KspBridge.Mod.Modules.Debug/PendingOrbit.cs, src/KspBridge.Server.Modules.Debug/KspBridge.Server.Modules.Debug.csproj, src/KspBridge.Server.Modules.Debug/DebugTools.cs, tests/KspBridge.Mod.Modules.Debug.Tests/KspBridge.Mod.Modules.Debug.Tests.csproj, tests/KspBridge.Mod.Modules.Debug.Tests/OrbitRequestTests.cs, tests/KspBridge.Mod.Modules.Debug.Tests/PendingOrbitTests.cs

게임 화면을 누르지 않고 MCP 도구만으로 시험할 수 있게 하는 검증용 도구(발사, 되돌리기, 우주센터 이동, 궤도 설정). 장면을 바꾸는 도구는 실행 중인 계획을 코어의 장면 추적으로 끝낸다. 괄호 안 D번호는 docs/specs/관측/decisions.md의 결정이다.

## 요구(완료 조건)

- R1. `debug_launch`는 저장된 craft나 stock craft(`Ships/VAB`·`Ships/SPH`)를 기본 승무원과 함께 발사장(VAB는 `LaunchPad`, SPH는 `Runway` 기본)에 올린다. 우주센터·비행·에디터·추적소 장면에서 받고, 없는 craft나 시설 이름은 `bad_request`이다. 요청을 받으면 비행 장면을 불러오며, 호출하는 쪽이 `get_status`로 준비를 확인한다.
- R2. `debug_revert`는 비행을 발사 시점으로 되돌린다. 비행 장면이 아니면 `not_ready`, 되돌릴 수 없는 비행이면 `revert_unavailable`이다. `debug_space_center`는 비행을 저장하고 우주센터로 나간다.
- R3. `debug_set_orbit`은 활성 기체를 지정 궤도로 옮긴다(게임의 궤도 설정 치트와 같은 함수, D004). 천체 내부 이름(대소문자 무시, 생략하면 지금 천체), Ap 고도·Pe 고도(해발, m), 경사각(°, 기본 0)을 받고 지금 Pe에서 시작한다. 게임이 궤도를 적용하고 상황이 `ORBITING`으로 바뀐 것(Ap·Pe 1 km 안)을 확인한 뒤 응답하며, 10초 안에 확인하지 못하면 `not_ready`와 이유를 돌려준다(D008).
- R4. `debug_set_orbit`의 오류: 비행 장면이 아니거나 장면을 불러오는 중이거나 활성 기체가 없으면 `not_ready`. 없는 천체, Ap 고도 < Pe 고도, Pe 고도가 대기 상한 이하(대기가 없으면 게임의 지형 최고 높이 이하, 그 높이를 게임이 모르면 거절), Ap 고도가 영향권 밖(천체 중심 기준), 장반경이 영향권의 0.99배를 넘음(게임이 자르는 범위)이면 `bad_request`(영향권이 무한한 천체는 위 두 상한 검사를 하지 않는다). 계획이 실행 중이면 계획을 먼저 끝내라는 이유와 함께 `bad_request`이며 계획과 기체는 바뀌지 않는다. 게임에 예외가 남거나 도구가 멈추지 않는다(D013·D015).

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-18 | 골격 | R1·R2에 해당하는 검증용 발사·되돌리기·우주센터 이동(기준 spec 생기기 전) | docs/specs/골격/ |
| 2026-11-21 | 관측 | R1·R2 추가(기준 spec 처음 만듦, 지금 동작을 옮김), R3·R4 추가 | docs/specs/관측/ |

표에 없는 이전 변경: 기록 없음
