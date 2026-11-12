---
status: done
category: ksp-bridge
feature: SASRCS
phase: 방안
issue: 9
---

# SASRCS 방안

이슈: [#9 SAS·RCS 도구: SAS 켜기·끄기와 모드, RCS 켜기·끄기와 병진 명령](https://github.com/SeongSikChae/KSP1-BRIDGE-MCP/issues/9)

관련: [0004-비행제어-방안.md](0004-비행제어-방안.md), [0005-비행제어-설계.md](0005-비행제어-설계.md)

## 문제

- MCP 클라이언트는 비행 계획 밖에서 스톡 SAS를 켜거나 모드(prograde 등)를 바꿀 수 없다. 화면에서 SAS 버튼이나 키를 눌러야 한다.
- RCS를 켜고 끄거나 병진(translate) 입력을 줄 수단이 없다. 도킹 접근, 미세 속도 조정, 분리 뒤 이격을 MCP만으로 할 수 없다.
- 비행 계획의 자세 제어는 리액션 휠 토크만 쓴다. 스톡 RCS 부품(`ModuleRCS`)은 RCS 액션 그룹이 켜져 있을 때만 토크를 내는데(`GetPotentialTorque`가 `rcs_active`를 확인), Controller는 RCS 그룹을 건드리지 않는다. 진공에서 휠 토크가 작은 기체는 자세를 늦게 잡는다.
- 지금 `get_status`·`get_flight`에는 SAS·RCS 상태가 없다.

## 현재 동작 (조사)

- `AttitudeController.Attach`가 계획 시작 때 SAS 상태를 기록하고 끈다. `Fly`는 매 틱 사용자가 켠 SAS를 다시 끈다. `Coordinator.Release`가 계획 종료 때 기록한 SAS 상태를 복원한다(`RestoreSas`).
- Controller는 `FlightCtrlState`의 pitch/roll/yaw만 쓴다. 병진 축(X/Y/Z)은 쓰지 않는다.
- `AvailableTorque`는 `ITorqueProvider` 전체를 더하므로, RCS 그룹이 켜져 있으면 RCS 토크도 이미 포함된다. 사용자가 계획 전에 RCS를 켜 두면 Controller의 회전 입력으로 RCS가 분사된다.

## 목표

- MCP 도구만으로(화면·키 입력 없이) SAS를 켜고 끄고, 모드를 바꾸고, RCS를 켜고 끄고, 병진 펄스를 줄 수 있다.
- 비행 계획이 실행 중일 때는 계획의 제어와 충돌하는 입력을 받지 않는다.
- 비행 계획이 진공 자세 제어에 RCS를 쓸 수 있다.
- 상태 조회로 SAS·RCS 상태를 확인할 수 있다.

## 대안과 채택

### 1. 비행 계획 실행 중의 요청 (합의)

| 대안 | 내용 | 단점 |
|---|---|---|
| **모두 409 거부 (채택)** | 계획이 실행 중이면 SAS 변경, RCS 켜기·끄기, 병진 요청을 모두 409로 거부한다 | 계획 중 RCS 보조는 계획 단계 옵션(3절)으로만 쓸 수 있다 |
| RCS 켜기·끄기만 허용 | RCS 그룹은 Controller 입력과 충돌하지 않으므로 허용하고 병진만 거부 | 계획이 RCS를 켜고 끄는 3절 옵션과 상태 소유자가 둘이 된다 |

### 2. RCS 병진 명령 (합의)

| 대안 | 내용 | 단점 |
|---|---|---|
| **시간 지정 펄스 (채택)** | x/y/z(-1~1)와 지속 시간을 받아 그 시간 동안만 입력하고 0으로 돌린다. 씬 전환·활성 기체 변경 때 즉시 멈춘다 | 긴 연속 분사는 여러 번 나눠 요청해야 한다 |
| 유지형 | 0으로 다시 보낼 때까지 계속 입력 | 멈추는 요청을 빠뜨리면 추진제를 다 쓸 때까지 분사한다 |
| 둘 다 | 시간을 주면 펄스, 생략하면 유지형 | 유지형의 위험이 그대로 남는다 |

### 3. 비행 계획의 자세 제어에 RCS 사용 (합의)

| 대안 | 내용 | 단점 |
|---|---|---|
| **계획 단계 옵션 (채택)** | 단계에 RCS 사용 옵션을 두면 그 단계가 시작될 때 Controller가 RCS 그룹을 그 값으로 맞춘다. 계획이 RCS를 바꾼 뒤 옵션을 생략한 단계가 시작되면 계획 전 상태로 되돌린다. 계획이 끝나면 계획 전 RCS 상태로 되돌린다(SAS 복원과 같은 방식). 단계 중에는 강제하지 않는다 | 단계마다 지정해야 한다 |
| 포함하지 않음 | 계획은 RCS 그룹을 건드리지 않는다 | 1절에서 계획 중 RCS 요청을 거부하므로, 계획 전에 켜 두는 방법밖에 없다 |
| 자동 사용 | 휠 토크가 모자라면 Controller가 알아서 켠다 | 판정 기준(토크·고도·대기)이 따로 필요하고 추진제를 예고 없이 쓴다 |

### 4. 도구 형태 (합의)

| 대안 | 내용 | 단점 |
|---|---|---|
| **도구 3개 (채택)** | `set_sas(on, mode, reference)`, `set_rcs(on)`, `rcs_translate(x, y, z, seconds)`. `reference`(navball 기준 프레임 orbit·surface·target)는 검토 뒤 추가 결정(설계 0014) | 도구 수가 늘어남 |
| 도구 2개 | `set_sas`와 `set_rcs(on, translate?)` | 켜기·끄기와 펄스가 한 도구에 섞여 인자 조합 설명이 복잡함 |
| 도구 1개 | `set_controls(sas, sas_mode, rcs, translate)` | 인자 조합마다 의미가 달라 설명이 가장 복잡함 |

### 5. 계획 단계 RCS 옵션의 범위 (합의)

| 대안 | 내용 | 단점 |
|---|---|---|
| **모든 단계 공통 키 (채택)** | `name`·`timeout_s`처럼 모든 단계에 `rcs` 키를 허용한다. 단계가 바뀌면 그 단계의 값으로 RCS를 켜거나 끄고, 생략한 단계는 계획 전 상태로 돌아간다(3절) | 대기 중 단계에서도 켤 수 있어 추진제를 쓸 수 있다(사용자 선택) |
| 자세 단계만 | attitude·coast·circularize에만 허용 | 모드별로 허용 여부가 달라 규칙이 늘어난다 |

### 6. SAS 모드를 바꿀 수 없을 때 (합의)

스톡 `VesselAutopilot.CanSetMode`로만 판정하고 자체 규칙을 더하지 않는다. navball 속도 표시 모드는 모드의 기준 프레임을 바꾸고, `CanSetMode`의 판정에도 영향을 준다(prograde 계열은 표시 속도로 판정하고, normal·radial의 벡터 잠금 검사는 orbit 표시에서만 한다). 모드는 stability, prograde, retrograde, normal, antinormal, radial_in, radial_out, target, anti_target, maneuver의 10개다.

| 대안 | 내용 | 단점 |
|---|---|---|
| **409 거부 + 가능 모드 표시 (채택)** | 409로 거부하고 SAS는 바꾸지 않는다. 상태 조회에 지금 고를 수 있는 모드 목록을 보여 준다 | 없음 |
| SAS만 켜고 경고 | SAS는 켜고 모드는 stability로 두며 응답에 경고 | 요청과 다른 상태가 되어 클라이언트가 응답을 매번 확인해야 한다 |

### 7. 상태 표시 (합의)

| 대안 | 내용 | 단점 |
|---|---|---|
| **get_flight 상세, get_status 요약 (채택)** | `get_flight`: SAS on·mode·가능 모드, RCS on·부품 수·MonoPropellant 잔량/최대, 병진 펄스 진행 상태. `get_status`: SAS·RCS on과 펄스 진행 여부 | 두 곳을 맞춰야 한다 |
| get_status에만 | 모든 항목을 get_status에 | 비행 씬 전용 정보가 전역 상태에 섞인다 |
| get_flight에만 | 모든 항목을 get_flight에 | 씬 확인용 get_status로는 SAS·RCS 상태를 알 수 없다 |

## 알려진 제약

- 로더(`KspBridge.Mod`)는 바꾸지 않는다(설계 0014 결정 4). 그래서 새 로직과 이전 로직이 모두 시작하지 못하는 이중 실패 때는 로더가 SAS만 복원하고 RCS는 복원하지 않는다.

## 비범위

- #7 검증용 디버그 도구
- SAS 모드를 비행 계획 단계로 쓰는 것(계획 중 SAS는 Controller가 끈다)
- RCS 부품 개별 설정(추력 제한, 축 활성화 등 PAW 항목)
- 도킹 자동화
