# ─────────────────────────────────────────────────────────────────────────────
# publish.ps1  —  Build Release local de SysViewManager
#
# Usage :
#   .\publish.ps1                  # version 0.0.0 (dev)
#   .\publish.ps1 -Version 1.2.0   # version personnalisée
#   .\publish.ps1 -Open            # ouvre le dossier output après build
# ─────────────────────────────────────────────────────────────────────────────
param(
    [string] $Version = "0.0.0",
    [switch] $Open
)

$ErrorActionPreference = "Stop"

$proj   = "$PSScriptRoot\SysViewManager\SysViewManager.csproj"
$outDir = "$PSScriptRoot\SysViewManager\bin\Release\net8.0-windows10.0.17763.0\win-x64\publish"
$exe    = "$outDir\SysViewManager.exe"

Write-Host ""
Write-Host "  SysView V6 — publish Release" -ForegroundColor Cyan
Write-Host "  Version   : $Version" -ForegroundColor Gray
Write-Host "  Sortie    : $outDir" -ForegroundColor Gray
Write-Host ""

$sw = [System.Diagnostics.Stopwatch]::StartNew()

dotnet publish $proj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:AssemblyVersion=$Version `
    --nologo

$sw.Stop()

if (-not (Test-Path $exe)) {
    Write-Host ""
    Write-Host "  ERREUR : SysViewManager.exe introuvable." -ForegroundColor Red
    exit 1
}

$sizeMB = [math]::Round((Get-Item $exe).Length / 1MB, 1)

Write-Host ""
Write-Host "  OK  SysViewManager.exe  —  $sizeMB MB  ($([math]::Round($sw.Elapsed.TotalSeconds,1)) s)" -ForegroundColor Green
Write-Host ""

if ($Open) { Start-Process explorer.exe $outDir }
