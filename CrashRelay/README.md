# GW Crash Relay

GW Launcher가 보낸 크래시 로그 zip을 받아 Slack 채널에 업로드하는 사내 릴레이 서비스 (#PJTGW-3099).

Slack 봇 토큰은 **이 서비스가 도는 빌드 머신에만** 존재한다. 런처(클라이언트)에는 어떤 시크릿도 배포되지 않는다.

---

## 동작

```
GW Launcher ──(multipart POST)──> Nginx /crash-report/ ──> 127.0.0.1:5080 ──(Slack API)──> Slack 채널
```

1. 런처가 `POST /upload` 로 zip + 메타데이터 전송
2. 릴레이가 검증(크기·확장자·ZIP 매직넘버) 후 `files.getUploadURLExternal` → 바이트 업로드 → `files.completeUploadExternal`
3. 결과(JSON)를 런처에 반환

구 `files.upload` API는 2025-11-12에 sunset 되어 사용하지 않는다.

## API

### `GET /health`

```json
{ "ok": true, "service": "GWCrashRelay" }
```

### `POST /upload` (multipart/form-data)

| 필드 | 필수 | 설명 |
|------|------|------|
| `file` | O | 크래시 로그 zip |
| `message` | O | 사용자가 입력한 크래시 상황 (최대 500자로 절단) |
| `build` | | 빌드명 (최대 120자) |
| `user` | | 보고자 계정 (최대 64자) |
| `machine` | | PC 이름 (최대 64자) |

응답

```json
{ "ok": true,  "permalink": "https://...", "error": null }
{ "ok": false, "permalink": null, "error": "사유" }
```

| 상태 코드 | 의미 |
|-----------|------|
| 200 | 전송 성공 |
| 400 | 요청 형식 오류 (파일 없음/zip 아님/용량 초과/설명 누락) |
| 429 | 레이트리밋 초과 (IP당 10분에 10회) |
| 502 | Slack API 호출 실패 (토큰·스코프·채널 문제) |
| 503 | 서버에 Slack 설정이 없음 |

### `POST /issue` (application/json) — 클라 내부 크래시 리포트용, 파일 없이 Slack 메시지만 게시

클라가 자체 크래시 리포트로 Issue-key(버그 리포트 `BUG-숫자`, 크래시 리포트 `CRASH-숫자`)를 발급받은 뒤 호출한다. 서버는 `chat.postMessage`로 아래 3줄 메시지를 채널에 올린다.

Issue-key 접두사에 따라 제목이 달라지고 본문은 같다. 제목은 Block Kit `header`(큰 제목 스타일)로 게시한다.

```
:jira-bug: [GW 버그 리포트]        ← BUG-숫자
:경광등: [GW 크래시 리포트]        ← CRASH-숫자
제보자 : @mk.ko
Issue-key : BUG-2
리포트링크 : http://pjtgw.bluehole.net:8080/BUG-2
```

`:jira-bug:`, `:경광등:`은 워크스페이스에 등록된 커스텀 이모지여야 렌더링된다(없으면 `:이름:` 글자 그대로 보임).

```json
{ "reporter": "mk.ko", "issueKey": "BUG-2" }
```

| 필드 | 필수 | 설명 |
|------|------|------|
| `reporter` | O | Windows 계정(= Slack ID). `[A-Za-z0-9._-]` 최대 64자 |
| `issueKey` | O | `BUG-숫자`(버그 리포트) 또는 `CRASH-숫자`(크래시 리포트) 형식만 허용 (대소문자 무관, 대문자로 정규화). 두 유형 모두 메시지 형식은 동일하고 링크만 키에 따라 `Relay:IssueBaseUrl + 키`(예: `.../CRASH-12`)로 달라진다 |

- **멘션**: `{reporter}@{Relay:ReporterEmailDomain}` 이메일로 `users.lookupByEmail`을 호출해 Slack 사용자 ID를 찾아 `<@U…>` 멘션을 건다(성공한 조회는 메모리 캐시). 조회에 실패하면 평문 `@계정`으로 폴백하며 이 경우 멘션 알림이 가지 않을 수 있다. **봇에 `users:read.email` 스코프가 필요**하다(추가 후 앱 재설치).
- **계정 별칭 보정**: 일부 PC에서 Windows 계정의 `.`이 빠져(`s.kim` → `skim`) 전송되는 경우를 위해, 서버는 `reporter-aliases.json`의 `ReporterAliases` 테이블에서 요청 계정(대소문자 무시)을 찾아 실제 계정으로 바꿔 처리한다. 테이블에 없으면 요청 값을 그대로 쓴다. 변환은 형식 검증을 통과한 뒤 적용되고, 변환 결과도 같은 정규식으로 다시 검증한다.
  ```json
  { "ReporterAliases": { "skim": "s.kim", "mkko": "mk.ko" } }
  ```
  - 파일 위치: `D:\Build\CrashRelay\reporter-aliases.json` (없어도 됨, 수정 시 자동 재로드). 토큰이 든 `appsettings.Production.json`(읽기 전용 ACL)과 별개 파일이라 Jenkins가 갱신할 수 있다.
  - **Jenkins `GWLauncher` job 의 `REPORTER_ALIASES` 파라미터**(`DEPLOY_CRASH_RELAY` 체크 시 적용)로 추가한다. 형식 `요청계정:실제계정`을 쉼표/공백/줄바꿈으로 여러 개 입력 (예: `skim:s.kim, mkko:mk.ko`). 비우면 기존 목록 유지, 입력하면 기존 목록에 **누적 추가/덮어쓰기**, `skim:`처럼 값을 비우면 해당 별칭 **삭제**. 형식 오류는 서비스를 중지하기 전에 빌드를 실패시킨다.
- 링크는 서버가 `Relay:IssueBaseUrl + 키`로 직접 만든다(클라가 URL을 보낼 수 없음).
- **응답 `messageTs`**: 성공 시 Slack 원문 메시지 키가 내려온다(`{ "ok": true, "permalink": null, "error": null, "messageTs": "1763900000.123456" }`). 크래시 리포트 서버가 Issue-key와 함께 보관했다가 아래 `/issue/update` 호출 때 그대로 돌려준다.
- 응답/상태 코드는 `/upload`와 같다(성공 200 `{ "ok": true }`, 형식 오류 400, 레이트리밋 429 — IP당 10분에 30회, Slack 실패 502, 설정 없음 503).

```
curl -X POST http://bravo-build.omnicraftlabs.co.kr/crash-report/issue -H "Content-Type: application/json" -d "{\"reporter\":\"mk.ko\",\"issueKey\":\"BUG-2\"}"
```

### `POST /issue/update` (application/json) — 이슈 수정 시 원문 메시지의 스레드에 답글

이슈가 수정(상태 변경)되면 크래시 리포트 서버가 호출한다. 서버는 `/issue`가 올린 원문 메시지에 스레드 답글을 단다(채널은 서버 설정 채널로 고정).

```
수정자 : @mk.ko
Issue-key : BUG-2
작업상태 : 수정완료
리포트링크 : http://pjtgw.bluehole.net:8080/BUG-2
```

```json
{ "updater": "mk.ko", "issueKey": "BUG-2", "messageTs": "1763900000.123456", "status": "수정완료" }
```

| 필드 | 필수 | 설명 |
|------|------|------|
| `updater` | O | 수정자 Windows 계정 (`reporter`와 같은 규칙, 별칭 보정·멘션 동일 적용) |
| `issueKey` | O | `BUG-숫자` / `CRASH-숫자` |
| `messageTs` | O | `/issue` 응답의 `messageTs` 그대로 (`10자리.6자리` 형식만 허용) |
| `status` | O | 작업상태 문자열 (최대 40자, 줄바꿈 제거, Slack 특수문자 이스케이프). 값 종류는 제한하지 않는다 |

- **작업 완료 시 원문 이모지**: `status`가 완료 상태이면 스레드 답글에 더해 **원문에 `:수정_완료:` 리액션**을 추가한다(`reactions.add`). 완료 상태는 공백 제거·대소문자 무시로 비교하며 기본값은 `작업 완료`/`수정 완료`, `Relay:CompletedStatuses`로 바꾼다. 이모지는 `Relay:CompletedReaction`(기본 `수정_완료`). 리액션 실패(스코프·이모지 없음)는 로그만 남기고 응답은 성공이다(답글은 이미 게시됨). **봇에 `reactions:write` 스코프 추가·앱 재설치가 필요**하다.
- 응답은 `/issue`와 같은 형태이며 성공 시 `messageTs`에 **답글의 키**가 내려온다(원문 키와 다름, 보관 불필요).
- 오류: `thread_not_found`/`message_not_found`는 502로 내려오며 원문 `messageTs`가 틀렸거나 채널이 바뀐 경우다. 형식 오류는 400, 레이트리밋은 `/issue`와 같은 한도(IP당 10분에 30회).
- **채널을 변경한 경우** 변경 전 원문의 스레드에는 답글을 달 수 없다(새 채널에는 그 원문이 없음).
- 원문 존재 여부는 서버가 검증하지 않는다(Slack이 판단). 사내망 전용 전제이며 `messageTs` 형식·레이트리밋으로만 제한한다.

```
curl -X POST http://bravo-build.omnicraftlabs.co.kr/crash-report/issue/update -H "Content-Type: application/json" -d "{\"updater\":\"mk.ko\",\"issueKey\":\"BUG-2\",\"messageTs\":\"<원문 messageTs>\",\"status\":\"수정완료\"}"
```

## 설정

`appsettings.json`(기본값) + `appsettings.Production.json`(실운영 값) 조합. 환경변수 `Relay__SlackBotToken` 형태도 가능하다.

| 키 | 기본값 | 설명 |
|----|--------|------|
| `Relay:Port` | 5080 | Kestrel 리슨 포트 (localhost 전용) |
| `Relay:IssueBaseUrl` | `http://pjtgw.bluehole.net:8080/` | `/issue` 메시지의 이슈 링크 기준 URL |
| `Relay:CompletedStatuses` | `["작업 완료","수정 완료"]` | 원문에 완료 이모지를 다는 작업상태 값(공백·대소문자 무시) |
| `Relay:CompletedReaction` | `수정_완료` | 완료 시 원문에 추가할 이모지 이름(콜론 제외) |
| `Relay:ReporterEmailDomain` | `bluehole.net` | 제보자 계정 → Slack 사용자 조회용 이메일 도메인(빈 값이면 조회 생략, 평문 멘션) |
| `Relay:MaxUploadBytes` | 536870912 (512MB) | 업로드 상한 |
| `Relay:SlackBotToken` | (없음) | `xoxb-` 봇 토큰 — **커밋 금지** |
| `Relay:SlackChannelId` | (없음) | 대상 채널 ID. 클라이언트가 채널을 지정할 수 없도록 서버에 고정 |
| `Relay:ArchiveDir` | (없음) | 지정 시 업로드된 zip 사본 보관 |
| `Relay:ArchiveKeepDays` | 30 | 보관 기간 |

`appsettings.Production.sample.json`을 복사해 `appsettings.Production.json`으로 만들고 값을 채운다.
이 파일명은 `.gitignore`에 등록되어 있다.

## Slack 앱 준비

1. Slack 앱 생성 (KRAFTON은 Enterprise Grid라 조직 관리자 승인이 필요할 수 있음)
2. **Bot Token Scopes**: `files:write`, `chat:write`, `users:read.email`(멘션 조회), `reactions:write`(작업 완료 이모지)
3. 워크스페이스에 설치 후 `xoxb-` 토큰 발급
4. **대상 채널에 봇을 초대** (`/invite @앱이름`) — 초대하지 않으면 `not_in_channel` 오류

## 배포 (빌드 머신)

> 최초 설치는 **[SETUP.md](SETUP.md)** 에 단계별로 정리되어 있다. 아래는 요약이다.

```powershell
# 리포 루트에서
powershell -ExecutionPolicy Bypass -File .\publish-crash-relay.ps1 -ServiceName GWCrashRelay
```

기본 배포 경로는 `D:\Build\CrashRelay`이며, 스크립트는 기존 `appsettings.Production.json`을 **읽거나 쓰지 않고 그대로 둔다**(publish 산출물에 이 파일이 없고, 토큰 보호를 위해 ACL이 읽기 전용이라 Jenkins 계정은 쓸 수 없다). 복사 단계가 실패해도 서비스는 다시 시작한 뒤 오류로 종료한다.

**Jenkins `GWLauncher` job 으로 배포 (권장 — 릴레이만 수정한 경우 포함)**

릴레이 전용 job 은 없고, 런처 job(`GWLauncher`, `Jenkinsfile.groovy`)의 `DEPLOY_CRASH_RELAY` 파라미터로 배포한다. 런처 코드는 바꾸지 않고 **릴레이만 수정한 경우에도 이 job 으로 배포하면 된다.**

1. 릴레이 수정분을 커밋·push 한다.
2. Jenkins `GWLauncher` job → **Build with Parameters** → **`DEPLOY_CRASH_RELAY` 체크**.
3. 릴레이만 수정한 경우의 나머지 파라미터:
   - `LauncherVersionInfo.Version` 은 **올리지 않는다.**
   - `FORCE_UPDATE` 는 **해제**한다. 기본값(true)이면 `minSupportedVersion` 이 현재 버전으로 올라간다.
   - `RELEASE_NOTES` 는 **기존 `launcher.json` 의 값과 같게** 입력한다. 비워 두면 `releaseNotes` 가 빈 값으로 덮어써진다.
4. 빌드가 끝나면 `http://127.0.0.1:5080/health`(빌드 머신) 와 `/issue`·`/upload` 테스트 전송으로 확인한다.

> 이 job 은 릴레이 배포와 별개로 **런처 zip 빌드·배포와 `launcher.json` 갱신 스테이지도 항상 같이 실행**된다. 버전을 올리지 않으면 같은 버전의 zip 이 같은 이름으로 다시 올라가고 `latestVersion` 도 그대로라 사용자에게는 업데이트가 나타나지 않는다. 다만 `releaseNotes`·`minSupportedVersion` 은 위 파라미터 값으로 갱신되므로 3번을 지킨다.

**최초 설치 순서**

1. `publish-crash-relay.ps1` 을 `-ServiceName` 없이 실행해 파일만 배포
2. `appsettings.Production.json` 작성 (토큰·채널 ID)
3. 아래 NSSM 절차로 서비스 등록
4. 이후부터는 `-ServiceName GWCrashRelay` 로 실행 (배포 전후 자동 중지/시작)

publish는 **framework-dependent**다. self-contained는 런타임 팩을 NuGet에서 복원해야 해 오프라인 빌드 머신에서 실패하므로 쓰지 않는다. 빌드 머신에 .NET 10 SDK(런처 빌드용)가 있으면 ASP.NET Core 공유 프레임워크도 함께 있으므로 그대로 실행된다.

### Windows 서비스 등록 (최초 1회, NSSM)

```powershell
nssm install GWCrashRelay "D:\Build\CrashRelay\GWCrashRelay.exe"
nssm set GWCrashRelay AppDirectory "D:\Build\CrashRelay"
nssm set GWCrashRelay AppEnvironmentExtra ASPNETCORE_ENVIRONMENT=Production
nssm set GWCrashRelay Start SERVICE_AUTO_START
nssm start GWCrashRelay
```

토큰 파일은 서비스 계정과 관리자만 읽을 수 있도록 ACL을 제한한다.

```powershell
icacls "D:\Build\CrashRelay\appsettings.Production.json" /inheritance:r /grant:r "Administrators:(R)" "SYSTEM:(R)"
```

### Nginx

```nginx
location /crash-report/ {
    proxy_pass http://127.0.0.1:5080/;

    client_max_body_size 512m;
    proxy_read_timeout   300s;
    proxy_send_timeout   300s;
    proxy_request_buffering off;

    proxy_set_header Host              $host;
    proxy_set_header X-Real-IP         $remote_addr;
    proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;

    # 사내망만 허용 (실제 대역에 맞게 조정)
    # allow 10.0.0.0/8;
    # deny  all;
}
```

`proxy_request_buffering off`가 없으면 Nginx가 큰 zip을 통째로 임시 파일에 받은 뒤에야 전달해 업로드 진행률이 끝까지 튀어 오른 채 멈춘 것처럼 보인다.

### 확인

```powershell
curl http://127.0.0.1:5080/health
curl http://bravo-build.omnicraftlabs.co.kr/crash-report/health
```

## 보안 처리

- 파일명은 서버가 재생성 — 클라이언트 파일명을 그대로 쓰지 않아 경로 조작 차단
- 확장자 + ZIP 매직넘버 이중 검사
- 채널 ID를 서버에 고정 — 클라이언트가 임의 채널로 보낼 수 없음
- 문자열 필드는 제어문자 제거 + 길이 제한
- IP당 레이트리밋 (X-Forwarded-For 기준)
- Kestrel은 localhost만 리슨 — 외부 노출 경로는 Nginx location 하나뿐
