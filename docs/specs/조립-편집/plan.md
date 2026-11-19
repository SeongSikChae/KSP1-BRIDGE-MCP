# 조립 편집: 부품 배치·부착·대칭·스테이지 구성 계획

## 요약

#39의 `editor` 모듈 쌍에 편집 도구를 더한다. 게임의 부착 처리(`EditorLogic.attachPart` 등)는 private이고 마우스 상태 기계(fsm)에 묶여 있어 직접 부를 수 없다. 그래서 구조를 바꾸는 편집은 **craft 노드 재구성** 방식으로 한다.

1. 지금 기체를 `ShipConstruct.SaveShip()`으로 craft 노드로 만든다(되돌리기용 원본도 이것).
2. 그 노드를 KSP에 의존하지 않는 순수 모델(`CraftDoc`)로 읽어 편집한다. 새 부품의 PART 노드, 부착 위치·회전 계산에 필요한 값(부품 노드 좌표, 표면 충돌 지점)은 게임에서 미리 구해 넘긴다.
3. 편집한 노드의 무결성을 파일로 쓰기 전에 검사한다. 모든 `link`·`sym`·`attN`·`srfN` 참조가 실제 PART를 가리키는지, 루트가 하나인지, 스테이지 번호가 정규화되어 있는지 본다. 틀리면 불러오지 않고 `bad_request`를 돌려준다. 게임 로더는 깨진 `link`에 대화상자를 띄우고(`ShipConstruct.LoadShip`의 "Loading Failed"), 깨진 `sym`에는 null 짝을 만들기 때문에, 불러온 뒤의 검사로는 막을 수 없다.
4. 편집한 노드를 임시 craft 파일로 쓰고 `EditorLogic.LoadShipFromFile`로 다시 불러온다. 이 함수는 장면을 다시 로드하지 않고, 같은 호출 안에서 에디터를 재시작해 기체를 만든다(#39에서 동기 완료를 확인했고, 디컴파일에서 `StartEditor(isRestart: true)` 경로를 확인함). 빈 에디터에 불러오므로 `craftID`가 유지된다(`SpawnConstruct`가 식별자를 다시 매기는 경우는 기존 기체가 있을 때뿐).
5. 불러온 결과를 검사한다(부품 수, 기대한 부착·대칭·스테이지). 어긋나면 원본으로 되돌리고 `bad_request`를 돌려준다. 원본에 부품이 있으면 원본 노드를 같은 방식으로 다시 불러온다. 원본이 빈 기체면 파일로 불러오지 않는다. 부품 0개 craft를 불러오면 `StartEditor`가 `ship.parts[0]`에서 예외를 내기 때문이다(`EditorLogic.cs:1567`). 이때는 #39 `editor_new`와 같은 방식으로 빈 에디터를 다시 열고, 오류 문구에 "에디터를 빈 상태로 다시 열었으니 `get_status`로 준비를 확인하라"고 쓴다.

예외 두 가지는 게임의 공개 API로 직접 한다.

- **루트 바꾸기**: 공개 static `EditorReRootUtil.MakeRoot(newRoot, oldRoot)`가 게임 에디터의 재루트를 그대로 한다(표면 부착 방향 뒤집기, 맞지 않게 된 대칭 묶음 정리까지 포함). 그 뒤 1~5단계로 다시 불러와, 새 루트가 craft의 첫 PART가 되게 하고 에디터 상태를 깨끗이 맞춘다.
- **부품 설정(C8·C9)**: 구조가 바뀌지 않으므로 다시 불러오지 않는다. 살아 있는 부품의 `PartResource.amount`와 모듈 필드(`BaseField`, 에디터 조작 `uiControlEditor`)를 바로 바꾼다.

## 바꿀 파일·구조

- `src/KspBridge.Mod.Modules.Editor/EditorModule.cs`: 새 처리기 등록(아래 인터페이스 표). 편집 처리기는 `RequireEditor()` 뒤에 `ShipEditor`를 부른다.
- `src/KspBridge.Mod.Modules.Editor/CfgNode.cs`(새 파일): KSP `ConfigNode`와 같은 모양의 순수 트리(이름, 값 목록, 하위 노드). `ConfigNode` ↔ `CfgNode` 변환은 `ShipEditor`에 둔다. 단위 테스트가 KSP·Unity 없이 돌게 하기 위함이다.
- `src/KspBridge.Mod.Modules.Editor/Geo.cs`(새 파일): 순수 `Vec3`·`Quat`(곱, 역, 벡터 회전, 축-각, from-to)과 KSP 문자열 형식(`x,y,z`, `x|y|z`, `x,y,z,w`) 읽기·쓰기.
- `src/KspBridge.Mod.Modules.Editor/CraftDoc.cs`(새 파일): craft 노드의 순수 편집 모델. PART 목록, `part = 이름_cid`, `link`, `attN`, `srfN`, `sym`, `pos`·`rot`·`attPos`·`attPos0`·`attRot`·`attRot0`, `mir`·`symMethod`, `istg`·`dstg`·`sidx`, `attm`을 다루고, 모르는 값·하위 노드(MODULE, RESOURCE 등)는 손대지 않고 그대로 둔다. 편집 연산: 루트 놓기, 노드 부착, 표면 부착(대칭 포함), 하위 트리 제거, 교체(루트 포함), 하위 트리 강체 이동·회전, 스테이지 번호 바꾸기, 스테이지 정규화, 쓰기 전 무결성 검사. 각 연산은 먼저 전부 검사하고, 통과한 뒤에만 고친다.
- `src/KspBridge.Mod.Modules.Editor/AttachMath.cs`(새 파일): 순수 기하 계산. 노드 부착 자세(대상 노드 위치·방향 → 새 부품 위치·회전, 정반대 방향 포함), 표면 부착 자세(게임 공식), 방사 대칭 각도, 두 짝의 실제 자세로 구한 대칭 변환(방사는 회전, 거울은 대칭면 반사)과 그 변환으로 옮긴 이동량.
- `src/KspBridge.Mod.Modules.Editor/ShipEditor.cs`(새 파일): KSP 쪽 편집 실행. 원본 저장, 부품 정보 수집(`AvailablePart.partPrefab`의 `attachNodes`, `srfAttachNode`, `attachRules`), 표면 충돌 지점 구하기(대상 부품의 `Collider.Raycast`), 새 부품 PART 노드 만들기, 임시 파일로 다시 불러오기, 결과 검사·되돌리기, 재루트.
- `src/KspBridge.Mod.Modules.Editor/PartSettings.cs`(새 파일): 부품 설정 조회·변경(C8, C9).
- `src/KspBridge.Mod.Modules.Editor/Staging.cs`(새 파일): 스테이지 조회(C10)와 스테이지 연산의 순수 규칙(번호 매기기, 빈 스테이지 없애기, 두 스테이지 바꾸기). 순수 규칙은 KSP 타입을 쓰지 않는다.
- `src/KspBridge.Mod.Modules.Editor/ShipView.cs`: 편집 결과용으로 `Describe`에 바뀐 부품 식별자 목록을 붙이는 도우미를 더한다(기존 형식은 그대로).
- `src/KspBridge.Mod.Modules.Editor/KspBridge.Mod.Modules.Editor.csproj`: 표면 충돌 계산용 `UnityEngine.PhysicsModule` 참조 추가.
- `src/KspBridge.Server.Modules.Editor/EditorTools.cs`: 새 MCP 도구(아래 표). 기존 도구와 같은 얇은 층.
- `tests/KspBridge.Mod.Modules.Editor.Tests/CraftDocTests.cs`, `AttachMathTests.cs`, `StagingTests.cs`(새 파일): 순수 로직 단위 테스트.
- `ReadMe.md`: 도구 목록에 새 `editor_*` 도구 추가. 편집이 쓰는 임시 파일(`GameData/KspBridge/PluginData/editor/edit.craft`)과 썸네일 자리 표시 파일(`Ships/@thumbs/editor/edit.png`)을 적는다.

## 결정

- **구조 편집은 craft 노드 재구성 + 다시 불러오기**: 게임의 craft 불러오기 경로가 부착 노드, 대칭, 스테이지, 자원, 모듈 상태를 모두 일관되게 만든다. 요청 하나가 노드 하나의 편집이므로 전부 적용 또는 전혀 적용 안 함(C12)이 자연스럽고, 편집 규칙을 KSP 없이 단위 테스트할 수 있다.
  검토한 대안: 살아 있는 부품에 직접 부착(`setParent`, `AttachNode.attachedPart`, `ship.Add`, 이벤트 발생). 게임의 private 부착 처리를 흉내 내야 해서 빠뜨린 상태가 생기기 쉽다. `EditorLogic.SpawnPart`: 부품을 마우스에 들린 상태로 만들어 fsm 입력이 필요하다.
- **새 부품의 PART 노드는 게임이 만들게 한다**: `AvailablePart.partPrefab`을 `Object.Instantiate`로 복제해 임시 `ShipConstruct`에 넣고 `SaveShip()`으로 PART 노드를 얻은 뒤, 복제본을 바로 없앤다. 모듈·자원 기본값을 직접 쓰지 않아도 된다. 이 노드에 계산한 `pos`·`rot`, 연결(`attN`·`srfN`·`link`·`sym`), 새 `craftID`(기체 안에서 쓰지 않는 값)를 넣는다.
  검토한 대안: PART 노드를 직접 쓰기. MODULE·RESOURCE를 빠뜨리면 불러온 부품이 달라질 수 있다.
- **쓰기 전에 무결성을 검사한다**(검토 1회차 지적 3): 게임 로더는 깨진 `link`에 대화상자를 띄우고(MCP로 닫을 수 없음), 깨진 `sym`에는 null 짝을 넣는다. 그래서 불러오기 전에 `CraftDoc`이 모든 참조를 검사해 원인을 막는다. 불러온 뒤의 검사(아래)는 게임 쪽 차이를 잡는 두 번째 단계다.
- **다시 불러오기 결과를 검사하고, 어긋나면 원본으로 되돌린다**: 게임 로더는 실패를 알리지 않는다(#39에서 확인). 부품 수와 기대한 부모·대칭·스테이지를 비교해, 다르면 원본으로 되돌리고 `bad_request`(무엇이 어긋났는지)를 돌려준다. 원본이 빈 기체면 파일 대신 빈 에디터 다시 열기로 되돌린다(검토 1회차 지적 2, 요약 5단계).
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
- **거울 대칭도 다룬다**(검토 1회차 지적 6): SPH 기체나 거울 대칭 craft를 불러오면 `symMethod = Mirror` 묶음이 들어온다. 대칭 짝 사이의 변환은 대칭 방식을 가정하지 않고 두 짝의 실제 자세에서 구한다. 방사는 묶음 부모 축 둘레 회전이고, 거울은 묶음 부모를 지나는 대칭면 반사다. 반사면은 두 짝의 위치 차이에 수직인 면이고, 짝의 `mir` 값과 맞는지 확인한다. 대칭 묶음에 노드 부착할 때 새 부품은 대상의 `symMethod`를 이어받고, 짝마다 대상 짝의 `mir`를 따른다. 거울 짝의 자세는 짝 대상 노드 기준으로 노드 부착 계산을 다시 해서 얻는다.
- **제거·교체·이동은 대칭 짝까지 함께**: 대상과 대칭 짝의 하위 트리 전체가 대상이다. 남은 부품의 `attN`에서 지운 부품을 가리키던 칸은 `Null_0`으로, `link`·`sym`은 지운다.
- **교체**: 새 부품은 옛 부품의 부모 쪽 노드 이름(표면 부착이면 표면)으로 같은 자리에 붙인다. 노드로 붙은 자식은 새 부품의 같은 이름 노드로 옮기고, 그 노드의 위치 변화만큼 자식 하위 트리를 강체로 옮긴다. 표면 부착 자식은 월드 자세를 그대로 두고 부모만 바꾼다(새 부품의 겉면과 떨어질 수 있음을 도구 설명에 적는다). 바꾸기 전에 아래를 모두 검사하고, 하나라도 맞지 않으면 거부한다(검토 1회차 지적 5).
  - 부모 쪽: 옛 부품이 노드로 붙어 있었으면 새 부품에 같은 이름 노드가 있다. 표면으로 붙어 있었으면 새 부품의 `attachRules.srfAttach`가 참이고 `srfAttachNode`가 있다.
  - 자식 쪽: 노드 자식마다 새 부품에 같은 이름 노드가 있다. 표면 자식이 있으면 새 부품의 `attachRules.allowSrfAttach`가 참이다.
  - **루트 교체**: 부모가 없으므로, 새 부품을 옛 루트의 자세(위치·회전)에 놓고 자식 쪽 검사만 한다. 새 부품이 루트가 될 수 있어야 한다(`attachRules.allowRoot`). 교체한 부품이 craft의 첫 PART가 된다.
- **이동·회전**: `offset`(m)은 부모 부품의 로컬 좌표, `rotate`(도, x·y·z 오일러)는 부품 자신의 로컬 좌표로 받는다. 부품과 하위 트리를 강체로 옮기고, `attPos`·`attRot`도 같은 양만큼 맞춘다. 대칭 짝에는 위 "거울 대칭도 다룬다"의 짝 변환으로 이동량과 회전을 옮겨 넣는다. 방사는 회전한 이동량과 같은 로컬 회전이다. 거울은 반사한 이동량과, 반사면 기준으로 뒤집은 회전이다(요·롤 부호 반전). 루트 부품 이동은 기체 전체 이동이라 거부한다.
- **부품 설정 = 에디터에서 보이는 조작만**: 모듈 필드 중 `guiActiveEditor`이고 `uiControlEditor`가 있는 것(`UI_FloatRange`·`UI_Toggle`·`UI_ChooseOption`·`UI_Cycle` 등)만 목록에 넣는다. 값을 바꿀 때는 범위·선택지를 검사하고 `BaseField.SetValue`와 조작의 `onFieldChanged`를 불러 게임이 화면에서 바꾼 것과 같게 한 뒤 `GameEvents.onEditorShipModified`를 보낸다. 자원은 `isTweakable`인 것만 바꾼다. 대칭 짝에는 같은 모듈 순번·필드에 같은 값을 넣는다(`symmetry=false`면 그 부품만).
  검토한 대안: 모든 `KSPField` 허용. 에디터에서 바꿀 수 없게 한 값까지 바뀌어 게임과 다른 기체가 된다.
- **스테이지 번호는 게임의 `inverseStage`(큰 번호가 먼저 작동)**. 스테이지 대상 부품 = `hasStagingIcon`이 참이고 `stagingOn`인 부품. 새 스테이지 부품의 기본 스테이지는 지금 가장 큰 번호 + 1(맨 먼저 작동)이며, 부착 도구의 `stage` 인자로 정할 수 있다. 스테이지 편집도 `istg`·`sidx`를 고친 노드를 다시 불러오는 방식이다.
- **모든 구조 편집 뒤 스테이지를 정규화한다**(검토 1회차 지적 4): 에디터가 다시 시작되면 `StageManager.OnEditorStarted`가 2프레임 뒤 `SortIcons(deleteManualStages: true)`로 빈 스테이지를 지우고 번호를 다시 매길 수 있다(`StageManager.cs:501-503`, 2913 이후). 그러면 같은 요청 안에서 만든 C11 응답과 그 뒤 조회가 달라진다. 그래서 스테이지 옮기기뿐 아니라 부착·제거·교체·재루트 뒤에도 `CraftDoc`이 정규화한 상태로 쓴다. 정규화는 스테이지 대상 부품의 `istg`가 0부터 빈 번호 없이 이어지고, 같은 스테이지 안 `sidx`가 0부터 이어지며, 스테이지 대상이 아닌 부품의 `istg`는 부모 쪽 가장 가까운 스테이지 대상 부품을 따르는 상태다. 게임의 재정렬이 바꿀 것이 없게 하는 것이 목적이다. 재정렬이 실제로 아무것도 바꾸지 않는지는 T005에서 2프레임 이상 지난 뒤 다시 조회해 확인한다. 바꾸면 의도 변경으로 plan을 고친다.

## 인터페이스·데이터

새 MCP 도구(Mod 처리기 `editor/<op>`와 1:1). 기체를 바꾸는 도구는 성공하면 `{changed:[id...], removed?:[id...], dropped_held_part, ship:<editor_get_ship 형식>}`를 돌려준다.

| 도구 | op | 인자 |
|---|---|---|
| `editor_place_root` | `place_root` | `part`(부품 이름) |
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
| `editor_swap_stages` | `swap_stages` | `a`, `b` |

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

오류 코드: #39와 같다. `not_ready`(에디터 아님·준비 안 됨), `bad_request`(인자, 없는 부품 이름·식별자, 루트 규칙, 노드 없음·차 있음, 표면 부착 불가·광선 빗나감, 루트 제거, 교체 시 부모 쪽·자식 쪽으로 다시 붙일 수 없음·루트가 될 수 없는 부품, 범위 밖 설정 값, 스테이지 대상 아님, 쓰기 전 무결성 검사 실패, 다시 불러오기 결과 불일치. 마지막 경우 빈 기체로 되돌렸으면 에디터를 다시 열었다는 문구 포함).

## 오류·위험

- **스테이지가 다시 불러오기 뒤 유지되는지**: craft의 `istg`를 게임이 다시 계산할 수 있고, `StageManager`의 재정렬은 2프레임 뒤에 돈다. 정규화로 원인을 막고(결정 참조), 첫 단계에서 KSP로 확인한다(tasks 단계 1, 불러온 직후와 2프레임 이상 뒤 두 번 조회). 그래도 바뀌면 plan을 고친다(의도 변경).
- **썸네일 자리 표시 파일**: `Ships/@thumbs/editor/edit.png` 하나가 게임 폴더에 남는다. 수정 시각 맞추기가 실패하면 렌더링이 다시 일어날 뿐 편집은 맞게 된다. T005에서 편집 뒤 Player.log에 "Autogen thumbnail" 줄이 없는지 본다.
- **거울 대칭 반사면**: 짝 위치 차이로 반사면을 구하므로, 두 짝이 같은 위치(대칭면 위)에 있으면 면이 정해지지 않는다. 이때는 `mir` 값이 −1인 축을 면의 법선으로 쓴다. 그것도 없으면 그 묶음의 이동은 거부한다.
- **새 부품 PART 노드 만들기**: 프리팹 복제본을 `SaveShip`에 넣을 때 부품 초기화가 덜 되어 일부 값이 비거나 예외가 날 수 있다. 단계 1에서 확인하고, 안 되면 불러온 기존 craft의 같은 부품 노드 형식을 따라 직접 만드는 쪽으로 바꾼다(의도 변경).
- **다시 불러오기 비용·부작용**: 편집마다 에디터를 재시작하므로 카메라·선택이 초기화되고 시간이 걸린다(수백 ms 예상). 게임의 되돌리기(undo) 기록은 남지 않는다(spec 범위 밖).
- **`LoadShipFromFile`이 기억하는 파일 경로**: 에디터가 임시 파일 경로를 기억할 수 있다. `editor_save`는 경로를 직접 계산하므로 영향이 없고, 기체 이름(`ship` 값)은 원본 값을 그대로 둔다.
- **표면 충돌체**: 일부 부품은 충돌체가 여럿이거나 비어 있다. 대상 부품의 모든 활성 충돌체에 광선을 쏘아 가장 가까운 점을 쓰고, 없으면 거부한다.
- **노드 크기**: 크기가 다른 노드끼리 붙는 것은 게임도 허용하므로 막지 않는다(도구 설명에 `editor_list_parts`의 `sizes`를 보라고 적는다).
- **다른 사람의 비행·장면**: 편집 도구는 에디터 안에서만 동작해 장면을 바꾸지 않는다.

## 검증 전략

KSP 확인은 화면을 보지 않고 MCP 응답으로만 한다. 시작 전에 `get_status`로 장면과 다른 비행 진행 여부를 확인한다. 시험 craft는 `bridge-test-` 이름으로 저장하고 끝나면 지운다. 순수 로직(`CraftDoc`, `AttachMath`, `Staging`)은 단위 테스트로 확인한다.

- C1: 빈 에디터에서 `editor_place_root(mk1pod.v2)` → 부품 1개. 다시 부르면 `bad_request`. 탱크를 아래에 붙인 뒤 `editor_set_root(탱크)` → 루트가 탱크이고, 포드의 부모가 탱크이며, 부품 수가 같다. 빈 기체로 되돌리는 경로는 단위 테스트로 판정(원본 부품 0개 → 파일 대신 다시 열기)을 확인한다.
- C2: 포드 `bottom`에 탱크 부착 → 탱크 `parent=포드`, `parent_node=bottom`, `node=top`. 같은 노드에 다시 → `bad_request`. 없는 노드 → `bad_request`.
- C3·C4: 탱크에 방사 분리기를 `symmetry=3`으로 표면 부착 → 3개, `attach=surface`, 서로 `symmetry`에 들어 있음. 그 분리기의 노드에 부스터 탱크를 붙이면 3개 모두에 붙고 대칭 묶음이 된다. 표면 부착 불가 대상 → `bad_request`. 표면 노드 방향이 ±Z인 부품(예: `radialFlatSolarPanel`)과 ±X인 부품(예: 방사 분리기)을 붙여 둘 다 바깥을 향하고 겉면에 붙는지 본다(위치가 대상 축에서 대상 반지름 이상 떨어져 있음). 각도·게임 공식은 단위 테스트로도 확인한다.
- C5: 분리기 하나 제거 → 대칭 짝과 그 아래 부스터까지 제거되고 `removed`에 모두 있다. 루트 제거 → `bad_request`.
- C6: 탱크를 더 긴 탱크로 교체 → 같은 부모·노드, 아래 엔진이 새 탱크 `bottom`에 붙어 있다. 노드가 맞지 않는 부품, 표면 자식이 있는데 표면 부착을 받지 않는 부품으로 교체 → `bad_request`이고 기체 그대로. 루트 포드를 다른 포드로 교체 → 새 포드가 루트이고 아래 부품이 그대로 붙어 있다. 검사 규칙은 단위 테스트로도 확인한다.
- C7: 방사 대칭 부품 `editor_move(offset=[0,0.5,0])` → 짝도 같은 높이로 옮겨지고 부착 관계 유지. SPH에서 거울 대칭 날개 한 쌍을 불러와(기본 제공 SPH craft) 한쪽을 앞뒤로 옮기고 요 방향으로 돌리면 짝이 대칭면 반대쪽에서 같은 만큼 움직인다. 방사·거울 이동량 변환은 단위 테스트로도 확인한다.
- C8·C9: 엔진 설정에 추력 제한·짐벌 필드, 분리기 설정에 분리 힘 필드가 있다. 추력 제한 50 → 다시 조회하면 50, 대칭 짝도 50. 범위 밖 값 → `bad_request`. 탱크 연료 양 절반 → 반영. 최대량 초과 → `bad_request`.
- C10: `editor_get_stages`로 확인 → 엔진을 다른 스테이지로 옮김 → 반영되고 빈 스테이지 없음. `swap_stages` → 순서 바뀜. 연료 탱크를 옮기려 하면 `bad_request`. 저장·불러오기 뒤에도 같은지 C13에서 확인.
- C11: 편집 도구 응답의 `ship`과 바로 뒤 `editor_get_ship` 결과를 비교해 같다.
- C11: 편집 응답의 스테이지 값이 2프레임 이상 지난 뒤의 `editor_get_ship`과 같은지도 본다(제거·교체로 스테이지가 빈 경우 포함).
- C12: 에디터 밖에서 편집 도구 → `not_ready`. 위 거부 경우마다 거부 뒤 `editor_get_ship`이 거부 전과 같다. 깨진 `link`·`sym`·`attN`·`srfN` 참조를 만든 노드는 쓰기 전 검사에서 거부되는지 단위 테스트로 확인한다. 끝나고 Player.log에 이 모듈의 예외와 "Loading Failed"가 없다.
- C13: 빈 에디터에서 포드 → 낙하산 → 분리기 → 탱크 → 엔진(위 단) → 분리기 → 탱크 → 엔진(아래 단), 아래 단에 방사 대칭 핀을 붙이고 스테이지를 짠다. `editor_save(bridge-test-2stage)` → `editor_new` → `editor_load` → 부품 구성·부착·대칭·스테이지가 저장 전과 같다. `editor_leave` 뒤 `debug_launch`로 이 craft를 발사해 비행 장면이 된다(그 뒤 우주센터로 돌아온다).
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
  - C12 확인에 Player.log를 본다: 게임 안 예외가 남지 않았음은 MCP 응답으로 알 수 없어 확인 용도로만 읽는다(#39와 같음).
