# flight-observation

파일: src/KspBridge.Mod.Modules.Flight/KspBridge.Mod.Modules.Flight.csproj, src/KspBridge.Mod.Modules.Flight/FlightModule.cs, src/KspBridge.Mod.Modules.Flight/FlightReadiness.cs, src/KspBridge.Mod.Modules.Flight/FlightSnapshot.cs, src/KspBridge.Mod.Modules.Flight/FlightData.cs, src/KspBridge.Mod.Modules.Flight/FlightReport.cs, src/KspBridge.Mod.Modules.Flight/StageRef.cs, src/KspBridge.Mod.Modules.Flight/Attitude.cs, src/KspBridge.Mod.Modules.Flight/Vec.cs, src/KspBridge.Server.Modules.Flight/KspBridge.Server.Modules.Flight.csproj, src/KspBridge.Server.Modules.Flight/FlightTools.cs, tests/KspBridge.Mod.Modules.Flight.Tests/KspBridge.Mod.Modules.Flight.Tests.csproj, tests/KspBridge.Mod.Modules.Flight.Tests/AttitudeTests.cs, tests/KspBridge.Mod.Modules.Flight.Tests/StageRefTests.cs, tests/KspBridge.Mod.Modules.Flight.Tests/FlightReportTests.cs, tests/KspBridge.Mod.Modules.Flight.Tests/FlightReadinessTests.cs

비행 장면의 활성 기체 상태를 돌려주는 MCP 도구 `get_flight`(조회 전용). Δv·연소 시간·스테이지 TWR은 게임 내장 Δv 계산기의 값을 쓴다. 괄호 안 D번호는 docs/specs/관측/decisions.md의 결정이다.

## 용어

- 활성 기체: 비행 장면에서 게임이 조종 대상으로 잡은 기체(`get_status`의 `vessel`과 같은 기체).
- 게임 계산기: 게임 내장 Δv 계산기. 스테이지마다 Δv·연소 시간·TWR(스로틀 100%)을 현재 위치의 대기압 기준, 해면 기준, 진공 기준으로 계산한다.
- 스테이지 번호: 게임 스테이징 순서의 번호(`check_ship`·`editor_get_stages`와 같다). 큰 번호가 먼저 작동한다.
- 마지막으로 작동한 스테이지: 스테이징으로 가장 최근에 작동했고 그 스테이지의 부품이 아직 기체에 남아 있는 스테이지의 번호. 남아 있지 않으면 없다(발사 전, 그 스테이지의 부품이 모두 떨어진 경우). 그 스테이지의 부품은 스테이지 번호가 그 스테이지 이상이고 스테이징 아이콘이 있는 부품이다. 분리를 마친 분리기처럼 아이콘이 없어진 부품은 세지 않는다(D018·D020).
- 다음 스테이지: 다음 스테이징 때 작동할 스테이지의 번호(발사 전에는 가장 큰 스테이지 번호). 남은 스테이지가 없으면 없다.
- 기준 스테이지: 기본 응답의 스테이지 Δv·연소 시간·TWR을 보이는 스테이지. 마지막으로 작동한 스테이지가 있으면 그 스테이지, 없으면 다음 스테이지다. 게임 계산기가 그 스테이지의 값을 내지 않으면 Δv·연소 시간·TWR은 0이다(D006·D018).
- 지표 기준 자세: 게임 내비볼과 같은 기준. 피치는 수평면에서 기수 방향까지의 각(위가 +, −90°~90°), 헤딩은 북쪽에서 시계 방향으로 잰 기수 방향(0°~360°), 롤은 기수 축 둘레의 기울기(−180°~180°).

## 요구(완료 조건)

- R1. 비행 장면의 활성 기체 상태를 돌려주는 MCP 도구(`get_flight`)가 있다. 기체를 바꾸지 않는 조회다. 새 Mod 모듈과 MCP 서버 모듈 쌍(`flight`)으로 더해지고 핫리로드로 반영된다(D001).
- R2. 기본 응답에 다음 값이 들어간다(단위는 필드 이름이나 설명에 밝힌다).
  - 식별·시각: 기체 이름, 상황(게임 상황 이름), 천체 내부 이름, 게임 시각(UT, s), 임무 경과 시간(s)
  - 위치·고도: 위도·경도(°), 해발 고도(m), 지형(바다 위는 수면) 위 고도(m)
  - 속도: 지표 속도, 궤도 속도, 수직 속도(위가 +), 수평 지표 속도(m/s)
  - 궤도 요소: Ap 고도·Pe 고도(해발, m), 경사각(°), 이심률, 장반경(m), 주기(s), Ap까지 남은 시간·Pe까지 남은 시간(s). 열린 궤도(이심률 1 이상)이면 주기·Ap 고도·Ap까지 남은 시간은 `null`이다.
  - 자세: 지표 기준 피치·헤딩·롤(°)
  - 조종 상태: 스로틀(0~1), SAS 켜짐 여부, RCS 켜짐 여부
  - 시간 가속(`time_warp`): 지금 적용된 배율(`rate`), 방식(`mode`: `none`·`rails`·`physics`, 배율이 1이면 `none`), 활성 기체가 물리 계산에서 빠져 있는지(`on_rails`)(시간-가속 D004)
  - 질량·추력: 기체 질량(t), 현재 추력 합(kN), 현재 TWR(현재 추력 ÷ (질량 × 현재 위치 중력))
  - 스테이지: 마지막으로 작동한 스테이지(없으면 `null`), 다음 스테이지(없으면 `null`), 기준 스테이지의 번호·Δv(현재 위치 대기압 기준·진공)·연소 시간(s)·TWR(현재 위치 기준), 기체 전체 Δv 합(현재 위치 대기압 기준·진공)(D006)
- R3. 게임 계산기가 아직 값을 내지 않았으면 Δv·연소 시간·스테이지 TWR 값과 요청한 스테이지별 표는 `null`이고, 응답에 계산이 준비되지 않았다는 표시와 이유가 들어간다. 이때도 나머지 값은 정상으로 돌려준다.
- R4. 스테이지별 Δv·연소 시간·TWR은 게임 계산기의 값이다(D003). 무인 stock 기체 `ComSat Lx`를 에디터에서 `check_ship`(천체 Kerbin)으로 점검한 값과, 그 기체를 발사대에 올린 뒤(계산 준비 표시가 없는 응답의) `get_flight` 스테이지별 표의 해면·진공 Δv가 스테이지마다 1% 안에서 같다(두 값이 모두 0인 스테이지는 같다고 본다)(D011).
- R5. 인자 `include`로 요청하면 다음 항목을 더 준다. 요청하지 않으면 주지 않는다(D005). 모르는 항목은 `bad_request`이다.
  - `stages`: 작동 순서(큰 번호부터)로 스테이지마다 스테이지 번호, 시작·끝 질량(t), Δv(현재 위치 대기압 기준·해면·진공), TWR(현재 위치 기준·해면·진공), 연소 시간(s). 게임 계산기가 내는 스테이지 가운데 기준 스테이지 이하인 스테이지가 모두 들어간다. 발사대에서 스테이징 전이면 번호 집합은 같은 craft의 `editor_get_stages`와 같다.
  - `resources`: 기체 전체의 자원별 현재량·최대량(게임 자원 이름, 게임 단위)
  - `body`: 지금 천체의 반지름(m), 중력 상수 GM(m³/s²), 자전 주기(s), 대기 상한(m, 대기가 없으면 `null`), 영향권 반경(m, 영향권이 무한한 천체(Sun)는 `null`)(D009)
- R6. 같은 상태에서 R5 항목을 요청한 응답과 요청하지 않은 응답의 R2 값 구성은 같다.
- R7. 비행 장면이 아니면(지금 장면 이름을 이유에 넣음), 장면을 불러오는 중이면, 비행 장면이 준비되지 않았거나 활성 기체가 없으면 `not_ready`, 인자가 틀리면 `bad_request`를 돌려주며, 게임에 예외가 남거나 도구가 멈추지 않는다(D021·D022).
- R8. 실제 게임에서 MCP 응답만으로 다음이 맞는다. 천체 반지름·GM·자전 주기는 R5 천체 정보의 값을 쓴다.
  - 발사대(stock `Kerbal X`, 스테이징 전): 상황 `PRELAUNCH`, 지표 속도 0.5 m/s 미만, 피치 89°~91°, 궤도 속도가 그 위치의 자전 속도와 1% 안, 해발 고도가 `get_status`의 `altitude_m`과 1 m 안, 지형 위 고도 0 초과, 위도·경도가 stock 발사대 좌표(위도 −0.0972°, 경도 −74.5577°)와 0.01° 안(D012), 임무 경과 시간 0·UT 증가, 현재 추력·TWR 0, SAS·RCS 꺼짐, 마지막으로 작동한 스테이지 `null`·다음 스테이지는 표의 가장 큰 번호·기준 스테이지 값이 표의 그 스테이지 값과 같음
  - 무인 기체(stock `ComSat Lx`): 기체 질량이 같은 craft의 에디터 `mass_t`와 0.01 t 안(D010), 자원별 최대량이 같은 craft의 `editor_get_part_settings` 자원 합계와 0.001 × (그 자원을 가진 부품 수) 안에서 같고, 현재량은 `ElectricCharge`를 뺀 자원이 같은 허용치 안에서 같으며 `ElectricCharge` 현재량은 0 이상 최대량 이하(D026·D027)
  - 두 기체(`Kerbal X`·`ComSat Lx`)의 "해발 고도 − 지형 위 고도"(발사대 지형 높이)가 0.5 m 안에서 같음(D025)
  - 활주로(stock `Aeris 3A`): 헤딩 89°~91°, 롤 −2°~2°
  - 지정 궤도(`debug_set_orbit`으로 Kerbin Ap 120 km·Pe 90 km·경사각 30°): 이심률·주기가 케플러 식 값과 0.5% 안, 궤도 속도가 활력 방정식 값과 0.5% 안, Ap·Pe까지 남은 시간의 차가 주기의 절반과 1% 안, Ap까지 남은 시간이 짧으면 수직 속도 0 이상(길면 0 이하)

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-21 | 관측 | R1~R8 추가(기준 spec 처음 만듦) | docs/specs/관측/ |
| 2026-11-22 | 시간-가속 | R2 바꿈(`time_warp` 추가) | docs/specs/시간-가속/ |

표에 없는 이전 변경: 기록 없음
