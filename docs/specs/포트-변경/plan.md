# MCP·Mod 포트 변경 계획

## 요약

포트 값이 적힌 곳의 숫자를 바꾼다. MCP 서버는 8651을 65531로, Mod는 8650을 65530으로 바꾼다. 코드 구조나 설정 형식은 바꾸지 않는다. 마지막에 기능 `bridge-connection`의 기준 spec을 새로 만든다.

## 바꿀 파일·구조

- `src/Shared/Protocol.cs`: `DefaultModPort`를 65530으로, `DefaultServerPort`를 65531로 바꾼다 (C1).
- `src/KspBridge.Mod.Loader/settings.cfg`: `port`를 65530으로 바꾼다 (C2).
- `scripts/deploy.ps1`: yaml을 새로 만들 때 쓰는 `Port`를 65531로, `ModUrl`을 `http://127.0.0.1:65530`으로 바꾼다 (C3).
- `src/KspBridge.Server/AppConfiguration.cs`: `ModUrl` 주석의 예시 주소를 `http://127.0.0.1:65530`으로 바꾼다 (C4).
- `ReadMe.md`: 구성도의 두 주소, settings.cfg 설명의 "기본 8650", 클라이언트 등록 예시 주소를 새 포트로 바꾼다 (C4).
- `docs/features/bridge-connection.md`: 새 파일. 기준 spec 틀대로 만든다. `파일:` 줄에는 위 다섯 파일을 적는다. 요구에는 두 기본 포트와 각 값을 정하는 곳, 그리고 앞선 변경 폴더 `docs/specs/골격/`에서 이 기능에 해당하는 루프백 바인딩 요구를 적는다. 이력에는 이번 작업 한 줄을 적고, 표에 없는 이전 변경은 "기록 없음"으로 둔다 (C6).

## 결정

- **숫자만 바꾸고 구조는 그대로 둔다**: 사용자가 요청한 것은 값 변경이다.
  - 대안 1: `DefaultServerPort`를 지운다. 참조가 없지만 spec C1에서 값을 맞춰 두기로 정했으므로 지우지 않는다.
  - 대안 2: 서버 `Port`를 선택 값으로 바꿔 상수를 기본값으로 쓰게 한다. 설정 형식이 바뀌어 요청 범위를 넘으므로 하지 않는다.
- **배포된 설정은 고치지 않는다**: 게임 폴더의 settings.cfg와 서버 폴더의 kspbridge.yaml은 workspace 밖에 있다. 규칙상 이 작업은 한 workspace의 파일만 바꾼다. 그래서 완료 보고에서 사용자에게 안내만 한다 (C7).
  - 대안: `deploy.ps1`이 기존 설정 파일의 포트를 덮어쓰게 한다. 사용자가 직접 바꾼 값을 지워 버리므로 하지 않는다.
- **`ExitCodeTests.cs`의 `example.com:8650`은 그대로 둔다**: 루프백이 아닌 주소를 거부하는지 보는 시험 값이라 포트와 관계가 없다(spec C4의 예외).

## 오류·위험

- 65530과 65531은 Windows 동적 포트 대역(49152–65535) 안에 있다. 재부팅 뒤 Hyper-V나 WSL이 이 범위를 예약하거나, 나가는 연결이 임시 포트로 쓰고 있으면 바인딩에 실패할 수 있다. 사용자가 정한 값이므로 받아들인다. 2026-11-18 기준으로 예약 범위와 겹치지 않고 사용 중이지도 않음을 확인했다. 실패하면 `netsh int ipv4 show excludedportrange protocol=tcp`로 확인하라고 완료 보고에 함께 적는다.
- 배포된 설정을 맞추지 않으면 서버, Mod, 클라이언트의 포트가 서로 달라져 연결되지 않는다. C7 안내로 막는다.
- Mod Loader는 고정 호스트다. 새 `DefaultModPort`는 Loader를 다시 빌드·배포하고 KSP를 다시 시작해야 반영된다. settings.cfg 값이 있으면 이 상수는 쓰이지 않는다.

## 검증 전략

- C1~C3: 바꾼 줄을 읽어 값을 확인한다.
- C4: bin·obj·.git을 빼고 `865[01]`를 검색한다. spec C4 예외로 둔 곳만 남는지 확인한다.
- C5: `dotnet build KspBridge.slnx`와 `dotnet test KspBridge.slnx`를 실행한다.
- C6: 기준 spec 파일을 읽어 요구와 이력이 있는지 확인한다.
- C7: 완료 보고에 세 곳과 새 값을 적는다.
- 실제 게임과 서버를 띄워 연결되는지는 확인하지 않는다. 배포된 설정이 아직 옛 포트이고, 이를 바꾸는 일은 사용자 몫이기 때문이다. 확인하지 않았다는 사실을 보고에 적는다.

## 원칙 점검

- 적용 규칙: Bizadm `workflow/spec-driven.md`(한 workspace, 세 문서, 네 검토, 기준 spec), 전역 `language-preference.md`(대화는 한국어로 쓰고 코드·주석은 저장소 관례를 따른다. 주석은 기존에도 한국어다), 사용자 메모리 "verify-without-game-screen"(게임 화면 확인을 요구하지 않는다).
- 충돌: 없음. 배포된 설정을 고치지 않는 것은 workspace 규칙을 따른 결과다.
