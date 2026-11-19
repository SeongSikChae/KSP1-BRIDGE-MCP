# bridge-connection

파일: src/Shared/Protocol.cs, src/KspBridge.Mod.Loader/settings.cfg, scripts/deploy.ps1, src/KspBridge.Server/AppConfiguration.cs, ReadMe.md

## 요구(완료 조건)

- R1. KSP Mod(Loader)의 기본 HTTP 포트는 65530이다. 실제 포트는 배포되는 `GameData/KspBridge/settings.cfg`의 `port` 값(저장소 원본 `src/KspBridge.Mod.Loader/settings.cfg`)이 정하고, 그 파일을 읽지 못하거나 값이 없을 때만 `Protocol.DefaultModPort`(65530)를 쓴다.
- R2. MCP 서버의 기본 HTTP 포트는 65531이다. 서버 포트는 `kspbridge.yaml`의 필수 값 `Port`가 정하며, `scripts/deploy.ps1`이 `kspbridge.yaml`을 처음 만들 때 `Port: 65531`, `ModUrl: http://127.0.0.1:65530`을 쓴다. 이미 있는 `kspbridge.yaml`은 덮어쓰지 않는다. `Protocol.DefaultServerPort`(65531)는 값을 맞춰 두려고 둔 상수이며 동작에 쓰이지 않는다.
- R3. 서버와 Mod는 루프백 주소(`127.0.0.1`)에만 바인딩한다. 서버는 루프백이 아닌 `ModUrl`을 설정 오류로 거부한다. 인증 없이 기체를 조종할 수 있는 엔드포인트를 바깥에 노출하지 않기 위해서다.
- R4. MCP 클라이언트는 `http://127.0.0.1:<서버 Port>/mcp`로 등록한다(기본 `http://127.0.0.1:65531/mcp`). `ReadMe.md`의 구성도와 등록 예시는 기본 포트와 같은 값을 보인다.

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 | 작업 문서 |
|---|---|---|---|---|
| 2026-11-19 | 포트-변경 | R1·R2·R3·R4 추가(기준 spec 처음 만듦, 기본 포트를 Mod 65530·서버 65531로 바꿈) | docs/specs/포트-변경/ | 2026-11-18-포트-변경.md |

표에 없는 이전 변경: 기록 없음
