# 조립 편집: 부품 배치·부착·대칭·스테이지 구성 작업

형식: `- [x] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 기반과 위험 확인

- [x] T001 [P] 순수 설정 트리 `CfgNode`(이름, 순서 있는 값 목록, 하위 노드, 값 읽기·쓰기·지우기) (src/KspBridge.Mod.Modules.Editor/CfgNode.cs) — C11, C12
- [x] T002 [P] 순수 `Vec3`·`Quat` 연산과 KSP 벡터·쿼터니언 문자열 읽기·쓰기 (src/KspBridge.Mod.Modules.Editor/Geo.cs) — C2, C3, C7
- [x] T003 [P] `UnityEngine.PhysicsModule` 참조 추가 (src/KspBridge.Mod.Modules.Editor/KspBridge.Mod.Modules.Editor.csproj) — C3
- [x] T004 [P] `Available()` 필터를 부품 하나를 검사하는 공용 함수로 꺼냄(목록 결과는 그대로) (src/KspBridge.Mod.Modules.Editor/PartCatalog.cs) — C12
- [x] T005 `ShipEditor` 뼈대: `ConfigNode`↔`CfgNode` 변환, 원본 저장, 임시 파일(`GameData/KspBridge/PluginData/editor/edit.craft`)로 다시 불러오기, 썸네일 자리 표시 파일과 수정 시각 맞추기, 진행 중 편집(번호·기대 결과·원본 노드) 보관과 마지막 최종 결과 보관, 안정 판정(모든 부품 `Part.editorStarted`, `StageManager.sortRoutine` 리플렉션 읽기 null, 불러온 뒤 3프레임 이상, 필드를 못 찾으면 거부), 원본으로 되돌리기(원본이 빈 기체면 빈 에디터 다시 열기), 들린 부품 감지, 프리팹 복제로 새 부품 PART 노드 만들기 (src/KspBridge.Mod.Modules.Editor/ShipEditor.cs) — C1, C11, C12
- [x] T006 KSP 위험 확인(임시 진단 처리기 `editor/diag_*`. 이 처리기는 T024에서 지움). 결과를 작업 문서에 적는다 (src/KspBridge.Mod.Modules.Editor/EditorModule.cs) — C10, C11, C12
  - Kerbal X를 불러와 진단 처리기 안에서 스테이지 대상 부품의 `istg`·`sidx`를 직접 정규화한 노드로 다시 불러온 뒤, 안정 판정이 몇 프레임 만에 참이 되는지와 안정 직후·1초 뒤 `editor_get_ship`이 같은지(스테이지·`craftID` 포함)
  - 스테이지를 끈 부품(`HeatShield1`의 분리 스테이지 끔)이 든 craft로 같은 확인
  - 새 부품 PART 노드가 정상 부품으로 불러와지는지(프리팹 복제 방식이 실패하면 프리팹에서 직접 쓴 노드와 MODULE 없는 최소 노드를 비교)
  - 다시 불러오기 전후 `Ships/@thumbs/editor/edit.png`의 크기·수정 시각이 자리 표시 파일 그대로인지
  - 확인 결과(2026-11-20, 자세한 기록은 작업 문서): Kerbal X 73부품 정규화 다시 불러오기는 4프레임 만에 안정, 안정 직후와 1.2초 뒤 `editor_get_ship`·스테이지 구성 같음, `craftID` 유지. 게임은 같은 스테이지 안 대칭 짝에 같은 `sidx`를 매김(plan 정규화 규칙에 반영). `HeatShield1` 스테이지 끔(`stage` −1)·켬(`stage` 0) 모두 4프레임 안정, 1초 뒤 같음. `edit.png`는 70바이트 자리 표시 그대로. 프리팹 복제 + `SaveShip`은 `NullReferenceException`으로 실패, 프리팹에서 직접 쓴 노드는 정상(`mk1pod.v2` 질량 0.746 t), MODULE 없는 최소 노드는 질량 0.75 t로 다름 → plan 결정을 프리팹 직접 쓰기로 바꾸고 T026을 더함(의도 변경)
- [x] T026 새 부품 PART 노드를 프리팹에서 직접 쓰도록 바꿈(값 줄은 `SaveShip` 형식, MODULE·RESOURCE는 프리팹 모듈·자원의 `Save`, 프리팹 복제 코드와 T006 실험 코드 정리). 진단 처리기 `diag_new_part`도 새 함수를 부르도록 바꾸고(복제·최소 노드 갈래 삭제), 응답에 노드 쪽과 함께 프리팹 쪽 모듈 이름 목록·자원 이름과 기본량을 돌려줌(T023 비교용) (src/KspBridge.Mod.Modules.Editor/ShipEditor.cs, src/KspBridge.Mod.Modules.Editor/EditorModule.cs) — C1, C2, C3, C12

체크포인트: `dotnet build` 경고 0. T006의 확인이 모두 통과한다. 통과하지 못하면 spec-driven 3절 3번대로 plan.md부터 고친다.

## 단계 2: 순수 편집 모델

- [x] T007 [P] `AttachMath`: 노드 부착 자세(정반대 방향 180° 처리), 새 부품 쪽 노드 자동 선택, 게임 공식 표면 부착 자세(`LookRotation` 구현 포함), 방사 대칭 각도, 실제 자세로 구한 짝 변환(방사 회전·거울 반사, 반사면을 정할 수 없으면 실패)과 자세·이동량·회전 옮기기 (src/KspBridge.Mod.Modules.Editor/AttachMath.cs) — C2, C3, C4, C7
- [x] T008 [P] `Staging` 순수 규칙: 스테이지 묶기(큰 번호부터), 지정 번호 범위(기존 번호 또는 가장 큰 번호 + 1, 없으면 0), 새 부품 기본 번호, 부품 옮기기, 서로 다른 두 기존 스테이지 바꾸기, 스테이지 대상 부품 정규화(`istg` 빈 번호 없음, `sidx` 0부터 이어지되 대칭 짝은 같은 값) (src/KspBridge.Mod.Modules.Editor/Staging.cs) — C10, C11
- [x] T009 `CraftDoc`: PART 목록·식별자·부모 관계 읽기, 연결 값(`link`·`attN`·`srfN`·`sym`)과 `mir`·`symMethod` 다루기, 루트 놓기, 노드 부착(대칭 묶음의 `symMethod`·`mir` 이어받기), 표면 부착(방사 대칭, 대칭 묶음 대상에 짝 변환으로 복제, 묶음 대상에 `symmetry` 2 이상 거부), 하위 트리 제거, 교체(부모 쪽·자식 쪽 검사, 루트 교체, 새 식별자, 스테이지 이어받기 규칙), 하위 트리 강체 이동·회전(방사·거울 짝, 루트 거부), 스테이지 번호 지정·범위 검사, `changed`·`removed` 계산(편집 전후 비교, 재번호만 바뀐 부품 제외), 쓰기 전 무결성 검사(모든 참조, 루트 하나, 스테이지 대상 부품의 번호 정규화), 되돌리기 방식 판정(원본 부품 0개면 빈 에디터 다시 열기, 아니면 원본 다시 불러오기)을 순수 함수로. 연산마다 먼저 검사하고 통과한 뒤에만 고치며, 끝에 T008의 스테이지 대상 부품 정규화를 부른다. 짝 변환은 T007, 스테이지 규칙은 T008을 쓴다 (src/KspBridge.Mod.Modules.Editor/CraftDoc.cs) — C1, C2, C3, C4, C5, C6, C7, C10, C11, C12
- [x] T010 `AttachMath` 단위 테스트: 위·아래 노드 자세, 정반대 방향 노드(NaN 없음), 노드 자동 선택, 표면 노드 방향 ±X·±Z·±Y별 게임 공식 결과(바깥을 향함), 대칭 각도, 방사·거울 짝 자세·이동량 (tests/KspBridge.Mod.Modules.Editor.Tests/AttachMathTests.cs) — C2, C3, C4, C7
- [x] T011 `Staging` 단위 테스트: 지정 범위(빈 기체 0, 범위 밖 거부), 옮기기·바꾸기(같은 번호·없는 번호 거부), 제거 뒤 빈 스테이지 정규화, 같은 스테이지 안 대칭 짝은 같은 `sidx`·나머지는 0부터 이어짐 (tests/KspBridge.Mod.Modules.Editor.Tests/StagingTests.cs) — C10, C11
- [x] T012 `CraftDoc` 단위 테스트: 부착·대칭 묶음 표면 부착·제거·교체(루트 교체, 부모 쪽·자식 쪽 거부, 새 식별자·`removed`, 스테이지 대상 여부 세 경우)·이동(방사·거울, 루트 거부)의 노드 결과, `changed` 범위(재루트·재번호 포함), 거부 시 노드 그대로, 깨진 `link`·`sym`·`attN`·`srfN` 참조·루트 둘·정규화되지 않은 스테이지 번호(대칭 짝이 아닌 부품의 `sidx` 중복 포함) 거부와 대칭 짝의 같은 `sidx` 허용, 모르는 값·하위 노드 보존, 되돌리기 방식 판정(원본 부품 0개) (tests/KspBridge.Mod.Modules.Editor.Tests/CraftDocTests.cs) — C1, C2, C3, C4, C5, C6, C7, C11, C12

체크포인트: `dotnet test` 통과.

## 단계 3: KSP 쪽 편집과 처리기·도구

- [x] T013 `ShipEditor` 편집 실행: 부품 이름 검사(T004 공용 함수), 부품 정보 수집(프리팹 노드·표면 노드·부착 규칙·스테이지 대상 여부), 대상 충돌체 광선 계산, `CraftDoc` 연산 호출, 진행 중 편집 등록, `edit_result`(미안정 `pending`, 안정 뒤 부품 수·부모·부착 방식과 노드·대칭·요청한 스테이지 구성 검사, 맞으면 T016 도우미로 기체·`changed`·`removed`·`symmetry_cleared`·`dropped_held_part` 반환, 어긋나면 T009 판정대로 되돌리기, 빈 기체면 그 응답에서 바로 안내가 담긴 `bad_request`), 재루트(`MakeRoot` 전 원본 저장, 실패 시 원본 다시 불러오기), 스테이지 편집 적용, 진행 중 편집이 있을 때 다른 요청 앞 검사(미안정 `not_ready`, 안정 뒤 마무리, 되돌리기로 이어지면 `not_ready`)를 처리기가 부를 함수로 (src/KspBridge.Mod.Modules.Editor/ShipEditor.cs) — C1, C2, C3, C4, C5, C6, C7, C10, C11, C12
- [x] T014 `PartSettings`: 자원·에디터 조작 필드 조회, 변경 앞에 T013의 진행 중 편집 검사 호출, 값 검사·적용·대칭 짝 적용, `onEditorShipModified` 발생, 진행 중 편집으로 등록해 안정 뒤 T016 도우미로 결과 반환 (src/KspBridge.Mod.Modules.Editor/PartSettings.cs) — C8, C9, C11, C12
- [x] T015 스테이지 조회(게임 판정식 `(hasStagingIcon || stackIcon.StageIcon != null) && stagingOn`, `inverseStage`) (src/KspBridge.Mod.Modules.Editor/Staging.cs) — C10
- [x] T016 편집 결과 형식(`changed`·`removed`·`symmetry_cleared`·`dropped_held_part`·`ship`) 도우미 (src/KspBridge.Mod.Modules.Editor/ShipView.cs) — C11
- [x] T017 새 처리기 12개와 `edit_result` 등록, 새 편집 op 처리기 전부(`place_root`·`set_root`·`attach_node`·`attach_surface`·`remove`·`replace`·`move`·`set_stage`·`swap_stages`. `set_part_settings`는 T014 안에서 부름)와 기존 `load`·`new`·`save`·`enter`·`leave` 처리기 앞에 진행 중 편집 검사(T013 함수) 추가, 우리 요청이 아닌 장면 전환 때 진행 중 편집을 버리고 그 번호의 `edit_result`에 버린 사실을 담은 `not_ready` (src/KspBridge.Mod.Modules.Editor/EditorModule.cs) — C1, C2, C3, C4, C5, C6, C7, C8, C9, C10, C11, C12
- [x] T018 [P] 새 MCP 도구 12개. 기체를 바꾸는 도구는 Mod op 호출 뒤 `edit_result`를 50 ms 간격·최대 10초 되묻고 최종 결과만 돌려줌(시간 초과면 `not_ready`) (src/KspBridge.Server.Modules.Editor/EditorTools.cs) — C1, C2, C3, C4, C5, C6, C7, C8, C9, C10, C11, C14
- [x] T019 [P] 도구 목록 갱신, 임시 craft·썸네일 자리 표시 파일 안내 (ReadMe.md) — C14
- [x] T020 고장 주입 진단 처리기(임시, T024에서 지움): 안정을 늦춘 상태, 안정 뒤 결과 불일치를 강제한 상태(원본에 부품이 있을 때·빈 기체일 때), `MakeRoot` 뒤 실패를 강제한 상태를 만들고, 각 상태가 T013·T017의 경로로 들어가는지 확인 (src/KspBridge.Mod.Modules.Editor/EditorModule.cs) — C11, C12
- [x] T021 [P] 기준 spec 새로 만들기(마지막 구현 작업): #39 조립-기반 spec(C1~C9)과 이번 spec(C1~C14)을 읽어 지금 유효한 요구를 R번호로 쓰고, 이력에 조립-기반·조립-편집 두 줄, `파일:` 줄에 editor 모듈 쌍과 테스트 파일 (docs/features/editor.md) — C1, C2, C3, C4, C5, C6, C7, C8, C9, C10, C11, C12, C13, C14

- [x] T027 새 부품의 스테이지 대상 여부를 미리 판정하지 않음: 새 부품은 요청한 스테이지(기본은 가장 큰 번호 + 1, 교체는 옛 부품이 대상이면 옛 스테이지)에 넣어 쓰고, 결과 검사는 게임이 실제로 대상으로 만든 부품만으로 기대 구성을 다시 번호 매겨 비교하며, `stage`를 지정한 새 부품이 비대상이면 불일치로 되돌림. 프리팹 판정(`IsStagedPrefab`) 삭제. 단위 테스트(기대 구성 필터·재번호 비교) 추가. KSP 확인: 엔진 아래 TD-12 분리기는 새 스테이지 1, `HeatShield1`·`dockingPortLateral`은 비대상으로 성공·`stage` 지정 시 거부, 분리기를 탱크로 교체 성공(단계 4 진행 중 발견해 더한 작업) (src/KspBridge.Mod.Modules.Editor/Staging.cs, src/KspBridge.Mod.Modules.Editor/CraftDoc.cs, src/KspBridge.Mod.Modules.Editor/ShipEditor.cs, tests/KspBridge.Mod.Modules.Editor.Tests/StagingTests.cs, tests/KspBridge.Mod.Modules.Editor.Tests/CraftDocTests.cs) — C6, C10, C11, C12

체크포인트: `dotnet build KspBridge.slnx` 경고 0, `dotnet test` 통과, `scripts/deploy.ps1`로 KSP를 켠 채 배포해 새 도구와 T020 진단 처리기가 호출된다. 기준 spec이 있다.

## 단계 4: KSP 검증

- [x] T022 plan 검증 전략대로 C1~C12를 MCP 응답으로 확인. 진행 중 편집·되돌리기·재루트 실패 경로는 T020의 진단 처리기로 확인. C7 거울 이동·회전은 SPH 거울 대칭 craft로 확인 (작업 대상 파일 없음) — C1, C2, C3, C4, C5, C6, C7, C8, C9, C10, C11, C12
- [x] T023 2단 로켓 조립·저장·불러오기·발사 확인(`get_status` `FLIGHT`·`ready=true`·활성 기체 이름) 후 우주센터로 돌아와 시험 craft 삭제. 새 부품 PART 노드 확인(plan 오류·위험, T024 전에): C13에 쓰는 부품 종류(포드·낙하산·분리기·탱크·엔진·핀)마다 빈 에디터에서 `diag_new_part`로 만든 응답의 노드 쪽 MODULE·RESOURCE 개수·이름·자원량이 같은 응답의 프리팹 쪽 값과 같으며, 그 노드가 불러와짐(`diag_new_part` 응답이 `ok`, 안정 뒤 `editor_get_ship`이 `part_count` 1이고 부품 이름이 같음) (작업 대상 파일 없음) — C1, C2, C3, C13
- [x] T024 T006·T020의 임시 진단 처리기 삭제 뒤 빌드·테스트를 다시 통과하고, KSP를 켠 채 배포해 클라이언트 재시작 없이 새 도구가 호출되는지 확인 (src/KspBridge.Mod.Modules.Editor/EditorModule.cs) — C14
- [x] T025 `git -C <대상> diff --stat 590edcd`로 코어·`debug`·배포 스크립트가 바뀌지 않았는지 확인 (작업 대상 파일 없음) — C14

체크포인트: 모든 완료 조건의 확인 결과를 명령·응답과 함께 보고할 수 있고, 구현 후 검토로 넘어갈 수 있다.

## 의존 관계

- 단계 1의 T005는 T001 뒤(`CfgNode` 변환을 씀). T006은 T001~T005 뒤. T026은 T006 결과(프리팹 복제 실패, 직접 쓰기 확인) 뒤에 더한 작업이며 T006 뒤, 단계 2·3의 T013보다 먼저 한다. T006 결과(안정 판정, 스테이지 불변, PART 노드, 썸네일)에 따라 단계 2·3 설계가 바뀔 수 있어 단계 2보다 먼저 한다.
- T007은 T002 뒤, T008은 단계 1 뒤. T009는 T001·T002·T007·T008 뒤(짝 변환과 스테이지 규칙을 씀). 테스트 T010~T012는 각 대상(T007~T009) 뒤.
- T015·T016은 T005 뒤. T013은 T004·T005·T009·T015·T016 뒤(기존 부품의 스테이지 대상 판정과 결과 형식 도우미를 씀). T014는 T013·T016 뒤(진행 중 편집 검사와 결과 형식 도우미를 씀). T017은 T013~T016 뒤. T018·T019는 인터페이스가 plan에 정해져 있어 단계 3 안에서 동시에 할 수 있다. T020은 T013·T017 뒤(되돌리기·`edit_result`·재루트 경로가 있어야 고장 주입을 확인할 수 있음).
- T027은 단계 4(T022) 진행 중 발견한 판정 차이로 더한 작업이며 T015·T013 뒤, T022를 다시 이어 가기 전에 한다.
- T021(기준 spec)은 spec-driven 3절 2번의 마지막 구현 작업이라 단계 3 안, 단계 4 검증 전에 한다. 다른 작업과 파일이 겹치지 않아 단계 3 안에서 동시에 할 수 있다.
- 단계 4는 단계 3 배포 뒤. T022·T023은 진단 처리기를 쓰므로 T024(진단 처리기 삭제)보다 먼저 한다. T024는 삭제 뒤 다시 빌드·배포해 C14를 확인하고, T025는 마지막에 한다.
