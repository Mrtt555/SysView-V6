# ─────────────────────────────────────────────────────────────────────────────
# publish.ps1  —  Build Release local de SysViewManager
#
# Usage :
#   .\publish.ps1                  # version 0.0.0 (dev)
#   .\publish.ps1 -Version 1.2.0   # version personnalisée
#   .\publish.ps1 -Open            # ouvre le dossier output après build
#   .\publish.ps1 -Kill            # arrête SysViewManager avant de compiler
# ─────────────────────────────────────────────────────────────────────────────
param(
    [string] $Version = "0.0.0",
    [switch] $Open,
    [switch] $Kill
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

# ── Arrêt du processus si demandé ou si le fichier est verrouillé ────────────
$proc = Get-Process -Name "SysViewManager" -ErrorAction SilentlyContinue
if ($proc) {
    if ($Kill) {
        Write-Host "  Arrêt de SysViewManager (PID $($proc.Id))..." -ForegroundColor Yellow
        try {
            # taskkill fonctionne même si le process est élevé, depuis un shell admin
            $tk = & taskkill /F /PID $proc.Id 2>&1
            Start-Sleep -Milliseconds 800
            if (Get-Process -Name "SysViewManager" -ErrorAction SilentlyContinue) {
                throw "Toujours en cours"
            }
            Write-Host "  Process arrêté." -ForegroundColor Gray
            Write-Host ""
        } catch {
            Write-Host "  Impossible d'arrêter SysViewManager (process élevé)." -ForegroundColor Red
            Write-Host "  → Quittez-le via l'icône dans la barre système (clic droit → Quitter)" -ForegroundColor Yellow
            Write-Host "    puis relancez : .\publish.ps1 -Version $Version" -ForegroundColor Yellow
            exit 1
        }
    } else {
        Write-Host "  ATTENTION : SysViewManager.exe est en cours d'exécution." -ForegroundColor Yellow
        Write-Host "  → Quittez-le via l'icône dans la barre système, puis relancez :" -ForegroundColor Yellow
        Write-Host "     .\publish.ps1 -Version $Version" -ForegroundColor Cyan
        Write-Host "  → Ou arrêtez-le automatiquement avec -Kill (nécessite droits admin)" -ForegroundColor Yellow
        Write-Host ""
        exit 1
    }
}

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

# ── Vérification du résultat réel (exit code dotnet) ─────────────────────────
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "  ERREUR : dotnet publish a échoué (exit $LASTEXITCODE)." -ForegroundColor Red
    Write-Host "  Si SysViewManager.exe est en cours, utilisez : .\publish.ps1 -Version $Version -Kill" -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path $exe)) {
    Write-Host ""
    Write-Host "  ERREUR : SysViewManager.exe introuvable après publish." -ForegroundColor Red
    exit 1
}

$sizeMB = [math]::Round((Get-Item $exe).Length / 1MB, 1)

Write-Host ""
Write-Host "  OK  SysViewManager.exe  —  $sizeMB MB  ($([math]::Round($sw.Elapsed.TotalSeconds,1)) s)" -ForegroundColor Green
Write-Host ""

if ($Open) { Start-Process explorer.exe $outDir }
