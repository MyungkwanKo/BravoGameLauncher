# GW Crash Relay 빌드 머신 세팅 (단계별)

빌드 머신(Jenkins `master` 노드, Nginx로 `bravo-build.omnicraftlabs.co.kr` 서비스 중)에
크래시 로그 릴레이를 처음 올리는 절차. (#PJTGW-3099)

전체 흐름은 이렇다.

```
GW Launcher ──POST──> Nginx :80 /crash-report/ ──proxy──> GWCrashRelay 127.0.0.1:5080 ──> Slack
```

---

## STEP 0. 사전 확인

빌드 머신에 **관리자 권한**으로 접속한 뒤 확인한다.

```powershell
# .NET SDK (런처를 이걸로 빌드하므로 이미 있어야 정상)
dotnet --list-sdks

# ASP.NET Core 런타임 포함 여부 확인
dotnet --list-runtimes | Select-String "Microsoft.AspNetCore.App"
```

`Microsoft.AspNetCore.App 10.x` 가 보이면 된다. 없으면 [ASP.NET Core Runtime 10](https://dotnet.microsoft.com/download) 설치가 필요하다.

Slack 봇 토큰(`xoxb-`)도 이 시점에 준비돼 있어야 한다. 없으면 STEP 3까지 진행하고 멈춰도 된다.

---

## STEP 1. 소스 가져오기

이미 Jenkins 워크스페이스에 리포가 있으면 그걸 써도 되고, 별도로 클론해도 된다.

```powershell
cd D:\
git clone http://bravo-repo.omnicraftlabs.co.kr/bravounit/jenkins/bravogamelauncher.git
cd D:\bravogamelauncher
```

---

## STEP 2. 릴레이 최초 배포 (서비스 이름 없이)

아직 Windows 서비스가 없으므로 `-ServiceName` 을 주지 않는다.

```powershell
powershell -ExecutionPolicy Bypass -File .\publish-crash-relay.ps1
```

결과 확인 — `D:\Build\CrashRelay\GWCrashRelay.exe` 와 `appsettings.json` 이 생겨 있어야 한다.

```powershell
dir D:\Build\CrashRelay
```

---

## STEP 3. 설정 파일 작성

`appsettings.Production.json` 은 **기본 설정(`appsettings.json`)을 덮어쓰는 파일**이다.
바꿀 값만 넣으면 되고, 나머지(`Port`, `MaxUploadBytes` 등)는 기본값이 그대로 적용된다.

### 3-1. 샘플 복사

```powershell
Copy-Item D:\Build\CrashRelay\appsettings.Production.sample.json `
          D:\Build\CrashRelay\appsettings.Production.json
```

배포 산출물에 샘플이 포함되지 않도록 csproj에서 제외해 두었으므로, 샘플이 없으면 리포에서 직접 복사한다.

```powershell
Copy-Item D:\bravogamelauncher\CrashRelay\appsettings.Production.sample.json `
          D:\Build\CrashRelay\appsettings.Production.json
```

### 3-2. 내용 작성

메모장 등으로 열어 **아래 내용만** 남긴다. 샘플의 `_comment` 블록은 설명용이므로 지운다.

```json
{
  "Relay": {
    "SlackBotToken": "xoxb-여기에-실제-봇-토큰",
    "SlackChannelId": "C09ET0RBBBJ",
    "ArchiveDir": "D:\\Build\\CrashReports"
  }
}
```

| 키 | 필수 | 설명 |
|----|------|------|
| `SlackBotToken` | O | Slack 앱에서 발급한 `xoxb-` 로 시작하는 봇 토큰 |
| `SlackChannelId` | O | 전송 대상 채널 ID. 테스트 채널은 `C09ET0RBBBJ` |
| `ArchiveDir` | | 업로드된 zip 사본 보관 경로. **필요 없으면 이 줄 통째로 삭제** |

주의할 점

- **JSON이라 백슬래시는 `\\` 로 두 번** 쓴다. `D:\Build\...` 가 아니라 `D:\\Build\\...`
- 파일 인코딩은 **UTF-8**. 메모장이면 "다른 이름으로 저장 → 인코딩: UTF-8"
- 마지막 항목 뒤에 쉼표를 남기면 JSON 파싱 에러가 난다
- 이 파일은 `.gitignore` 에 등록되어 있어 커밋되지 않는다. **절대 커밋하지 말 것**

### 3-3. 문법 검사

```powershell
Get-Content D:\Build\CrashRelay\appsettings.Production.json -Raw | ConvertFrom-Json
```

오류 없이 객체가 출력되면 정상이다.

### 3-4. (대안) 환경변수로 주기

파일 대신 환경변수를 쓰려면 아래처럼 한다. 파일보다 우선 적용된다.

```powershell
[Environment]::SetEnvironmentVariable("Relay__SlackBotToken",  "xoxb-...",     "Machine")
[Environment]::SetEnvironmentVariable("Relay__SlackChannelId", "C09ET0RBBBJ", "Machine")
```

구분자는 점(`.`)이 아니라 **밑줄 두 개(`__`)** 다.

---

## STEP 4. 수동 실행으로 동작 확인

서비스로 등록하기 전에 콘솔에서 직접 띄워 본다. 문제가 있으면 여기서 바로 로그가 보인다.

```powershell
cd D:\Build\CrashRelay
$env:ASPNETCORE_ENVIRONMENT = "Production"
.\GWCrashRelay.exe
```

`Now listening on: http://127.0.0.1:5080` 이 뜨면 정상이다.

이 창은 **켜 둔 채로 놔둔다.** 서버가 여기서 계속 돌고 있어야 한다.

이제 **다른 PowerShell 창**을 열어 헬스체크한다. 이 두 번째 창에서는 `curl` 만 치고,
`GWCrashRelay.exe` 를 다시 실행하지 않는다. 다시 실행하면 첫 창이 이미 5080을 쓰고 있어
`address already in use` 로 죽는다(서버가 정상 동작 중이라는 뜻이므로 당황할 필요 없다).

```powershell
curl.exe http://127.0.0.1:5080/health
# {"ok":true,"service":"GWCrashRelay"}
```

토큰까지 확인하려면 실제 업로드를 한 번 해 본다.

```powershell
# 아무 zip이나 하나 만들어 테스트
Compress-Archive -Path D:\Build\CrashRelay\appsettings.json -DestinationPath $env:TEMP\test.zip -Force

curl.exe -X POST http://127.0.0.1:5080/upload `
  -F "file=@$env:TEMP\test.zip" `
  -F "message=릴레이 세팅 테스트" `
  -F "build=SETUP_TEST" `
  -F "user=$env:USERNAME" `
  -F "machine=$env:COMPUTERNAME"
```

`{"ok":true,"permalink":"https://..."}` 가 나오고 Slack 채널에 파일이 올라오면 성공이다.

실패 시 응답의 `error` 값을 보고 아래 표를 참고한다.

| error | 원인 | 조치 |
|-------|------|------|
| `not_in_channel` | 봇이 채널에 없음 | 채널에서 `/invite @앱이름` |
| `channel_not_found` | 채널 ID 오류 또는 접근 불가 | `SlackChannelId` 확인 |
| `invalid_auth` / `not_authed` | 토큰이 잘못됨/비었음 | `appsettings.Production.json` 확인 |
| `missing_scope` | 스코프 부족 | 앱에 `files:write`, `chat:write` 추가 후 재설치 |
| `서버에 Slack 설정이 없습니다` | 설정 파일을 못 읽음 | 파일 위치·인코딩·`ASPNETCORE_ENVIRONMENT` 확인 |

확인이 끝나면 `Ctrl+C` 로 종료한다.

---

## STEP 5. 토큰 파일 권한 제한

동작 확인이 끝난 뒤에 한다. **먼저 걸면 STEP 4의 콘솔 실행이 `UnauthorizedAccessException` 으로 죽는다.**

일반 사용자가 토큰을 읽지 못하게 상속을 끊고 관리자·SYSTEM만 남긴다.

```powershell
icacls "D:\Build\CrashRelay\appsettings.Production.json" /inheritance:r /grant:r "Administrators:(R)" "SYSTEM:(R)"
```

확인.

```powershell
icacls "D:\Build\CrashRelay\appsettings.Production.json"
```

주의할 점

- 이 뒤로 **콘솔에서 수동 실행하려면 반드시 관리자 권한 PowerShell** 이어야 한다(`Start-Process powershell -Verb RunAs`). 일반 창에서는 설정 파일을 못 읽고 죽는다
- 서비스로 돌릴 때는 SYSTEM 계정이라 문제없다
- 서비스를 SYSTEM 이 아닌 별도 계정으로 돌릴 계획이면 그 계정에도 `(R)` 을 추가해야 한다

  ```powershell
  icacls "D:\Build\CrashRelay\appsettings.Production.json" /grant "DOMAIN\서비스계정:(R)"
  ```

---

## STEP 6. Windows 서비스 등록

### 방법 A. NSSM (권장)

[nssm.cc](https://nssm.cc/download) 에서 받아 `nssm.exe` 를 `C:\Tools\nssm\` 등에 둔다.

```powershell
cd C:\Tools\nssm

# 1) 서비스 생성
.\nssm.exe install GWCrashRelay "D:\Build\CrashRelay\GWCrashRelay.exe"

# 2) 작업 디렉터리 — 설정 파일을 찾으려면 반드시 필요
.\nssm.exe set GWCrashRelay AppDirectory "D:\Build\CrashRelay"

# 3) 환경변수
.\nssm.exe set GWCrashRelay AppEnvironmentExtra ASPNETCORE_ENVIRONMENT=Production

# 4) 자동 시작
.\nssm.exe set GWCrashRelay Start SERVICE_AUTO_START

# 5) 로그 파일 (선택이지만 강력 권장)
New-Item -ItemType Directory -Path D:\Build\CrashRelay\logs -Force
.\nssm.exe set GWCrashRelay AppStdout "D:\Build\CrashRelay\logs\stdout.log"
.\nssm.exe set GWCrashRelay AppStderr "D:\Build\CrashRelay\logs\stderr.log"
.\nssm.exe set GWCrashRelay AppRotateFiles 1
.\nssm.exe set GWCrashRelay AppRotateBytes 10485760

# 6) 시작
.\nssm.exe start GWCrashRelay
```

상태 확인.

```powershell
Get-Service GWCrashRelay
curl.exe http://127.0.0.1:5080/health
```

### 방법 B. 작업 스케줄러 (NSSM 반입이 어려울 때)

```powershell
$action  = New-ScheduledTaskAction -Execute "D:\Build\CrashRelay\GWCrashRelay.exe" -WorkingDirectory "D:\Build\CrashRelay"
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -RunLevel Highest
$settings  = New-ScheduledTaskSettingsSet -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit 0

Register-ScheduledTask -TaskName "GWCrashRelay" -Action $action -Trigger $trigger -Principal $principal -Settings $settings
Start-ScheduledTask -TaskName "GWCrashRelay"
```

이 방식은 `publish-crash-relay.ps1` 의 `-ServiceName` 자동 중지/시작을 쓸 수 없으므로,
배포 때마다 `Stop-ScheduledTask` / `Start-ScheduledTask` 를 수동으로 해야 한다.

---

## STEP 7. Nginx 설정

설정 파일은 `C:\nginx\conf\nginx.conf` 이고, nginx는 자기 디렉터리 기준으로 동작하므로
**`C:\nginx` 안에서 실행**해야 한다(PATH에 등록되어 있지 않다).

```powershell
cd C:\nginx
.\nginx.exe -t
```

`server_name bravo-build.omnicraftlabs.co.kr;` 인 server 블록에서 **`location /launcher/` 다음**에 아래를 추가한다.

```nginx
        # ── 크래시 로그 릴레이 (#PJTGW-3099) ─────────
        location ^~ /crash-report/ {
            proxy_pass http://127.0.0.1:5080/;

            client_max_body_size 512m;
            proxy_read_timeout   300s;
            proxy_send_timeout   300s;
            proxy_request_buffering off;

            proxy_set_header Host              $host;
            proxy_set_header X-Real-IP         $remote_addr;
            proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;

            # 사내망만 허용하려면 (실제 대역에 맞게 조정)
            # allow 10.0.0.0/8;
            # deny  all;
        }
```

주의할 점

- `proxy_pass` 끝의 **슬래시(`/`)를 빠뜨리면 안 된다.** 없으면 `/crash-report/upload` 가 릴레이에 그대로 전달돼 404가 난다. 슬래시가 있어야 `/upload` 로 변환된다
- **`^~` 를 붙인다.** 같은 server 블록에 `location ~* \.(zip|apk|exe|msi)$` 정규식 로케이션이 있는데, nginx는 정규식 로케이션을 일반 prefix 로케이션보다 우선 적용한다. `^~` 는 정규식 검사를 건너뛰게 해 이 간섭을 원천 차단한다
- `client_max_body_size` 는 이 server 블록이 이미 `10g` 로 크게 잡혀 있지만, 릴레이 상한(512MB)과 맞추기 위해 location 단위로 다시 지정한다
- `proxy_request_buffering off` 가 없으면 Nginx가 zip을 통째로 받은 뒤에야 전달해, 런처 진행률이 100%에서 멈춘 것처럼 보인다

적용. 이 머신의 nginx는 **`Nginx` 라는 이름의 Windows 서비스**로 떠 있어서
`nginx -s reload` 는 계정이 달라 실패한다(`Access is denied`). **서비스를 재시작해야 한다.**

```powershell
cd C:\nginx
.\nginx.exe -t          # 문법 검사 (이건 그냥 실행하면 된다)

Restart-Service Nginx   # 반영
```

---

## STEP 8. 종단 확인

### 8-1. 헬스체크 (빌드 머신 + 개발자 PC 양쪽)

```powershell
curl.exe http://bravo-build.omnicraftlabs.co.kr/crash-report/health
# {"ok":true,"service":"GWCrashRelay"}
```

### 8-2. 기존 경로가 깨지지 않았는지 확인

```powershell
curl.exe -I http://bravo-build.omnicraftlabs.co.kr/launcher/launcher.json
# HTTP/1.1 200 OK
```

### 8-3. Nginx 를 통과하는 실제 업로드

health 는 GET 이라 `client_max_body_size` / `proxy_request_buffering` 을 건드리지 않는다.
POST 로 한 번 통과시켜 봐야 프록시 설정까지 검증된다.

```powershell
curl.exe -X POST http://bravo-build.omnicraftlabs.co.kr/crash-report/upload `
  -F "file=@$env:TEMP\test.zip" `
  -F "message=Nginx 경유 종단 테스트" `
  -F "build=SETUP_TEST_VIA_NGINX" `
  -F "user=$env:USERNAME" `
  -F "machine=$env:COMPUTERNAME"
```

여기까지 성공하면 서버 세팅 완료다. 이제 런처 v30 을 배포하면 된다.

---

## 설정 변경 (토큰·채널 교체 등)

STEP 5에서 상속을 끊고 **읽기 권한만** 남겼기 때문에, 관리자 권한이어도 그냥은 저장되지 않는다.
잠깐 쓰기 권한을 주고 고친 뒤 되돌린다. **관리자 PowerShell**에서 실행한다.

```powershell
$f = "D:\Build\CrashRelay\appsettings.Production.json"

# 1) 관리자에게 전체 권한 임시 부여
icacls $f /grant "Administrators:(F)"

# 2) 수정 후 저장 (인코딩 UTF-8 유지)
notepad $f

# 3) 다시 읽기 전용으로 복구
icacls $f /grant:r "Administrators:(R)"
icacls $f
```

`/grant:r` 은 기존 항목을 **대체**하는 옵션이라 `(F)` 가 `(R)` 로 정확히 바뀐다. SYSTEM 항목은 그대로 유지된다.

변경 후에는 서비스를 재시작해야 반영된다.

```powershell
Restart-Service GWCrashRelay
curl.exe http://127.0.0.1:5080/health
```

> 채널만 바꾸는 경우 **런처는 재배포하지 않아도 된다.** 릴레이가 채널을 결정하기 때문이다.
> 다만 전송 실패 시 폴백으로 여는 딥링크 채널은 런처 상수(`CrashLogReporter.SlackChannelId` /
> `SlackChannelLink`)에 있으므로, 폴백 경로까지 맞추려면 런처도 함께 갱신해야 한다.

---

## 이후 업데이트 배포

릴레이 코드가 바뀌었을 때만 하면 된다. `appsettings.Production.json` 은 스크립트가 보존한다.

```powershell
cd D:\bravogamelauncher
git pull
powershell -ExecutionPolicy Bypass -File .\publish-crash-relay.ps1 -ServiceName GWCrashRelay
```

Jenkins 에서 돌리려면 런처 파이프라인 실행 시 **`DEPLOY_CRASH_RELAY` 파라미터를 체크**한다.

---

## 문제 해결

| 증상 | 확인할 것 |
|------|-----------|
| 서비스가 바로 죽음 | `logs\stderr.log`, 포트 5080 충돌(`netstat -ano \| findstr 5080`) |
| `address already in use` (5080) | 이미 다른 인스턴스가 떠 있다. 콘솔 테스트 창이 열려 있거나 서비스가 실행 중인 경우다. `netstat -ano \| findstr 5080` 으로 PID 확인 후 정리 |
| `/health` 는 되는데 업로드가 502 | 토큰/스코프/봇 채널 초대 — STEP 4의 error 표 참고 |
| 콘솔 실행 시 `UnauthorizedAccessException: appsettings.Production.json` | STEP 5의 ACL 때문. 관리자 권한 PowerShell로 실행 |
| 업로드가 413 | Nginx `client_max_body_size` |
| 업로드가 404 | Nginx `proxy_pass` 끝 슬래시 |
| `nginx -s reload` 가 `OpenEvent(...) failed (5: Access is denied)` | 실행 중인 nginx 마스터가 다른 계정(서비스/SYSTEM)으로 떠 있어 리로드 신호를 못 보낸 것. **설정이 반영되지 않았다.** 서비스면 `Restart-Service <이름>`, 프로세스뿐이면 `Stop-Process -Name nginx -Force` 후 `Start-Process C:\nginx\nginx.exe -WorkingDirectory C:\nginx` |
| 설정을 고쳤는데 `/crash-report/health` 가 301을 반환 | 리로드가 안 됐다는 뜻. 위 항목대로 재시작 |
| 진행률이 100%에서 멈춘 뒤 한참 뒤 완료 | `proxy_request_buffering off` 누락 |
| 429 응답 | 레이트리밋(IP당 10분 10회). 정상 동작 |
| 설정을 바꿨는데 반영 안 됨 | 서비스 재시작 필요 (`nssm restart GWCrashRelay`) |

로그 위치

```
D:\Build\CrashRelay\logs\stdout.log   # 업로드 성공 기록 (파일명/크기/보고자/PC)
D:\Build\CrashRelay\logs\stderr.log   # 오류
```

---

## 제거

```powershell
nssm stop GWCrashRelay
nssm remove GWCrashRelay confirm
Remove-Item D:\Build\CrashRelay -Recurse -Force
# Nginx 의 location /crash-report/ 블록 삭제 후 nginx -s reload
```
