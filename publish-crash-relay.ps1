<#
.SYNOPSIS
    GW Crash Relay(CrashRelay) 를 빌드 머신에 배포한다. (#PJTGW-3099)

.DESCRIPTION
    dotnet publish 로 self-contained 단일 폴더를 만들고 배포 경로에 복사한다.
    실운영 설정(appsettings.Production.json)은 배포 경로에 이미 있는 파일을 유지하며 덮어쓰지 않는다.

.PARAMETER DeployRoot
    배포 대상 폴더. 기본값 D:\Build\CrashRelay

.PARAMETER ServiceName
    NSSM 등으로 등록해 둔 Windows 서비스 이름. 지정하면 배포 전후로 중지/시작한다.

.PARAMETER ReporterAliases
    제보자 계정 별칭 보정 목록. "요청계정:실제계정" 쌍을 쉼표/공백/줄바꿈으로 구분해 여러 개 입력한다.
    예) "skim:s.kim, mkko:mk.ko"  (일부 PC에서 계정의 '.'이 빠져 오는 경우 보정용)
    - 비워 두면 기존 reporter-aliases.json 을 그대로 유지한다.
    - 입력하면 기존 목록에 추가/덮어쓴다(누적). "skim:" 처럼 값을 비우면 해당 별칭을 삭제한다.
    - 결과는 <DeployRoot>\reporter-aliases.json 에 저장되며 릴레이가 읽는다.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\publish-crash-relay.ps1
    powershell -ExecutionPolicy Bypass -File .\publish-crash-relay.ps1 -ServiceName GWCrashRelay
#>

[CmdletBinding()]
param(
    [string] $DeployRoot  = 'D:\Build\CrashRelay',
    [string] $ServiceName = '',
    [string] $ReporterAliases = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $MyInvocation.MyCommand.Definition
$projectDir = Join-Path $repoRoot 'CrashRelay'
$publishDir = Join-Path $projectDir '_publish'

if (-not (Test-Path -LiteralPath $projectDir)) {
    throw "CrashRelay 프로젝트를 찾을 수 없습니다: $projectDir"
}

# 별칭 입력은 서비스를 건드리기 전에 먼저 검증한다(오타 때문에 배포가 중간에 끊기지 않도록).
# 계정 문자 규칙은 릴레이의 reporter 검증(^[A-Za-z0-9._-]{1,64}$)과 같다.
$aliasUpdates = [ordered]@{}
if (-not [string]::IsNullOrWhiteSpace($ReporterAliases)) {
    foreach ($pair in ($ReporterAliases -split '[,;\s]+' | Where-Object { $_ })) {
        $idx = $pair.IndexOf(':')
        if ($idx -lt 1) { throw "REPORTER_ALIASES 형식 오류: '$pair' (예: skim:s.kim)" }
        $from = $pair.Substring(0, $idx).Trim().ToLowerInvariant()
        $to   = $pair.Substring($idx + 1).Trim()
        if ($from -notmatch '^[A-Za-z0-9._-]{1,64}$') { throw "REPORTER_ALIASES 요청 계정 형식 오류: '$from'" }
        if ($to -ne '' -and $to -notmatch '^[A-Za-z0-9._-]{1,64}$') { throw "REPORTER_ALIASES 실제 계정 형식 오류: '$to'" }
        $aliasUpdates[$from] = $to
    }
}

Write-Host "== GW Crash Relay 배포 ==" -ForegroundColor Cyan
Write-Host "프로젝트 : $projectDir"
Write-Host "배포 경로 : $DeployRoot"

# 1) publish
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

# framework-dependent 로 publish 한다.
# self-contained 는 런타임 팩을 NuGet에서 복원해야 해서 오프라인 빌드 머신에서 실패한다.
# 빌드 머신에는 .NET 10 SDK가 있으므로(런처를 이걸로 빌드한다) ASP.NET Core 공유 프레임워크도 이미 있다.
Write-Host "`n[1/4] dotnet publish..." -ForegroundColor Yellow
& dotnet publish (Join-Path $projectDir 'CrashRelay.csproj') `
    -c Release `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 실패 (exit $LASTEXITCODE)" }

# 2) 서비스 중지
if ($ServiceName) {
    Write-Host "`n[2/4] 서비스 중지: $ServiceName" -ForegroundColor Yellow
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        $svc.WaitForStatus('Stopped', '00:00:30')
    }
} else {
    Write-Host "`n[2/4] 서비스 이름 미지정 - 중지 단계 건너뜀" -ForegroundColor DarkGray
}

# 3) 복사 (실운영 설정은 보존)
# 복사 단계에서 실패해도 서비스는 반드시 다시 올린다(릴레이가 죽은 채로 빌드가 끝나는 것을 막는다).
Write-Host "`n[3/4] 배포 경로로 복사..." -ForegroundColor Yellow
try {
    New-Item -ItemType Directory -Path $DeployRoot -Force | Out-Null

    # appsettings.Production.json 은 토큰이 들어 있어 ACL을 읽기 전용(Administrators/SYSTEM (R))으로 잠가 둔다.
    # 그래서 이 스크립트를 실행하는 계정(Jenkins)은 읽을 수만 있고 쓸 수 없다.
    # publish 산출물에는 이 파일이 없으므로 Copy-Item 은 건드리지 않는다 -> 백업/재기록은 하지 않는다.
    $prodConfig = Join-Path $DeployRoot 'appsettings.Production.json'
    $hadProdConfig = Test-Path -LiteralPath $prodConfig
    if ($hadProdConfig) {
        Write-Host "      기존 appsettings.Production.json 유지 (덮어쓰지 않음)" -ForegroundColor DarkGray
    } else {
        Write-Warning "배포 경로에 appsettings.Production.json 이 없습니다. Slack 토큰/채널 설정이 없으면 서비스가 503을 반환합니다."
    }

    Copy-Item -Path (Join-Path $publishDir '*') -Destination $DeployRoot -Recurse -Force

    # 계정 별칭(reporter-aliases.json) 갱신: 입력이 있을 때만, 기존 목록에 누적 반영한다.
    # 이 파일은 publish 산출물에 없으므로 배포로 지워지지 않고, ACL 잠금 대상(Production.json)도 아니다.
    if ($aliasUpdates.Count -gt 0) {
        $aliasFile = Join-Path $DeployRoot 'reporter-aliases.json'
        $merged = [ordered]@{}
        if (Test-Path -LiteralPath $aliasFile) {
            $existing = Get-Content -LiteralPath $aliasFile -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($existing.ReporterAliases) {
                foreach ($p in $existing.ReporterAliases.PSObject.Properties) { $merged[$p.Name.ToLowerInvariant()] = [string]$p.Value }
            }
        }
        foreach ($k in $aliasUpdates.Keys) {
            if ($aliasUpdates[$k] -eq '') { $merged.Remove($k) } else { $merged[$k] = $aliasUpdates[$k] }
        }
        $tmp = "$aliasFile.tmp"
        (@{ ReporterAliases = $merged } | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath $tmp -Encoding UTF8
        Move-Item -LiteralPath $tmp -Destination $aliasFile -Force
        Write-Host "      reporter-aliases.json 갱신 ($($merged.Count)건)" -ForegroundColor DarkGray
        foreach ($k in $merged.Keys) { Write-Host "        $k -> $($merged[$k])" -ForegroundColor DarkGray }
    }

    # 방어: 복사 후에도 파일이 그대로 있는지만 확인한다.
    if ($hadProdConfig -and -not (Test-Path -LiteralPath $prodConfig)) {
        throw "복사 후 appsettings.Production.json 이 사라졌습니다. 배포 경로를 확인하세요: $prodConfig"
    }
}
catch {
    Write-Host "`n[ERROR] 복사 단계 실패: $($_.Exception.Message)" -ForegroundColor Red
    if ($ServiceName -and (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
        Write-Host "      서비스를 다시 시작합니다: $ServiceName" -ForegroundColor Yellow
        try { Start-Service -Name $ServiceName } catch { Write-Warning "서비스 재시작 실패: $($_.Exception.Message)" }
    }
    throw
}

# 4) 서비스 시작
if ($ServiceName) {
    # 최초 배포 시점에는 아직 NSSM 등록 전일 수 있으므로 없으면 안내만 하고 넘어간다.
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Write-Host "`n[4/4] 서비스 시작: $ServiceName" -ForegroundColor Yellow
        Start-Service -Name $ServiceName
    } else {
        Write-Host "`n[4/4] 서비스 '$ServiceName' 미등록 - 시작 건너뜀" -ForegroundColor Yellow
        Write-Host "      CrashRelay/README.md 의 NSSM 등록 절차를 먼저 수행하세요." -ForegroundColor DarkGray
    }
} else {
    Write-Host "`n[4/4] 서비스 이름 미지정 - 시작 단계 건너뜀" -ForegroundColor DarkGray
}

Write-Host "`n배포 완료: $DeployRoot" -ForegroundColor Green
Write-Host "헬스체크: curl http://127.0.0.1:5080/health" -ForegroundColor DarkGray
