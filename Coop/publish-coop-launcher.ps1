# 협업부서용 CoopLauncher 단일 파일 publish (GW Jenkins Publish 단계와 동일한 옵션)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $PSScriptRoot "CoopLauncher\CoopLauncher.csproj"
$out = Join-Path $PSScriptRoot "_publish\CoopLauncher"

if (-not (Test-Path $csproj)) { throw "csproj not found: $csproj" }
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out -Force | Out-Null

dotnet publish $csproj -c Release -r win-x64 `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  --self-contained false `
  -o $out

Write-Host "Output: $out"
