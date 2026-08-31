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

## 설정

`appsettings.json`(기본값) + `appsettings.Production.json`(실운영 값) 조합. 환경변수 `Relay__SlackBotToken` 형태도 가능하다.

| 키 | 기본값 | 설명 |
|----|--------|------|
| `Relay:Port` | 5080 | Kestrel 리슨 포트 (localhost 전용) |
| `Relay:MaxUploadBytes` | 536870912 (512MB) | 업로드 상한 |
| `Relay:SlackBotToken` | (없음) | `xoxb-` 봇 토큰 — **커밋 금지** |
| `Relay:SlackChannelId` | (없음) | 대상 채널 ID. 클라이언트가 채널을 지정할 수 없도록 서버에 고정 |
| `Relay:ArchiveDir` | (없음) | 지정 시 업로드된 zip 사본 보관 |
| `Relay:ArchiveKeepDays` | 30 | 보관 기간 |

`appsettings.Production.sample.json`을 복사해 `appsettings.Production.json`으로 만들고 값을 채운다.
이 파일명은 `.gitignore`에 등록되어 있다.

## Slack 앱 준비

1. Slack 앱 생성 (KRAFTON은 Enterprise Grid라 조직 관리자 승인이 필요할 수 있음)
2. **Bot Token Scopes**: `files:write`, `chat:write`
3. 워크스페이스에 설치 후 `xoxb-` 토큰 발급
4. **대상 채널에 봇을 초대** (`/invite @앱이름`) — 초대하지 않으면 `not_in_channel` 오류

## 배포 (빌드 머신)

> 최초 설치는 **[SETUP.md](SETUP.md)** 에 단계별로 정리되어 있다. 아래는 요약이다.

```powershell
# 리포 루트에서
powershell -ExecutionPolicy Bypass -File .\publish-crash-relay.ps1 -ServiceName GWCrashRelay
```

기본 배포 경로는 `D:\Build\CrashRelay`이며, 스크립트는 기존 `appsettings.Production.json`을 보존한다.

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
