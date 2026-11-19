# 조립 편집: 부품 배치·부착·대칭·스테이지 구성 계획

## 요약

#39의 `editor` 모듈 쌍에 편집 도구를 더한다. 게임의 부착 처리(`EditorLogic.attachPart` 등)는 private이고 마우스 상태 기계(fsm)에 묶여 있어 직접 부를 수 없다. 그래서 구조를 바꾸는 편집은 **craft 노드 재구성** 방식으로 한다.

1. 지금 기체를 `ShipConstruct.SaveShip()`으로 craft 노드로 만든다(되돌리기용 원본도 이것).
2. 그 노드를 KSP에 의존하지 않는 순수 모델(`CraftDoc`)로 읽어 편집한다. 새 부품의 PART 노드, 부착 위치·회전 계산에 필요한 값(부품 노드 좌표, 표면 충돌 지점)은 게임에서 미리 구해 넘긴다.
3. 편집한 노드의 무결성을 파일로 쓰기 전에 검사한다. 모든 `link`·`sym`·`attN`·`srfN` 참조가 실제 PART를 가리키는지, 루트가 하나인지, 스테이지 번호가 정규화되어 있는지 본다. 틀리면 불러오지 않고 `bad_request`를 돌려준다. 게임 로더는 깨진 `link`에 대화상자를 띄우고(`ShipConstruct.LoadShip`의 "Loading Failed"), 깨진 `sym`에는 null 짝을 만들기 때문에, 불러온 뒤의 검사로는 막을 수 없다.
4. 편집한 노드를 임시 craft 파일로 쓰고 `EditorLogic.LoadShipFromFile`로 다시 불러온다. 이 함수는 장면을 다시 로드하지 않고, 같은 호출 안에서 에디터를 재시작해 기체를 만든다(#39에서 동기 완료를 확인했고, 디컴파일에서 `StartEditor(isRestart: true)` 경로를 확인함). 이 경로(`LOAD_FROM_FILE`)는 `ShipConstruction.LoadShip(path)`로 파일의 `craftID`를 그대로 쓰므로 식별자가 유지된다(`EditorLogic.cs:14176-14183`, `ShipConstruct.cs:1904`. 식별자를 다시 매기는 `SpawnConstruct`는 Merge 경로에서만 불림).
   다시 불러온 직후에는 기체가 아직 확정되지 않았다. 부품마다 `Part.Start` 코루틴이 다음 프레임 이후에 모듈 시작·스테이지 아이콘·`UpdateStageability`(craft에 저장된 스테이지 켜기·끄기 반영)를 하고(`Part.cs:2517, 2768-2804, 20605`), `StageManager`는 2프레임 뒤 코루틴에서 빈 스테이지를 지우고 `SetSeparationIndices`로 모든 부품의 `inverseStage`를 다시 정한다(`StageManager.cs:501-503, 2965, 3099, 3112`). Mod 처리기는 동기 함수(`ModuleApi.cs:89` `RequestHandler`)라 프레임을 넘겨 기다릴 수 없으므로, 같은 호출 안에서 결과를 검사하거나 응답을 만들지 않는다. 4단계 처리기는 편집을 적용하고, 기대 결과와 원본 노드를 "진행 중 편집"으로 Mod에 남긴 뒤 편집 번호를 돌려준다.
5. **안정 대기**: MCP 서버 도구가 Mod `editor/edit_result`를 짧은 간격으로 되묻는다. Mod는 에디터가 안정될 때까지 `pending`을 돌려준다. 안정은 기체의 모든 부품이 `Part.editorStarted`(에디터에서 `Part.Start` 끝에 참이 됨, `Part.cs:3527`. `Part.started`는 비행 장면에서만 참이 되므로 쓰지 않음, `Part.cs:3394`)이고, `StageManager`의 private `sortRoutine`(`StageManager.cs:175`)이 null(리플렉션으로 읽기만 함)이며, 불러온 뒤 3프레임 이상 지난 상태다. 이 판정이 게임의 불러오기 뒤 작업이 모두 끝난 시점을 가리키는지는 tasks 단계 1의 위험 확인에서 확인한다.
6. **결과 검사**: 안정된 뒤 첫 `edit_result` 호출이 결과를 검사한다(부품 수, 기대한 부모·부착·대칭, 게임이 실제로 스테이지 대상으로 만든 부품 기준의 요청한 스테이지 구성). 맞으면 그때의 기체(`editor_get_ship` 형식)와 `changed`·`removed` 등을 돌려주고 진행 중 편집을 끝낸다. 응답은 게임의 후처리가 끝난 뒤의 값이므로 그 뒤 조회와 같다(C11). 어긋나면 원본으로 되돌리고 `bad_request`를 돌려준다. 원본에 부품이 있으면 원본 노드를 같은 방식으로 다시 불러오고, 되돌린 뒤에도 같은 안정 대기를 거쳐 `bad_request`로 끝낸다. 원본이 빈 기체면 파일로 불러오지 않는다. 부품 0개 craft를 불러오면 `StartEditor`가 `ship.parts[0]`에서 예외를 내기 때문이다(`EditorLogic.cs:1567`). 이때는 #39 `editor_new`와 같은 방식으로 빈 에디터를 다시 열고(장면 다시 로드), 안정 대기 없이 그 `edit_result` 응답에서 바로 `bad_request`로 편집을 끝낸다. 오류 문구에는 무엇이 어긋났는지와 "에디터를 빈 상태로 다시 열었으니 `get_status`로 준비를 확인하라"를 쓴다.

예외 두 가지는 게임의 공개 API로 직접 한다.

- **루트 바꾸기**: 공개 static `EditorReRootUtil.MakeRoot(newRoot, oldRoot)`가 게임 에디터의 재루트를 그대로 한다(표면 부착 방향 뒤집기, 맞지 않게 된 대칭 묶음 정리까지 포함). `MakeRoot`는 살아 있는 기체를 그 자리에서 바꾸므로, **부르기 전에** 1단계의 원본 노드를 저장한다. `MakeRoot` 뒤 그 기체를 `SaveShip()`해 2~6단계로 다시 불러와 새 루트가 craft의 첫 PART가 되게 한다. 게임(`EditorLogic.cs:7369-7406` `OnNewRootSelect`, `MakeRoot` 7371행)은 그 뒤 private `rootPart`에 새 루트를 넣지만(7397행), 우리는 다시 불러오기에서 `rootPart = ship.parts[0].localRoot`로 다시 정해지므로(`EditorLogic.cs:1567`) 따로 맞추지 않는다. `SaveShip()`은 `rootPart`가 아니라 부품 목록 순서로 저장한다(`ShipConstruct.cs:251-259`). 새 루트를 첫 PART로 두는 일은 `CraftDoc`이 한다. 루트가 될 수 없는 부품이면 `MakeRoot` 전에 거부한다. `MakeRoot` 뒤 어느 단계에서 실패하든(예외 포함) 저장해 둔 원본 노드를 다시 불러와 되돌린다.
- **부품 설정(C8·C9)**: 구조가 바뀌지 않으므로 다시 불러오지 않는다. 살아 있는 부품의 `PartResource.amount`와 모듈 필드(`BaseField`, 에디터 조작 `uiControlEditor`)를 바로 바꾼다. 설정이 스테이지 켜기·끄기를 바꾸면 게임이 다음 프레임에 재정렬할 수 있으므로, 응답은 구조 편집과 같이 5·6단계(안정 대기 뒤 기체 반환)를 거친다.

## 바꿀 파일·구조

- `src/KspBridge.Mod.Modules.Editor/EditorModule.cs`: 새 처리기 등록(아래 인터페이스 표와 서버 전용 `edit_result`). 기존 `load`·`new`·`save`·`enter`·`leave` 처리기 앞에 진행 중 편집 검사(결정 "진행 중 편집은 하나만 둔다")를 넣는다. 우리 요청이 아닌 장면 전환 때 진행 중 편집을 버린다. 편집 처리기는 `RequireEditor()` 뒤에 `ShipEditor`를 부른다.
- `src/KspBridge.Mod.Modules.Editor/CfgNode.cs`(새 파일): KSP `ConfigNode`와 같은 모양의 순수 트리(이름, 값 목록, 하위 노드). `ConfigNode` ↔ `CfgNode` 변환은 `ShipEditor`에 둔다. 단위 테스트가 KSP·Unity 없이 돌게 하기 위함이다.
- `src/KspBridge.Mod.Modules.Editor/Geo.cs`(새 파일): 순수 `Vec3`·`Quat`(곱, 역, 벡터 회전, 축-각, from-to)과 KSP 문자열 형식(`x,y,z`, `x|y|z`, `x,y,z,w`) 읽기·쓰기.
- `src/KspBridge.Mod.Modules.Editor/CraftDoc.cs`(새 파일): craft 노드의 순수 편집 모델. PART 목록, `part = 이름_cid`, `link`, `attN`, `srfN`, `sym`, `pos`·`rot`·`attPos`·`attPos0`·`attRot`·`attRot0`, `mir`·`symMethod`, `istg`·`dstg`·`sidx`, `attm`을 다루고, 모르는 값·하위 노드(MODULE, RESOURCE 등)는 손대지 않고 그대로 둔다. 편집 연산: 루트 놓기, 노드 부착, 표면 부착(대칭 포함), 하위 트리 제거, 교체(루트 포함), 하위 트리 강체 이동·회전, 스테이지 번호 바꾸기, 스테이지 정규화, 쓰기 전 무결성 검사. 각 연산은 먼저 전부 검사하고, 통과한 뒤에만 고친다.
- `src/KspBridge.Mod.Modules.Editor/AttachMath.cs`(새 파일): 순수 기하 계산. 노드 부착 자세(대상 노드 위치·방향 → 새 부품 위치·회전, 정반대 방향 포함), 표면 부착 자세(게임 공식), 방사 대칭 각도, 두 짝의 실제 자세로 구한 대칭 변환(방사는 회전, 거울은 대칭면 반사)과 그 변환으로 옮긴 이동량.
- `src/KspBridge.Mod.Modules.Editor/ShipEditor.cs`(새 파일): KSP 쪽 편집 실행. 원본 저장, 부품 정보 수집(`AvailablePart.partPrefab`의 `attachNodes`, `srfAttachNode`, `attachRules`), 표면 충돌 지점 구하기(대상 부품의 `Collider.Raycast`), 새 부품 PART 노드 만들기(프리팹에서 직접), 임시 파일로 다시 불러오기, 진행 중 편집(편집 번호, 기대 결과, 원본 노드) 보관, 안정 판정, 안정 뒤 결과 검사·되돌리기, 재루트.
- `src/KspBridge.Mod.Modules.Editor/PartSettings.cs`(새 파일): 부품 설정 조회·변경(C8, C9).
- `src/KspBridge.Mod.Modules.Editor/Staging.cs`(새 파일): 스테이지 조회(C10)와 스테이지 연산의 순수 규칙(번호 매기기, 빈 스테이지 없애기, 두 스테이지 바꾸기). 순수 규칙은 KSP 타입을 쓰지 않는다.
- `src/KspBridge.Mod.Modules.Editor/PartCatalog.cs`: `Available()`의 필터를 부품 하나를 검사하는 공용 함수로 꺼낸다(목록 결과는 그대로).
- `src/KspBridge.Mod.Modules.Editor/ShipView.cs`: 편집 결과용으로 `Describe`에 바뀐 부품 식별자 목록을 붙이는 도우미를 더한다(기존 형식은 그대로).
- `src/KspBridge.Mod.Modules.Editor/KspBridge.Mod.Modules.Editor.csproj`: 표면 충돌 계산용 `UnityEngine.PhysicsModule` 참조 추가.
- `src/KspBridge.Server.Modules.Editor/EditorTools.cs`: 새 MCP 도구(아래 표). 조회 도구는 기존과 같은 얇은 층이다. 기체를 바꾸는 도구는 Mod `editor/<op>`를 부른 뒤, 돌려받은 편집 번호로 `editor/edit_result`를 50 ms 간격, 최대 10초 되물어 최종 결과를 돌려준다(요약 5·6단계).
- `tests/KspBridge.Mod.Modules.Editor.Tests/CraftDocTests.cs`, `AttachMathTests.cs`, `StagingTests.cs`(새 파일): 순수 로직 단위 테스트.
- `ReadMe.md`: 도구 목록에 새 `editor_*` 도구 추가. 편집이 쓰는 임시 파일(`GameData/KspBridge/PluginData/editor/edit.craft`)과 썸네일 자리 표시 파일(`Ships/@thumbs/editor/edit.png`)을 적는다.
- `docs/features/editor.md`(새 파일): editor 기능의 기준 spec. 기준 spec이 없던 기존 기능이므로 마지막 구현 단계에서 #39 조립-기반 spec(C1~C9)과 이번 spec(C1~C14)을 읽어 지금 유효한 요구를 R번호로 처음 쓰고, 이력에 조립-기반·조립-편집 두 줄, `파일:` 줄에 editor 모듈 쌍과 테스트 파일을 적는다.

## 결정

- **구조 편집은 craft 노드 재구성 + 다시 불러오기**: 게임의 craft 불러오기 경로가 부착 노드, 대칭, 스테이지, 자원, 모듈 상태를 모두 일관되게 만든다. 요청 하나가 노드 하나의 편집이므로 전부 적용 또는 전혀 적용 안 함(C12)이 자연스럽고, 편집 규칙을 KSP 없이 단위 테스트할 수 있다.
  검토한 대안: 살아 있는 부품에 직접 부착(`setParent`, `AttachNode.attachedPart`, `ship.Add`, 이벤트 발생). 게임의 private 부착 처리를 흉내 내야 해서 빠뜨린 상태가 생기기 쉽다. `EditorLogic.SpawnPart`: 부품을 마우스에 들린 상태로 만들어 fsm 입력이 필요하다.
- **새 부품의 PART 노드는 로드된 프리팹에서 직접 쓴다**(T006 결과로 바꿈): 값 줄은 `ShipConstruct.SaveShip`과 같은 형식(`part`·`partName`·`pos`·`rot`·`attPos`·`attRot`·`mir`·`symMethod`·`istg`·`sidx`·`attm`·노드마다 `attN` 등, `ShipConstruct.cs:258-415`)으로 쓰고, MODULE·RESOURCE는 프리팹의 모듈·자원이 스스로 쓰게 한다(`prefab.Modules[i].Save`, `prefab.Resources[i].Save`). 프리팹을 복제하거나 바꾸지 않는다. 이 노드에 계산한 `pos`·`rot`, 연결(`attN`·`srfN`·`link`·`sym`), 새 `craftID`(기체 안에서 쓰지 않는 값)를 넣는다. T006에서 `mk1pod.v2`·`HeatShield1`이 정상 부품으로 불러와지고 질량이 프리팹 기본(0.746 t)과 같음을 확인했다.
  검토한 대안: 프리팹을 `Object.Instantiate`로 복제해 임시 `ShipConstruct`의 `SaveShip()`으로 얻기(처음 결정. 활성화되지 않은 복제본에서 `SaveShip`이 `NullReferenceException`, T006). MODULE·RESOURCE 없는 최소 노드(불러와지지만 모듈 기본 상태가 빠져 질량이 0.75 t로 달라짐, T006).
- **부품 이름은 #39 `editor_list_parts`와 같은 기준으로 받는다**: 루트 놓기·부착·교체의 `part` 이름은 `PartCatalog`의 `Available()` 필터(숨은 부품·`TechHidden`·미연구 부품 제외, `PartCatalog.cs:36-47`)를 통과해야 하며, 아니면 `bad_request`. 게임 로더는 미연구 부품도 오류 없이 불러오므로(`shipPartsUnlocked = false`만 둠) 불러온 뒤 검사로는 막을 수 없다. 필터는 `PartCatalog`에서 이름 하나를 검사하는 공용 함수로 꺼내 함께 쓴다.
- **쓰기 전에 무결성을 검사한다**(검토 1회차 지적 3): 게임 로더는 깨진 `link`에 대화상자를 띄우고(MCP로 닫을 수 없음), 깨진 `sym`에는 null 짝을 넣는다. 그래서 불러오기 전에 `CraftDoc`이 모든 참조를 검사해 원인을 막는다. 불러온 뒤의 검사(아래)는 게임 쪽 차이를 잡는 두 번째 단계다.
- **다시 불러오기 결과를 검사하고, 어긋나면 원본으로 되돌린다**: 게임 로더는 실패를 알리지 않는다(#39에서 확인). 에디터가 안정된 뒤(요약 5단계) 부품 수와 기대한 부모·대칭·스테이지를 비교해, 다르면 원본으로 되돌리고 `bad_request`(무엇이 어긋났는지)를 돌려준다. 원본이 빈 기체면 파일 대신 빈 에디터 다시 열기로 되돌린다(검토 1회차 지적 2, 요약 5단계).
- **임시 craft 파일은 `GameData/KspBridge/PluginData/editor/edit.craft`**: 저장 게임의 `Ships`에 두면 craft 목록에 나온다. `PluginData`는 KSP가 읽지 않는다.
- **썸네일 렌더링을 건너뛴다**(검토 1회차 지적 7): `ShipConstruction.LoadShip(path)`는 1프레임 뒤 썸네일을 자동으로 만든다(`ShipConstruction.cs:380, 2134`). 경로가 저장 게임 밖이면 `<KSP>/Ships/@thumbs/<상위 폴더 이름>/<파일 이름>.png`, 즉 `Ships/@thumbs/editor/edit.png`에 쓴다. 다만 썸네일이 craft보다 새것이면 만들지 않는다(`ThumbnailIsOutdated`). 그래서 임시 craft를 쓸 때마다 그 자리에 작은 자리 표시 PNG를 두고, 수정 시각을 craft보다 뒤로 맞춘다. 이 파일 하나가 게임 폴더에 남는다는 사실을 ReadMe에 적는다.
  검토한 대안: 렌더링을 그대로 두고 부작용으로 기록만 하기. 편집마다 렌더링 비용이 든다. `LOAD_FROM_CACHE`(파일 없이 노드로 불러오기)는 그 경로를 여는 `ShipToLoadSelected(ConfigNode)`가 private이라 쓸 수 없다.
- **마우스에 들린 부품은 버려진다**: `SaveShip`에 들어가지 않기 때문이다. 편집 결과에 `dropped_held_part: true`로 알린다. 편집을 거부하면 화면 조작(클릭)을 요구하게 되므로 거부하지 않는다(메모리 규칙: 게임 창 조작을 요구하지 않음).
- **재루트는 `EditorReRootUtil.MakeRoot`**: 게임과 같은 규칙(루트가 될 수 있는 부품인지, 대칭 정리)을 쓴다. 대칭 묶음이 깨지면(게임 규칙) 결과에 `symmetry_cleared`로 알린다.
  검토한 대안: `CraftDoc`에서 부모·자식 방향 뒤집기. 표면 부착 방향 뒤집기(`ReverseSrfNodeDirection`)를 다시 구현해야 한다.
- **노드 부착 자세**: 새 부품 회전 = 대상 부품 회전 × (새 노드 방향을 대상 노드 방향의 반대로 돌리는 회전), 위치 = 대상 노드의 월드 위치 − 새 회전 × 새 노드 위치. 새 노드 방향과 목표 방향(대상 노드 방향의 반대)이 서로 정반대라 from-to 회전의 축이 정해지지 않으면(외적 0, 예: 대상 `top`에 새 부품 `top`을 지정), 방향에 수직인 축(부품 로컬 X, 그것도 평행하면 Z)으로 180° 돌린다. 이 처리가 없으면 NaN이 나온다(검토 1회차 참고 지적). 새 부품 쪽 노드를 지정하지 않으면, 대상 노드 방향의 반대를 가장 가깝게 가리키는 빈 노드를 고르고(동점이면 `top`/`bottom` 이름 짝 우선), 없으면 거부한다.
- **표면 부착 위치 지정 = 대상 부품 기준 `height`(축 방향 m, 부품 원점 기준)와 `angle`(축 둘레 도, 0°는 대상 부품의 로컬 +Z, 위에서 볼 때 시계 방향)**: 그 높이에서 축 바깥쪽 먼 점에서 축을 향해 대상 부품 충돌체(collider)로 광선을 쏴 표면 점과 법선을 얻는다. 광선이 대상에 맞지 않으면 거부한다.
- **표면 부착 자세는 게임 공식을 그대로 쓴다**(검토 1회차 지적 1): 회전 = `LookRotation(법선, 대상 회전 × up) × LookRotation(표면 노드 방향, up)`, 위치 = 표면 점 − 회전 × 표면 노드 위치(`EditorLogic.cs:10794-10795`, 새 부품의 `attRotation`은 단위 회전). 표준 부품의 표면 노드 방향은 ±X, ±Z, ±Y가 섞여 있다. "노드 방향 = −법선"처럼 단순화하면 ±Z 부품이 180° 돌아간 채 대상 안쪽에 붙는다. `LookRotation`은 순수 `Quat`로 같은 정의(앞 = z, 위 = y 직교화)로 구현하고, ±X·±Z·±Y 노드 경우를 단위 테스트한다.
  검토한 대안: 월드 좌표를 직접 받기. Planner가 부품 모양을 몰라 표면 위의 점을 고를 수 없다.
- **방사 대칭**: `symmetry` 개수 n이면 `angle + k·360/n`(k=0..n-1)마다 위 계산을 해서 n개를 붙이고, 서로를 `sym`으로 잇고 `symMethod = Radial`로 둔다. 대칭 묶음의 부품에 노드 부착하면 묶음의 각 부품에 같은 노드로 붙이고 새 부품끼리 대칭으로 잇는다(C4).
- **대칭 묶음 부품에 표면 부착**(C4): 지정한 대상 짝에서 위 계산으로 새 부품 자세를 구한 뒤, 아래 "거울 대칭도 다룬다"의 짝 변환(지정한 짝 → 각 짝)으로 그 자세를 옮겨 짝마다 붙인다. 짝마다 표면 점을 다시 찾지 않으므로 짝의 겉모양이 같다는 전제(대칭 묶음은 같은 부품)를 쓴다. `symmetry`가 2 이상이면 곱해진 묶음이 되므로 거부한다. 새 부품끼리는 대상 묶음의 `symMethod`를 이어받아 대칭으로 잇는다.
- **거울 대칭도 다룬다**(검토 1회차 지적 6): SPH 기체나 거울 대칭 craft를 불러오면 `symMethod = Mirror` 묶음이 들어온다. 대칭 짝 사이의 변환은 대칭 방식을 가정하지 않고 두 짝의 실제 자세에서 구한다. 방사는 묶음 부모 축 둘레 회전이고, 거울은 묶음 부모를 지나는 대칭면 반사다. 반사면은 두 짝의 위치 차이에 수직인 면이고, 짝의 `mir` 값과 맞는지 확인한다. 대칭 묶음에 노드 부착할 때 새 부품은 대상의 `symMethod`를 이어받고, 짝마다 대상 짝의 `mir`를 따른다. 거울 짝의 자세는 짝 대상 노드 기준으로 노드 부착 계산을 다시 해서 얻는다.
- **제거·교체·이동은 대칭 짝까지 함께**: 대상과 대칭 짝의 하위 트리 전체가 대상이다. 남은 부품의 `attN`에서 지운 부품을 가리키던 칸은 `Null_0`으로, `link`·`sym`은 지운다.
- **교체**: 새 부품은 옛 부품의 부모 쪽 노드 이름(표면 부착이면 표면)으로 같은 자리에 붙인다. 노드로 붙은 자식은 새 부품의 같은 이름 노드로 옮기고, 그 노드의 위치 변화만큼 자식 하위 트리를 강체로 옮긴다. 표면 부착 자식은 월드 자세를 그대로 두고 부모만 바꾼다(새 부품의 겉면과 떨어질 수 있음을 도구 설명에 적는다). 바꾸기 전에 아래를 모두 검사하고, 하나라도 맞지 않으면 거부한다(검토 1회차 지적 5).
  - 부모 쪽: 옛 부품이 노드로 붙어 있었으면 새 부품에 같은 이름 노드가 있다. 표면으로 붙어 있었으면 새 부품의 `attachRules.srfAttach`가 참이고 `srfAttachNode`가 있다.
  - 자식 쪽: 노드 자식마다 새 부품에 같은 이름 노드가 있다. 표면 자식이 있으면 새 부품의 `attachRules.allowSrfAttach`가 참이다.
  - **루트 교체**: 부모가 없으므로, 새 부품을 옛 루트의 자세(위치·회전)에 놓고 자식 쪽 검사만 한다. 새 부품이 루트가 될 수 있어야 한다(`attachRules.allowRoot`). 교체한 부품이 craft의 첫 PART가 된다.
  - **식별자**: 교체로 생긴 부품(짝 포함)은 기체 안에서 쓰지 않는 새 `craftID`를 받는다. 옛 부품과 짝의 식별자는 `removed`로 돌려준다. 다시 붙은 자식은 부모가 바뀌므로 `changed`에 들어간다.
  - **스테이지**: 옛 부품이 스테이지 대상이면 새 부품을 옛 `istg`에 넣어 쓰고, 아니면 가장 큰 번호 + 1에 넣어 쓴다. 새 부품의 대상 여부는 불러온 뒤 게임이 정하므로(아래 '새 부품의 스테이지 대상 여부는 게임이 정한다'), 결과는 둘 다 대상이면 옛 스테이지 이어받기, 새 부품만 대상이면 맨 앞 새 스테이지, 새 부품이 비대상이면 스테이지 구성에서 빠짐이 된다(옛 스테이지가 비면 게임이 지우고, 결과 검사는 그 번호 변화를 허용한다).
- **이동·회전**: `offset`(m)은 부모 부품의 로컬 좌표, `rotate`(도, x·y·z 오일러)는 부품 자신의 로컬 좌표로 받는다. 부품과 하위 트리를 강체로 옮기고, `attPos`·`attRot`도 같은 양만큼 맞춘다. 대칭 짝에는 위 "거울 대칭도 다룬다"의 짝 변환으로 이동량과 회전을 옮겨 넣는다. 방사는 회전한 이동량과 같은 로컬 회전이다. 거울은 반사한 이동량과, 반사면 기준으로 뒤집은 회전이다(요·롤 부호 반전). 루트 부품 이동은 기체 전체 이동이라 거부한다.
- **부품 설정 = 에디터에서 보이는 조작만**: 모듈 필드 중 `guiActiveEditor`이고 `uiControlEditor`가 있는 것(`UI_FloatRange`·`UI_Toggle`·`UI_ChooseOption`·`UI_Cycle` 등)만 목록에 넣는다. 값을 바꿀 때는 범위·선택지를 검사하고 `BaseField.SetValue`와 조작의 `onFieldChanged`를 불러 게임이 화면에서 바꾼 것과 같게 한 뒤 `GameEvents.onEditorShipModified`를 보낸다. 자원은 `isTweakable`인 것만 바꾼다. 대칭 짝에는 같은 모듈 순번·필드에 같은 값을 넣는다(`symmetry=false`면 그 부품만).
  검토한 대안: 모든 `KSPField` 허용. 에디터에서 바꿀 수 없게 한 값까지 바뀌어 게임과 다른 기체가 된다.
- **스테이지 번호는 게임의 `inverseStage`(큰 번호가 먼저 작동)**. 스테이지 대상 부품 = 게임 판정과 같이 `(hasStagingIcon || stackIcon.StageIcon != null) && stagingOn`인 부품(새 부품은 아래 '새 부품의 스테이지 대상 여부는 게임이 정한다'). 새 스테이지 부품의 기본 스테이지는 지금 가장 큰 번호 + 1(맨 먼저 작동, 스테이지가 없으면 0)이며, 부착 도구(`place_root` 포함)의 `stage` 인자로 정할 수 있다. `stage`와 `set_stage`의 값은 기존 스테이지 번호 또는 가장 큰 번호 + 1만 받고, 그 밖의 값이나 비대상인 기존 부품을 옮기는 요청은 `CraftDoc`을 고치기 전에 거부하고, 새 부품에 `stage`를 줬는데 게임이 비대상으로 만든 경우는 불러온 뒤 결과 검사에서 거부(되돌리기)한다. `swap_stages`는 서로 다른 두 기존 번호만 받는다. 한 요청에서 대칭으로 생긴 부품은 같은 `istg`를 받는다. 스테이지 편집도 `istg`·`sidx`를 고친 노드를 다시 불러오는 방식이다.
- **새 부품의 스테이지 대상 여부는 게임이 정한다**(구현 중 KSP 확인과 plan 검토 8회차로 바꿈): 부품이 스테이지 대상인지는 `Part.Start`에서 모듈 `OnStart`가 `part.stagingIcon`을 정한 뒤 `UpdateStageability`가 판정한다(`Part.cs:2768-2804, 20605`). 아이콘을 정하는 조건이 모듈마다 다르고(`overrideStagingIconIfBlank`, `useStaging` 등), 스테이지 가능 모듈이 없으면 아이콘이 있어도 비대상이 되는 등, 프리팹만 보고 미리 맞히면 부품에 따라 어긋난다(프리팹 `hasStagingIcon`으로 판정했을 때 TD-12 분리기가 비대상으로 잘못 판정됨). 그래서 미리 판정하지 않는다.
  - 새 부품(부착·루트 놓기·교체로 생긴 부품)은 스테이지 대상일 수 있는 것으로 보고 요청한 스테이지(지정이 없으면 가장 큰 번호 + 1, 교체는 아래 "교체"의 규칙)에 넣어 쓴다. 기존 부품의 대상 여부는 살아 있는 부품의 게임 판정을 쓴다.
  - 결과 검사(요약 6단계)는 불러온 뒤 게임이 실제로 스테이지 대상으로 만든 부품만으로 비교한다. 기대 스테이지 구성에서 게임이 비대상으로 만든 새 부품을 빼고 번호를 0부터 다시 매긴 뒤, 살아 있는 스테이지 번호와 같아야 한다. 새 부품이 맨 앞(가장 큰 번호)의 새 스테이지에 혼자 있다가 비대상이 되어도 다른 부품의 순서는 그대로다.
  - 요청이 `stage`를 지정했는데 그 새 부품을 게임이 비대상으로 만들었으면 불일치("스테이지 대상이 아닌 부품에 stage를 지정함")로 보고 되돌린다. 결과는 C10대로 거부이고 기체는 요청 전과 같다.
  검토한 대안: 프리팹 모듈로 게임 순서를 흉내 내기(모듈별 아이콘 조건을 계속 따라가야 해서 기본 부품·모드 부품에서 또 어긋날 수 있음, plan 검토 8회차). 불러온 뒤 판정이 다르면 스테이지를 고쳐 한 번 더 불러오기(편집마다 다시 불러오기가 두 번이 됨).
- **`changed` 계산**: 편집 전후의 부품 목록을 비교해 정한다. 새로 생긴 부품, 요청이 직접 옮기거나 설정을 바꾼 부품(대칭 짝 포함), 부모·부착 방식·노드 이름·대칭 묶음이 달라진 부품, 요청이 스테이지를 정하거나 옮긴 부품을 넣는다. 정규화로 `istg` 번호만 바뀐 부품은 넣지 않는다. 재루트도 같은 비교로 방향이 뒤집힌 경로의 부품과 대칭이 풀린 부품이 들어간다.
- **모든 구조 편집 뒤 스테이지를 정규화한다**(검토 1회차 지적 4): 에디터가 다시 시작되면 `StageManager.OnEditorStarted`가 2프레임 뒤 `SortIcons(deleteManualStages: true)`로 빈 스테이지를 지우고 번호를 다시 매길 수 있다(`StageManager.cs:501-503`, 2913 이후). 그러면 같은 요청 안에서 만든 C11 응답과 그 뒤 조회가 달라진다. 원인을 두 층으로 막는다. (1) 스테이지 옮기기뿐 아니라 부착·제거·교체·재루트 뒤에도 `CraftDoc`이 스테이지 대상 부품을 정규화한 상태로 쓴다(`istg`가 0부터 빈 번호 없이, 같은 스테이지 안 `sidx`가 0부터 이어지되 대칭 짝은 같은 `sidx`. 게임도 이렇게 다시 매긴다, T006). 이것은 요청이 정한 스테이지 구성을 craft에 담는 일이다. 스테이지 대상이 아닌 부품의 `istg`는 `CraftDoc`이 정하지 않는다. (2) 응답은 게임의 불러오기 뒤 작업(`Part.Start`의 `UpdateStageability`, `StageManager` 코루틴의 빈 스테이지 삭제와 `SetSeparationIndices`)이 모두 끝난 뒤 만든다(요약 5·6단계). 스테이지 대상이 아닌 부품의 `inverseStage`와 스테이지 켜기·끄기 반영은 게임이 정한 값을 그대로 읽으므로, 응답과 그 뒤 조회가 어긋날 원인이 없다. (1)은 게임의 빈 스테이지 삭제가 요청이 정한 구성을 흩뜨리지 않게 하는 몫이다.
  검토한 대안: `CraftDoc`에 `setSeparationIndices` 규칙을 다시 구현(게임과 어긋날 여지가 남음). `SortIcons(instant: true)`(코루틴이 이미 등록돼 있어 아무것도 하지 않음, plan 검토 2회차). 불러온 직후 `SetSeparationIndices()` 호출(부품 `Start` 전이라 `stagingOn` 등 판정 입력이 확정되지 않음, plan 검토 3회차). 같은 호출에서 부품마다 `UpdateStageability` 미리 호출(모듈 `OnStart` 전이라 판정이 틀릴 수 있음). 코어에 비동기 처리기 추가(C14와 충돌).
  tasks 단계 1의 위험 확인에서 안정 판정 뒤 응답과 1초 뒤 조회가 같은지, 스테이지를 끈 부품이 든 craft로 확인한다. 다르면 의도 변경으로 plan을 고친다.
- **진행 중 편집은 하나만 둔다**: 진행 중 편집이 있는 동안 기체·장면을 바꾸거나 저장하는 editor 요청(새 편집 op 전부와 #39의 `load`·`new`·`save`·`enter`·`leave`)은, 에디터가 아직 안정되지 않았으면 `not_ready`("다른 편집이 진행 중")를 돌려주고, 안정됐으면 진행 중 편집의 결과 검사를 먼저 한다. 맞으면 그 편집을 끝내고 같은 호출에서 요청을 처리한다. 어긋나면 되돌리기(원본 다시 불러오기 또는 빈 에디터 다시 열기)를 시작하고, 요청은 처리하지 않고 `not_ready`("앞 편집을 되돌리는 중, 잠시 뒤 다시 요청")를 돌려준다. 되돌리기는 같은 호출 안에서 끝나지 않으므로(다시 불러온 뒤 안정 대기, 또는 장면 다시 로드), 그 호출에서 다른 요청을 처리하면 진행 중 편집이 둘이 되거나, 아직 안정되지 않은 기체(원본이 빈 기체면 장면 로드가 끝나기 전의 실패한 기체)를 대상으로 저장·편집하게 되기 때문이다. 되돌리기가 끝나면 앞 편집의 최종 결과(`bad_request`)는 그 번호의 `edit_result`로 받을 수 있다. 그래서 끝나지 않은 편집의 되돌리기가 그 뒤 불러온 기체를 덮어쓰거나, 검사 전 결과가 저장되는 일이 없다. 조회 도구는 그대로 답한다. 마지막으로 끝난 편집의 최종 결과(번호와 응답)는 장면이 바뀌어도 남겨 두어, 같은 번호의 `edit_result`에 같은 결과를 돌려준다. 서버가 10초 안에 최종 결과를 받지 못하면 `not_ready`("편집이 아직 끝나지 않음, 잠시 뒤 `editor_get_ship`으로 확인")를 돌려준다. 이렇게 남은 진행 중 편집이나 서버가 끊겨 버려진 진행 중 편집은, 안정된 뒤 다음 편집 요청이나 `edit_result`가 먼저 결과 검사(어긋나면 되돌리기)를 마쳐 끝낸다. 우리 요청이 아닌 경로(게임 화면 조작 등)로 에디터를 떠나거나 장면이 바뀌면 진행 중 편집을 버리고, 그 번호의 `edit_result`에는 버린 사실을 담은 `not_ready`를 돌려준다.
  검토한 대안: Mod가 자기 프레임 갱신에서 스스로 검사. 모듈에 프레임 콜백이 있는지와 상관없이, 서버가 되묻는 방식이 #39 도구 구조(얇은 서버 층 + Mod 처리기)와 맞고 기다림이 서버 쪽에만 있다.

## 인터페이스·데이터

새 MCP 도구(Mod 처리기 `editor/<op>`와 1:1). 기체를 바꾸는 도구는 성공하면 `{changed:[id...], removed?:[id...], symmetry_cleared?:[id...], dropped_held_part, ship:<editor_get_ship 형식>}`를 돌려준다. `removed`는 제거(`remove`)와 교체(`replace`) 응답에 들어가고, `symmetry_cleared`는 재루트로 대칭이 풀린 부품이 있을 때만 들어간다. 부품 설정 변경(`set_part_settings`)도 같은 형식이다.

| 도구 | op | 인자 |
|---|---|---|
| `editor_place_root` | `place_root` | `part`(부품 이름), `stage?` |
| `editor_set_root` | `set_root` | `id` |
| `editor_attach_node` | `attach_node` | `target`(id), `target_node`, `part`, `node?`, `stage?` |
| `editor_attach_surface` | `attach_surface` | `target`, `part`, `height`(기본 0), `angle`(기본 0), `symmetry`(1~8, 기본 1), `stage?` |
| `editor_remove` | `remove` | `id` |
| `editor_replace` | `replace` | `id`, `part` |
| `editor_move` | `move` | `id`, `offset?`(`[x,y,z]` m), `rotate?`(`[x,y,z]` 도) |
| `editor_get_part_settings` | `part_settings` | `id` |
| `editor_set_part_settings` | `set_part_settings` | `id`, `resources?`(`[{name, amount}]`), `fields?`(`[{module, field, value}]`), `symmetry`(기본 true) |
| `editor_get_stages` | `stages` | 없음 |
| `editor_set_stage` | `set_stage` | `id`, `stage`(기존 번호, 또는 가장 큰 번호 + 1이면 새 스테이지) |
| `editor_swap_stages` | `swap_stages` | `a`, `b`(서로 다른 기존 번호) |

`stage?` 인자는 모두 기존 번호 또는 가장 큰 번호 + 1(스테이지가 없으면 0)만 받는다.

기체를 바꾸는 op(`place_root`·`set_root`·`attach_node`·`attach_surface`·`remove`·`replace`·`move`·`set_part_settings`·`set_stage`·`swap_stages`)의 Mod 응답은 `{edit: <번호>, pending: true}`다(인자 검사·쓰기 전 검사에서 거부하면 바로 `bad_request`). 서버만 쓰는 Mod op `edit_result`(인자 `edit`)는 안정 전이면 `{pending: true}`, 안정 뒤면 위 최종 결과나 `bad_request`를 돌려준다. MCP 도구에는 최종 결과만 보인다.

부품 설정 응답:

```json
{ "id": 4294, "name": "liquidEngine2",
  "resources": [ { "name": "LiquidFuel", "amount": 90, "max": 90, "tweakable": true } ],
  "fields": [ { "module": "ModuleEngines", "module_index": 2, "field": "thrustPercentage", "title": "Thrust Limiter",
                "type": "float", "value": 100, "min": 0, "max": 100, "step": 0.5 },
              { "module": "ModuleGimbal", "module_index": 3, "field": "gimbalLock", "title": "Gimbal", "type": "bool", "value": false } ] }
```

- `fields[].module`은 모듈 클래스 이름, 같은 이름 모듈이 여럿이면 `module_index`로 가린다(변경 인자도 `module` 대신 `module_index`를 받는다).
- 선택형(`UI_ChooseOption`·`UI_Cycle`)은 `options` 목록을 준다.

스테이지 응답: `{ "stages": [ { "stage": 2, "parts": [ { "id": 1, "name": "liquidEngine2" } ] }, ... ] }`(큰 번호부터, 먼저 작동하는 순).

오류 코드: #39와 같다. `not_ready`(에디터 아님·준비 안 됨), `bad_request`(인자, 없는 부품 이름·식별자, 루트 규칙, 노드 없음·차 있음, 표면 부착 불가·광선 빗나감, 루트 제거, 교체 시 부모 쪽·자식 쪽으로 다시 붙일 수 없음·루트가 될 수 없는 부품, 범위 밖 설정 값, 스테이지 대상 아님, 범위 밖 스테이지 번호·같은 번호 바꾸기·비대상 부품에 `stage` 지정, 대칭 묶음에 표면 부착하며 `symmetry` 2 이상, 루트 이동, 거울 반사면을 정할 수 없음, 쓰기 전 무결성 검사 실패, 다시 불러오기 결과 불일치. 마지막 경우 빈 기체로 되돌렸으면 에디터를 다시 열었다는 문구 포함).

## 오류·위험

- **안정 판정이 게임 작업 끝을 가리키는지**: 판정은 `Part.editorStarted`, `StageManager`의 private `sortRoutine`(리플렉션으로 읽기만), 불러온 뒤 3프레임 이상이다. 이 셋이 맞은 뒤에도 게임이 기체를 바꾸는 작업(예: `OnPartUpdateStageability`가 다시 켠 재정렬)이 남는지 tasks 단계 1의 위험 확인에서 안정 직후와 1초 뒤 조회를 비교해 확인한다. 리플렉션 대상 필드를 찾지 못하면 판정에서 그 조건을 빼지 않고 편집을 `not_ready`로 거부한다(조용히 틀린 응답을 내지 않도록). 남는 작업이 있으면 plan을 고친다(의도 변경).
- **응답 시간**: 편집마다 다시 불러오기와 3프레임 이상 대기가 들어가 수백 ms가 걸릴 수 있다. 서버 되묻기 상한 10초를 넘기면 위 결정대로 `not_ready`를 돌려준다.
- **썸네일 자리 표시 파일**: `Ships/@thumbs/editor/edit.png` 하나가 게임 폴더에 남는다. 수정 시각 맞추기가 실패하면 렌더링이 다시 일어날 뿐 편집은 맞게 된다. 게임은 렌더링 여부와 상관없이 "Autogen thumbnail" 로그를 먼저 남기므로(`ShipConstruction.cs:2241`, 검사는 2253) 로그로는 판정할 수 없다. tasks 단계 1의 위험 확인에서 편집 전후 `edit.png`의 크기·수정 시각이 우리가 쓴 자리 표시 파일 그대로인지(게임이 덮어쓰지 않았는지)로 확인한다.
- **거울 대칭 반사면**: 짝 위치 차이로 반사면을 구하므로, 두 짝이 같은 위치(대칭면 위)에 있으면 면이 정해지지 않는다. 이때는 `mir` 값이 −1인 축을 면의 법선으로 쓴다. 그것도 없으면 그 묶음의 이동은 거부한다.
- **새 부품 PART 노드 만들기**: 프리팹에서 직접 쓰는 값 줄이 게임 버전에 따라 `SaveShip`과 달라질 수 있다. 결과 검사(요약 6단계)가 부품 수·부착을 확인하므로 어긋나면 되돌리기로 끝난다. T006에서 확인한 부품 외의 부품(엔진·분리기·탱크·날개 등)은 단계 4 검증에서 확인한다.
- **다시 불러오기 비용·부작용**: 편집마다 에디터를 재시작하므로 카메라·선택이 초기화되고 시간이 걸린다(수백 ms 예상). 게임의 되돌리기(undo) 기록은 남지 않는다(spec 범위 밖).
- **`LoadShipFromFile`이 기억하는 파일 경로**: 에디터가 임시 파일 경로를 기억할 수 있다. `editor_save`는 경로를 직접 계산하므로 영향이 없고, 기체 이름(`ship` 값)은 원본 값을 그대로 둔다.
- **표면 충돌체**: 일부 부품은 충돌체가 여럿이거나 비어 있다. 대상 부품의 모든 활성 충돌체에 광선을 쏘아 가장 가까운 점을 쓰고, 없으면 거부한다.
- **노드 크기**: 크기가 다른 노드끼리 붙는 것은 게임도 허용하므로 막지 않는다(도구 설명에 `editor_list_parts`의 `sizes`를 보라고 적는다).
- **다른 사람의 비행·장면**: 편집 도구는 에디터 안에서만 동작해 장면을 바꾸지 않는다.

## 검증 전략

KSP 확인은 화면을 보지 않고 MCP 응답으로만 한다. 시작 전에 `get_status`로 장면과 다른 비행 진행 여부를 확인한다. 시험 craft는 `bridge-test-` 이름으로 저장하고 끝나면 지운다. 순수 로직(`CraftDoc`, `AttachMath`, `Staging`)은 단위 테스트로 확인한다.

- C1: 빈 에디터에서 `editor_place_root(mk1pod.v2)` → 부품 1개. 다시 부르면 `bad_request`. 탱크를 아래에 붙인 뒤 `editor_set_root(탱크)` → 루트가 탱크이고, 포드의 부모가 탱크이며, 부품 수가 같다. 빈 기체로 되돌리는 경로는 단위 테스트로 판정(원본 부품 0개 → 파일 대신 다시 열기)을 확인한다.
- C2: 포드 `bottom`에 탱크 부착 → 탱크 `parent=포드`, `parent_node=bottom`, `node=top`. 같은 노드에 다시 → `bad_request`. 없는 노드 → `bad_request`.
- C3·C4: 탱크에 방사 분리기를 `symmetry=3`으로 표면 부착 → 3개, `attach=surface`, 서로 `symmetry`에 들어 있음. 그 분리기의 노드에 부스터 탱크를 붙이면 3개 모두에 붙고 대칭 묶음이 된다. 부스터 탱크 하나에 핀을 `symmetry=1`로 표면 부착하면 세 부스터 모두에 같은 자리로 붙고 대칭 묶음이 된다. 같은 요청을 `symmetry=2`로 하면 `bad_request`. 표면 부착 불가 대상 → `bad_request`. 표면 노드 방향이 ±Z인 부품(예: `radialFlatSolarPanel`)과 ±X인 부품(예: 방사 분리기)을 붙여 둘 다 바깥을 향하고 겉면에 붙는지 본다(위치가 대상 축에서 대상 반지름 이상 떨어져 있음). 각도·게임 공식은 단위 테스트로도 확인한다.
- C5: 분리기 하나 제거 → 대칭 짝과 그 아래 부스터까지 제거되고 `removed`에 모두 있다. 루트 제거 → `bad_request`.
- C6: 탱크를 더 긴 탱크로 교체 → 같은 부모·노드, 아래 엔진이 새 탱크 `bottom`에 붙어 있다. 새 탱크는 새 식별자이고 옛 식별자는 `removed`, 엔진은 `changed`에 있다. 엔진을 다른 엔진으로 교체하면 같은 스테이지, 탱크를 엔진으로 바꾸면 새 맨 앞 스테이지, 분리기를 탱크로 바꾸면 스테이지 구성에서 빠진다(단위 테스트로도 확인). 노드가 맞지 않는 부품, 표면 자식이 있는데 표면 부착을 받지 않는 부품으로 교체 → `bad_request`이고 기체 그대로. 루트 포드를 다른 포드로 교체 → 새 포드가 루트이고 아래 부품이 그대로 붙어 있다. 검사 규칙은 단위 테스트로도 확인한다.
- C7: 방사 대칭 부품 `editor_move(offset=[0,0.5,0])` → 짝도 같은 높이로 옮겨지고 부착 관계 유지. SPH에서 거울 대칭 날개 한 쌍을 불러와(기본 제공 SPH craft) 한쪽을 앞뒤로 옮기고 요 방향으로 돌리면 짝이 대칭면 반대쪽에서 같은 만큼 움직인다. 방사·거울 이동량 변환은 단위 테스트로도 확인한다.
- C8·C9: 엔진 설정에 추력 제한·짐벌 필드, 분리기 설정에 분리 힘 필드가 있다. 추력 제한 50 → 다시 조회하면 50, 대칭 짝도 50. 범위 밖 값 → `bad_request`. 탱크 연료 양 절반 → 반영. 최대량 초과 → `bad_request`.
- C10: 빈 에디터에서 첫 스테이지 대상 부품을 `stage=0`으로 붙이면 받아들이고 `stage=1`이면 `bad_request`. `editor_get_stages`로 확인 → 엔진을 다른 스테이지로 옮김 → 반영되고 빈 스테이지 없음. `swap_stages` → 순서 바뀜. 없는 번호·같은 번호 `swap_stages`, 범위 밖 `set_stage`, 연료 탱크 옮기기, 연료 탱크 부착에 `stage` 지정 → `bad_request`. 대칭 3개 분리기가 같은 스테이지. 엔진(스테이지 0) 아래 TD-12 분리기를 붙이면 새 스테이지 1이고, `HeatShield1`·도킹 포트(`dockingPortLateral`)를 붙이면 스테이지 대상이 아니고 성공하며, 그 부품에 `stage`를 지정하면 `bad_request`이고 기체 그대로다. 분리기를 탱크로 교체하면 성공하고 분리기 스테이지가 없어진다(게임이 스테이지 대상 여부를 정함). 저장·불러오기 뒤에도 같은지 C13에서 확인.
- C11: 편집 도구 응답의 `ship`과 바로 뒤 `editor_get_ship` 결과를 비교해 같고, 응답 뒤 1초 이상 기다려 다시 부른 `editor_get_ship`과도 같다(제거·교체로 스테이지가 빈 경우, 스테이지를 끈 부품이 든 기체 포함. 예: 열 차폐막 `HeatShield1`의 분리 스테이지를 끈 상태로 저장한 craft에 부품을 붙임). 진행 중 편집이 있을 때 다른 편집·`editor_load`·`editor_save` → 안정 전이면 `not_ready`(고장 주입 진단 처리기로 안정을 늦춰 확인), 안정 뒤면 진행 중 편집을 먼저 마무리하고 처리(마무리가 되돌리기로 이어지면 그 요청은 `not_ready`이고 처리되지 않음. 진단 처리기로 결과 불일치를 강제해 확인하며, 원본이 빈 기체인 경우 `editor_save`가 파일을 쓰지 않는지도 본다). 빈 에디터에서 적용 단계 실패를 강제하면 그 응답에서 바로 안내가 담긴 `bad_request`가 오고, 같은 번호로 다시 물어도 같은 결과다. `changed`·`removed`가 위 결정대로인지 부착·제거·교체·재루트 응답에서 본다.
- C12: 에디터 밖에서 편집 도구 → `not_ready`. 위 거부 경우마다 거부 뒤 `editor_get_ship`이 거부 전과 같다. `editor_list_parts`에 없는 부품 이름(예: `kerbalEVA`, 깃발)으로 부착 → `bad_request`. 재루트 뒤 실패 되돌리기는 고장 주입 진단 처리기(임시, 검증 뒤 지움)로 `MakeRoot` 뒤 실패를 강제해, 되돌린 뒤 `editor_get_ship`(루트 포함)이 요청 전과 같은지 본다. 깨진 `link`·`sym`·`attN`·`srfN` 참조를 만든 노드는 쓰기 전 검사에서 거부되는지 단위 테스트로 확인한다. 끝나고 Player.log에 이 모듈의 예외와 "Loading Failed"가 없다.
- C13: 빈 에디터에서 포드 → 낙하산 → 분리기 → 탱크 → 엔진(위 단) → 분리기 → 탱크 → 엔진(아래 단), 아래 단에 방사 대칭 핀을 붙이고 스테이지를 짠다. `editor_save(bridge-test-2stage)` → `editor_new` → `editor_load` → `editor_get_ship`의 부품 이름·부모·부착·노드·대칭(같은 순서 부품끼리 대응)과 `editor_get_stages`가 저장 전과 같다. `editor_leave` 뒤 `debug_launch`로 이 craft를 발사하고 `get_status`가 `FLIGHT`·`ready=true`, 활성 기체 이름 `bridge-test-2stage`를 보인다(그 뒤 우주센터로 돌아온다).
- C14: `git diff --stat <기준점>`에 코어·`debug`·배포 스크립트 파일이 없다. KSP를 켠 채 배포해 클라이언트 재시작 없이 새 도구가 호출된다.
- 빌드: `dotnet build KspBridge.slnx` 경고 0, `dotnet test` 통과.

## 원칙 점검

- 적용 규칙
  - `workflow/spec-driven.md`, `git/github-issue.md`(#40)
  - `dotnet/project-creation.md`: 새 프로젝트 없음(기존 editor 모듈·테스트 프로젝트에 파일 추가)
  - `git/commit-message.md`, `shell/powershell-windows.md`
  - 메모리 규칙: 코어 + 별도 도구(편집마다 별도 도구, 코어·`debug` 고치지 않음), 화면 없이 MCP로 검증, 게임 창 조작 요구하지 않음(들린 부품을 이유로 거부하지 않음), 장면 전환 전 `get_status`, 로드 직후 실패는 Mod 결함으로 다룸, 비행 추적은 MCP 상태로
  - 골격·#39 plan의 결정: 처리기는 메인 스레드에서만, Mod JSON은 자체 구현, 서버 모듈은 `IBridgeClient`만 사용, 대화상자를 띄우는 게임 API는 부르지 않음
- 충돌
  - net472(Mod 모듈과 테스트): 골격과 같은 이유(KSP 1.12.5 Unity Mono)로 예외.
  - 게임 private 필드 `StageManager.sortRoutine`을 리플렉션으로 읽는다(쓰지 않음): 재정렬 코루틴이 끝났는지 알려 주는 공개 값이 없고, 응답을 게임 작업이 끝난 뒤에 만들어야 C11을 지킬 수 있어서다. 필드를 찾지 못하면 편집을 거부한다.
  - C12 확인에 Player.log를 본다: 게임 안 예외가 남지 않았음은 MCP 응답으로 알 수 없어 확인 용도로만 읽는다(#39와 같음).
