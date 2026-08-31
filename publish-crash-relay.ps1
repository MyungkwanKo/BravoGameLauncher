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

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\publish-crash-relay.ps1
    powershell -ExecutionPolicy Bypass -File .\publish-crash-relay.ps1 -ServiceName GWCrashRelay
#>

[CmdletBinding()]
param(
    [string] $DeployRoot  = 'D:\Build\CrashRelay',
    [string] $ServiceName = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $MyInvocation.MyCommand.Definition
$projectDir = Join-Path $repoRoot 'CrashRelay'
$publishDir = Join-Path $projectDir '_publish'

if (-not (Test-Path -LiteralPath $projectDir)) {
    throw "CrashRelay 프로젝트를 찾을 수 없습니다: $projectDir"
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
Write-Host "`n[3/4] 배포 경로로 복사..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path $DeployRoot -Force | Out-Null

$prodConfig = Join-Path $DeployRoot 'appsettings.Production.json'
$backup = $null
if (Test-Path -LiteralPath $prodConfig) {
    $backup = Get-Content -LiteralPath $prodConfig -Raw -Encoding UTF8
    Write-Host "      기존 appsettings.Production.json 보존" -ForegroundColor DarkGray
}

Copy-Item -Path (Join-Path $publishDir '*') -Destination $DeployRoot -Recurse -Force

if ($null -ne $backup) {
    Set-Content -LiteralPath $prodConfig -Value $backup -Encoding UTF8 -NoNewline
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
