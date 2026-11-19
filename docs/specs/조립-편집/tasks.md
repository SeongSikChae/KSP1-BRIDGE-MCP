# 조립 편집: 부품 배치·부착·대칭·스테이지 구성 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 기반과 위험 확인

- [ ] T001 [P] 순수 설정 트리 `CfgNode`(이름, 순서 있는 값 목록, 하위 노드, 값 읽기·쓰기·지우기) (src/KspBridge.Mod.Modules.Editor/CfgNode.cs) — C11, C12
- [ ] T002 [P] 순수 `Vec3`·`Quat` 연산과 KSP 벡터·쿼터니언 문자열 읽기·쓰기 (src/KspBridge.Mod.Modules.Editor/Geo.cs) — C2, C3, C7
- [ ] T003 `ShipEditor` 뼈대: `ConfigNode`↔`CfgNode` 변환, 원본 저장, 임시 파일(`GameData/KspBridge/PluginData/editor/edit.craft`)로 다시 불러오기, 썸네일 자리 표시 파일 수정 시각 맞추기, 부품 수 검사, 원본으로 되돌리기(원본이 빈 기체면 빈 에디터 다시 열기), 들린 부품 감지, 프리팹 복제로 새 부품 PART 노드 만들기 (src/KspBridge.Mod.Modules.Editor/ShipEditor.cs) — C1, C11, C12
- [ ] T004 `UnityEngine.PhysicsModule` 참조 추가 (src/KspBridge.Mod.Modules.Editor/KspBridge.Mod.Modules.Editor.csproj) — C3
- [ ] T005 KSP 위험 확인(임시 진단 처리기, 확인 뒤 지움): Kerbal X를 불러와 `istg`를 정규화해 바꾼 노드로 다시 불러온 뒤, 직후와 2프레임 이상 뒤 두 번 조회해 스테이지·`craftID`가 같은지 확인. 프리팹 복제로 만든 PART 노드가 정상 부품으로 불러와지는지 확인. 편집 뒤 Player.log에 "Autogen thumbnail"이 없는지 확인. 결과를 작업 문서에 적는다 (src/KspBridge.Mod.Modules.Editor/EditorModule.cs) — C10, C11

체크포인트: `dotnet build` 경고 0. T005의 세 확인이 통과한다. 통과하지 못하면 3절 3번대로 plan.md부터 고친다.

## 단계 2: 순수 편집 모델

- [ ] T006 `CraftDoc`: PART 목록·식별자·부모 관계 읽기, 연결 값(`link`·`attN`·`srfN`·`sym`)과 `mir`·`symMethod` 다루기, 루트 놓기, 노드 부착(대칭 묶음의 `symMethod`·`mir` 이어받기), 표면 부착(대칭 포함), 하위 트리 제거, 교체(부모 쪽·자식 쪽 검사, 루트 교체), 하위 트리 강체 이동·회전(방사·거울 짝), 쓰기 전 무결성 검사(모든 참조, 루트 하나). 연산마다 먼저 검사하고 통과한 뒤에만 고치며, 끝에 스테이지 정규화를 부른다 (src/KspBridge.Mod.Modules.Editor/CraftDoc.cs) — C1, C2, C3, C4, C5, C6, C7, C10, C12
- [ ] T007 [P] `AttachMath`: 노드 부착 자세(정반대 방향 180° 처리), 새 부품 쪽 노드 자동 선택, 게임 공식 표면 부착 자세(`LookRotation` 구현 포함), 방사 대칭 각도, 실제 자세로 구한 짝 변환(방사 회전·거울 반사)과 이동량·회전 옮기기 (src/KspBridge.Mod.Modules.Editor/AttachMath.cs) — C2, C3, C4, C7
- [ ] T008 [P] `Staging` 순수 규칙: 스테이지 묶기, 새 부품 기본 번호, 부품 옮기기, 두 스테이지 바꾸기, 정규화(`istg` 빈 번호 없음, `sidx` 연속, 스테이지 대상이 아닌 부품의 `istg`) (src/KspBridge.Mod.Modules.Editor/Staging.cs) — C10, C11
- [ ] T009 [P] `CraftDoc` 단위 테스트: 부착·제거·교체(루트 교체, 부모 쪽·자식 쪽 거부)·이동(방사·거울)의 노드 결과, 거부 시 노드 그대로, 깨진 `link`·`sym`·`attN`·`srfN` 참조 거부, 모르는 값·하위 노드 보존, 원본 부품 0개일 때 되돌리기 방식 판정 (tests/KspBridge.Mod.Modules.Editor.Tests/CraftDocTests.cs) — C1~C7, C12
- [ ] T010 [P] `AttachMath` 단위 테스트: 위·아래 노드 자세, 정반대 방향 노드(NaN 없음), 노드 자동 선택, 표면 노드 방향 ±X·±Z·±Y별 게임 공식 결과(바깥을 향함), 대칭 각도, 방사·거울 짝 이동량 (tests/KspBridge.Mod.Modules.Editor.Tests/AttachMathTests.cs) — C2, C3, C4, C7
- [ ] T011 [P] `Staging` 단위 테스트: 옮기기·바꾸기, 제거 뒤 빈 스테이지 정규화 (tests/KspBridge.Mod.Modules.Editor.Tests/StagingTests.cs) — C10, C11

체크포인트: `dotnet test` 통과.

## 단계 3: KSP 쪽 편집과 처리기

- [ ] T012 `ShipEditor` 편집 실행: 부품 정보 수집(프리팹 노드·표면 노드·부착 규칙), 대상 충돌체 광선 계산, `CraftDoc` 연산 호출, 다시 불러온 뒤 부모·대칭·스테이지 검사, 재루트(`EditorReRootUtil.MakeRoot` 뒤 다시 불러오기), 스테이지 편집 적용 (src/KspBridge.Mod.Modules.Editor/ShipEditor.cs) — C1~C7, C10, C11, C12
- [ ] T013 [P] `PartSettings`: 자원·에디터 조작 필드 조회, 값 검사·적용·대칭 짝 적용, `onEditorShipModified` 발생 (src/KspBridge.Mod.Modules.Editor/PartSettings.cs) — C8, C9, C12
- [ ] T014 [P] 스테이지 조회(살아 있는 부품의 `hasStagingIcon`·`stagingOn`·`inverseStage`) (src/KspBridge.Mod.Modules.Editor/Staging.cs) — C10
- [ ] T015 편집 결과 형식(`changed`·`removed`·`dropped_held_part`·`ship`) 도우미 (src/KspBridge.Mod.Modules.Editor/ShipView.cs) — C11
- [ ] T016 새 처리기 12개 등록 (src/KspBridge.Mod.Modules.Editor/EditorModule.cs) — C1~C10, C12
- [ ] T017 [P] 새 MCP 도구 12개 (src/KspBridge.Server.Modules.Editor/EditorTools.cs) — C1~C10, C14
- [ ] T018 [P] 도구 목록 갱신, 임시 craft·썸네일 자리 표시 파일 안내 (ReadMe.md) — C14

체크포인트: `dotnet build KspBridge.slnx` 경고 0, `dotnet test` 통과, `scripts/deploy.ps1`로 KSP를 켠 채 배포해 새 도구가 호출된다.

## 단계 4: KSP 검증

- [ ] T019 plan 검증 전략대로 C1~C12를 MCP 응답으로 확인 (작업 대상 파일 없음) — C1~C12
- [ ] T020 2단 로켓 조립·저장·불러오기·발사 확인 후 시험 craft 삭제 (작업 대상 파일 없음) — C13- [ ] T021 `git diff --stat <기준점>`으로 코어·`debug`·배포 스크립트가 바뀌지 않았는지와 핫리로드 확인 (작업 대상 파일 없음) — C14
- [ ] T022 SPH 거울 대칭 craft로 C7 거울 이동·회전 확인 (작업 대상 파일 없음) — C7

체크포인트: 모든 완료 조건의 확인 결과를 명령·응답과 함께 보고할 수 있다.

## 의존 관계

- 단계 1의 T005는 T003·T004 뒤. T005 결과에 따라 단계 2·3 설계가 바뀔 수 있어 단계 2보다 먼저 한다.
- T006은 T001·T002 뒤, T007은 T002 뒤. 테스트 T009~T011은 각 대상(T006~T008) 뒤.
- T012는 T003·T006·T007·T008 뒤. T016은 T012~T015 뒤. T017·T018은 인터페이스가 plan에 정해져 있어 단계 3 안에서 동시에 할 수 있다.
- 단계 4는 단계 3 배포 뒤.
