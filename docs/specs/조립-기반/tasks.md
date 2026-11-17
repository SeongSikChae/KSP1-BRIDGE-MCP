# 조립 기반: 에디터 진입, 부품 목록 조회, craft 저장·불러오기 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 프로젝트 골격

- [x] T001 Mod 모듈 프로젝트 생성, Debug 모듈 csproj와 같은 틀 (src/KspBridge.Mod.Modules.Editor/KspBridge.Mod.Modules.Editor.csproj) — C9
- [x] T002 [P] 서버 모듈 프로젝트 생성, Debug 서버 모듈 csproj와 같은 틀 (src/KspBridge.Server.Modules.Editor/KspBridge.Server.Modules.Editor.csproj) — C9
- [x] T003 [P] 테스트 프로젝트 생성, Mod.Core.Tests와 같은 틀에 Editor 모듈 참조 (tests/KspBridge.Mod.Modules.Editor.Tests/KspBridge.Mod.Modules.Editor.Tests.csproj) — C9
- [x] T004 세 프로젝트를 솔루션에 추가 (KspBridge.slnx) — C9

체크포인트: `dotnet build KspBridge.slnx`가 경고 0으로 성공한다.

## 단계 2: KSP 비의존 로직과 단위 테스트

- [x] T005 craft 이름 검사, 부품 필터(분류·검색)와 페이지 나누기(`limit` 기본 50·최대 200, `offset`, `total`) (src/KspBridge.Mod.Modules.Editor/EditorQuery.cs) — C3, C5
- [x] T006 이름 규칙(빈 이름, 경로 구분자, `..`, 금지 문자)과 필터·페이지(대소문자 무시, 겹치지 않는 페이지, 범위 밖 offset) 테스트 (tests/KspBridge.Mod.Modules.Editor.Tests/EditorQueryTests.cs) — C3, C5

체크포인트: `dotnet test`가 통과한다.

## 단계 3: Mod 모듈

- [x] T007 `EditorModule`: 처리기 8개 등록, 장면·에디터 준비 판정(코어 `BlockReason` → `EditorLogic.fetch`·`ship`·요청 시설 일치, 입력 잠금 제외), `enter`(우주센터 → `StartEditor`, 에디터 안 다른 시설 → `StartEditor(다른 시설)`, 같은 시설이면 `already`), `leave`, `new` (src/KspBridge.Mod.Modules.Editor/EditorModule.cs) — C1, C2, C7
- [x] T008 [P] `PartCatalog`: `LoadedPartsList`에서 숨김·`TechHidden`·미연구(R&D 있을 때만) 제외, 부품 항목(이름, 표시 이름, 분류, 크기, 건조 질량, 비용, 자원, 엔진 추력·Isp·추진제, 모듈 이름) 구성, `EditorQuery`로 필터·페이지 (src/KspBridge.Mod.Modules.Editor/PartCatalog.cs) — C3
- [x] T009 [P] `ShipView`: 기체 이름·시설·부품 수·질량, 부품별 `craftID`·부모·부착 방식·노드·`inverseStage`·대칭 (src/KspBridge.Mod.Modules.Editor/ShipView.cs) — C4
- [x] T010 [P] `CraftFiles`: 저장 경로 계산, 저장(빈 기체 거부, 덮어쓰기 검사, `shipName`·이름 칸 맞춤), 불러오기(저장 게임 → 기본 제공 순서, 읽기·부품 설치 여부 사전 검사, `LoadShipFromFile`, 부품 수 사후 확인), 시설별 목록(이름·출처·부품 수) (src/KspBridge.Mod.Modules.Editor/CraftFiles.cs) — C5, C6, C8

체크포인트: 솔루션 빌드가 경고 0이다. T008~T010은 T007이 등록한 처리기에서 호출된다.

## 단계 4: 서버 모듈과 문서

- [x] T011 MCP 도구 8개(`editor_enter`, `editor_leave`, `editor_list_parts`, `editor_get_ship`, `editor_save`, `editor_load`, `editor_new`, `editor_list_crafts`). 장면을 바꾸는 도구는 Destructive로 표시하고 "먼저 `get_status`", "저장하지 않은 변경은 버려짐, 먼저 `editor_save`"를 설명에 넣음 (src/KspBridge.Server.Modules.Editor/EditorTools.cs) — C1~C8
- [x] T012 [P] 도구 목록에 `editor_*` 추가 (ReadMe.md) — C9

체크포인트: 빌드 경고 0, `dotnet test` 통과.

## 단계 5: KSP 검증

- [x] T013 배포(`scripts/deploy.ps1`) 후 KSP·클라이언트 재시작 없이 `get_status.versions.modules`에 Editor 모듈, `editor_*` 도구 호출 가능 확인, 코어·`debug`·배포 스크립트가 바뀌지 않았음을 `git diff --stat`으로 확인 — C9
- [x] T014 plan 검증 전략대로 C1~C8을 MCP 응답으로 확인하고 결과를 기록. `SwitchEditor`·`LoadShipFromFile` 타이밍 위험을 이때 확인. 시험용 `bridge-test-*` craft는 끝나고 삭제 — C1~C8

체크포인트: 모든 완료 조건의 확인 결과(명령과 응답 요지)가 있다.

### 확인 결과 (2026-11-18, 저장 Test·샌드박스, MCP 응답만으로 확인)

- C1: 우주센터에서 `editor_enter(VAB)` → `accepted`, 약 3초 뒤 `get_status` `scene=EDITOR, ready=true`. `editor_enter(SPH)` → 다시 준비된 뒤 `editor_get_ship.facility=SPH`. `editor_leave` → `scene=SPACECENTER, ready=true`. 재진입도 같음.
- C2: 비행 장면과 우주센터에서 `editor_get_ship`·`editor_save`·`editor_load`·`editor_new`·`editor_leave` → `not_ready`, 메시지에 현재 장면(`now FLIGHT`/`now SPACECENTER`). 진입 직후 로딩 중 `editor_get_ship` → `not_ready`. Player.log에 브리지 예외 0건. 게임이 없을 때(메인 메뉴)의 메시지에 장면을 넣은 수정(검토 지적 2)은 저장을 닫아야 해서 KSP에서는 확인하지 않음.
- C3: `editor_list_parts` → `total=461`, 항목 필드 모두 있음. `category=Engine` → 42개 모두 `engine` 값 있음. `search=lv-t45`, `search=liquidEngine2` → 해당 부품(게임이 한국어라 표시 이름 검색은 한국어). `limit/offset` 두 페이지 겹침 0, `limit=0` → `bad_request`. 경력 모드 필터는 경력 저장이 없어 확인하지 못함.
- C4: 빈 에디터 `part_count=0`. `Kerbal X` 불러온 뒤 73부품, 부모·스택 노드·표면 부착·스테이지·대칭 짝이 채워짐.
- C5: `editor_save(bridge-test-1)` → 파일 생성, `path` 일치. 같은 이름 → `bad_request`, `overwrite=true` → `overwritten=true`. 이름 `a/b` → `bad_request`. 빈 기체 → `bad_request`.
- C6: `editor_load(Kerbal X)` → `source=stock, part_count=73`. `editor_load(bridge-test-1)` → `source=save, part_count=73`. 없는 이름 → `bad_request`. 설치되지 않은 부품 이름을 넣은 시험 craft → `bad_request`(`uses parts that are not installed: bridgeTestNoSuchPart`), `PART` 없는 시험 craft → `bad_request`(`has no parts`), 두 경우 모두 그 뒤 `editor_get_ship.part_count=73` 그대로.
- C7: 처음에는 `editor_new`·재진입에서 직전 발사 기체가 되살아남(`StartupBehaviour`가 `LOAD_FROM_CACHE`로 남음). `START_CLEAN`을 먼저 넣도록 고친 뒤 `editor_new` → 준비된 뒤 `part_count=0`.
- C8: `editor_list_crafts(VAB)` → stock 25개(`Kerbal X` 포함)와 save의 `bridge-test-1`, 부품 수가 파일과 같음. SPH 24개.
- C9: `git diff --stat 6ea9543`에 코어·`debug`·배포 스크립트·서버 호스트 파일 없음. KSP·서버를 켠 채 배포 → `get_status.versions.modules`에 Editor 모듈, 새 MCP 세션에서 `editor_*` 8개가 보이고 호출됨. 이미 열려 있던 세션은 GET 알림 스트림이 끊겨 목록 갱신을 받지 못함(서버 호스트 문제, 이 작업 범위 밖).
- 빌드 경고 0, `dotnet test` 69개 통과. 시험용 `bridge-test-*` craft는 모두 삭제.

## 의존 관계

- 단계 1 → 단계 2·3·4: 프로젝트가 있어야 코드를 빌드한다.
- T005 → T006, T008, T010: 필터·이름 검사를 쓴다.
- T007 → T008~T010의 연결: 처리기 등록과 준비 판정을 T007이 맡는다. T008~T010은 서로 다른 파일이라 동시에 할 수 있다.
- T011은 Mod 쪽 op 이름·인자(plan 인터페이스 표)에만 의존하므로 단계 3과 병행할 수 있지만, 검증은 단계 3 뒤에 한다.
- 단계 5는 단계 1~4가 모두 끝난 뒤. 장면 전환 전 `get_status` 확인.
