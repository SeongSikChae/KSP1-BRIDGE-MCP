# debug-tools

파일: src/KspBridge.Mod.Modules.Debug/KspBridge.Mod.Modules.Debug.csproj, src/KspBridge.Mod.Modules.Debug/DebugModule.cs, src/KspBridge.Mod.Modules.Debug/OrbitRequest.cs, src/KspBridge.Mod.Modules.Debug/PendingOrbit.cs, src/KspBridge.Mod.Modules.Debug/SaveName.cs, src/KspBridge.Mod.Modules.Debug/SaveFileCheck.cs, src/KspBridge.Mod.Modules.Debug/LoadedGameCheck.cs, src/KspBridge.Mod.Modules.Debug/VesselSwitchCheck.cs, src/KspBridge.Mod.Modules.Debug/QuickSaveCheck.cs, src/KspBridge.Server.Modules.Debug/KspBridge.Server.Modules.Debug.csproj, src/KspBridge.Server.Modules.Debug/DebugTools.cs, tests/KspBridge.Mod.Modules.Debug.Tests/KspBridge.Mod.Modules.Debug.Tests.csproj, tests/KspBridge.Mod.Modules.Debug.Tests/OrbitRequestTests.cs, tests/KspBridge.Mod.Modules.Debug.Tests/PendingOrbitTests.cs, tests/KspBridge.Mod.Modules.Debug.Tests/SaveNameTests.cs, tests/KspBridge.Mod.Modules.Debug.Tests/SaveFileCheckTests.cs, tests/KspBridge.Mod.Modules.Debug.Tests/LoadedGameCheckTests.cs, tests/KspBridge.Mod.Modules.Debug.Tests/VesselSwitchCheckTests.cs, tests/KspBridge.Mod.Modules.Debug.Tests/QuickSaveCheckTests.cs, ReadMe.md

게임 화면을 누르지 않고 MCP 도구만으로 시험할 수 있게 하는 검증용 도구(발사, 되돌리기, 우주센터 이동, 궤도 설정, 기체 목록·전환, 이름 있는 저장·불러오기). 장면을 바꾸거나 활성 기체를 바꾸는 도구는 실행 중인 계획을 코어의 장면 추적으로 끝낸다. 괄호 안 결정은 `<변경 폴더> D<번호>`로 적으며, `docs/specs/<변경 폴더>/decisions.md`의 결정이다.

## 요구(완료 조건)

- R1. `debug_launch`는 저장된 craft나 stock craft(`Ships/VAB`·`Ships/SPH`)를 기본 승무원과 함께 발사장(VAB는 `LaunchPad`, SPH는 `Runway` 기본)에 올린다. 우주센터·비행·에디터·추적소 장면에서 받고, 없는 craft나 시설 이름은 `bad_request`이다. 요청을 받으면 비행 장면을 불러오며, 호출하는 쪽이 `get_status`로 준비를 확인한다.
- R2. `debug_revert`는 비행을 발사 시점으로 되돌린다. 비행 장면이 아니면 `not_ready`, 되돌릴 수 없는 비행이면 `revert_unavailable`이다. `debug_space_center`는 비행을 저장하고 우주센터로 나간다.
- R3. `debug_set_orbit`은 활성 기체를 지정 궤도로 옮긴다(게임의 궤도 설정 치트와 같은 함수, 관측 D004). 천체 내부 이름(대소문자 무시, 생략하면 지금 천체), Ap 고도·Pe 고도(해발, m), 경사각(°, 기본 0)을 받고 지금 Pe에서 시작한다. 게임이 궤도를 적용하고 상황이 `ORBITING`으로 바뀐 것(Ap·Pe 1 km 안)을 확인한 뒤 응답하며, 10초 안에 확인하지 못하면 `not_ready`와 이유를 돌려준다(관측 D008).
- R4. `debug_set_orbit`의 오류: 비행 장면이 아니거나 장면을 불러오는 중이거나 활성 기체가 없으면 `not_ready`. 없는 천체, Ap 고도 < Pe 고도, Pe 고도가 대기 상한 이하(대기가 없으면 게임의 지형 최고 높이 이하, 그 높이를 게임이 모르면 거절), Ap 고도가 영향권 밖(천체 중심 기준), 장반경이 영향권의 0.99배를 넘음(게임이 자르는 범위)이면 `bad_request`(영향권이 무한한 천체는 위 두 상한 검사를 하지 않는다). 계획이 실행 중이면 계획을 먼저 끝내라는 이유와 함께 `bad_request`이며 계획과 기체는 바뀌지 않는다. 게임에 예외가 남거나 도구가 멈추지 않는다(관측 D013·D015).
- R5. `debug_list_vessels`는 비행 장면의 모든 기체를 돌려준다. 기체마다 id(`persistentId`), 이름, 종류, 상황, 천체 이름, 로드 여부(`Vessel.loaded`), 소유 여부, 활성 기체 여부, 활성 기체와의 거리(m)가 있다. 기체를 바꾸지 않는 조회다. 비행 장면이 아니거나(지금 장면 이름을 이유에 넣음) 장면을 불러오는 중이거나 비행 장면이 준비되지 않았으면 `not_ready`이다(디버그-보강 D002·D006).
- R6. `debug_switch_vessel`은 기체 id를 받아 로드된 기체를 활성 기체로 만든다. 장면을 다시 불러오지 않는다. 응답에 새 활성 기체의 id와 이름이 있고, 응답 뒤 `get_status`가 그 기체를 활성 기체로 보이며 1초 안에 `ready=true`가 된다. 이미 활성 기체인 id면 아무것도 바꾸지 않고 그 사실을 알리는 성공 응답이다. 실행 중인 계획은 `vessel_change`로 끝난다(디버그-보강 D001).
- R7. `debug_switch_vessel`의 오류: 비행 장면이 아니거나 장면을 불러오는 중이거나 비행 장면이 준비되지 않았으면 `not_ready`. 없는 id, 로드되지 않은 기체, 소유하지 않은 기체(`force`와 상관없음)이면 `bad_request`. `force`가 false(기본)이고 저장 가능 검사(`FlightGlobals.ClearToSave` 결과가 `CLEAR`)를 통과하지 못하면 게임이 막는 이유를 담아 `bad_request`이고, 게임이 그 밖의 이유로 거절해도 `bad_request`이다. `force`가 true면 저장 가능 검사만 건너뛴다. 어느 오류든 활성 기체와 실행 중인 계획은 바뀌지 않고 1초 안에 `ready=true`이다. 게임에 예외가 남거나 도구가 멈추지 않는다(디버그-보강 D004·D006·D014).
- R8. `debug_save`는 비행 장면에서 지금 게임을 저장 이름(생략하면 `quicksave`)으로 지금 세이브 폴더에 저장하고 같은 이름이 있으면 덮어쓴다. 지정한 파일(과 게임의 메타 파일)만 쓰고 `persistent`는 쓰지 않으며, 게임 난이도 설정의 퀵세이브 허용과 상관없다. 실행 중인 계획과 기체는 바뀌지 않는다. 오류: 비행 장면이 아니거나 장면을 불러오는 중이거나 준비되지 않았으면 `not_ready`. 활성 기체가 파괴됐거나 퀵세이브 검사(저장 가능 검사 결과가 사다리·지면 위 이동·곧 추락, 게임 설정 `CAN_ALWAYS_QUICKSAVE`와 상관없음)를 통과하지 못하면 `bad_request`. 이름이 비었거나 영문자·숫자·`-`·`_`·공백 밖의 글자가 있거나 64자를 넘거나 `persistent`(대소문자 무시)이면 `bad_request`. 이런 오류면 파일이 만들어지거나 바뀌지 않는다. 게임에 예외가 남거나 도구가 멈추지 않는다. 실제 게임에서 궤도 기체를 저장하고 궤도를 바꾼 뒤 불러오면 `get_flight` Ap·Pe가 저장 때 값(각 1 km 안)으로 돌아오고, 저장 뒤 `persistent.sfs` 수정 시각이 바뀌지 않는다(디버그-보강 D003·D005·D007·D008·D013·D014).
- R9. `debug_load`는 저장 이름(생략하면 `quicksave`)의 비행 중 저장한 파일을 불러와 저장 때의 활성 기체(파일의 `pid`로 찾음)로 비행 장면을 연다. 우주센터·비행·에디터·추적소 장면에서 받는다. 도구 자체는 `persistent`를 쓰지 않으며(비행 장면이 시작될 때 게임이 하는 자동 저장은 막지 않음), 게임 난이도 설정의 퀵로드 허용과 상관없다. 요청을 받으면 장면을 불러오며 호출하는 쪽이 `get_status`로 준비를 확인한다. 실행 중인 계획은 코어의 장면 추적으로 끝난다(비행 장면에서 불러오면 `new_flight`). 서로 다른 궤도로 저장한 두 파일을 차례로 불러오면 각각 Ap·Pe가 그 저장 때 값(각 1 km 안)이다(디버그-보강 D003·D005·D007·D011·D018).
- R10. `debug_load`의 오류: 게임이 없거나 장면을 불러오는 중이거나 받는 장면이 아니면 `not_ready`. 다음은 장면을 바꾸기 전에 판정해 `bad_request`이다. 이름이 R8의 글자 규칙에 맞지 않음, 파일이 없음, 읽을 수 없음(판정에 쓰는 `GAME`·`version`·`scene`·`activeVessel`·활성 기체 `pid`가 없거나 형식이 틀린 경우 포함), 비행 중 저장한 파일이 아님(시작 장면이 비행이 아님, 비행 상태·기체가 없음, 활성 기체 번호가 범위 밖), 저장 파일 `GAME/version`(주.부.수정)이 지금 게임 버전과 다름, 게임이 그 파일로 만든 결과에 비행 상태가 없거나 호환되지 않거나 저장 때 활성 기체가 없음. 파일 값 판정에서 걸리면 장면·계획·`persistent`가 바뀌지 않는다. 게임이 파일을 읽은 뒤에 걸리면 장면·계획은 바뀌지 않고 지금 비행의 기체 id 기록이 지워졌다는 사실과 복구 방법을 이유에 넣는다. 게임에 예외가 남거나 도구가 멈추지 않는다(디버그-보강 D009·D015·D016·D017·D018·D019).

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-18 | 골격 | R1·R2에 해당하는 검증용 발사·되돌리기·우주센터 이동(기준 spec 생기기 전) | docs/specs/골격/ |
| 2026-11-21 | 관측 | R1·R2 추가(기준 spec 처음 만듦, 지금 동작을 옮김), R3·R4 추가 | docs/specs/관측/ |
| 2026-11-21 | 디버그-보강 | R5~R10 추가(기체 목록·전환, 이름 있는 저장·불러오기), R3·R4 결정 참조 표기를 폴더 이름 붙인 형식으로 바꿈 | docs/specs/디버그-보강/ |

표에 없는 이전 변경: 기록 없음
