# =============================================================
# push-installer.ps1 — Pousse installer/ vers la branche
# GitHub "Installeur" SANS changer de branche dans le dossier
# de travail principal.
#
# Usage :
#   .\push-installer.ps1
#   .\push-installer.ps1 -Message "fix: correction setup.iss"
# =============================================================
param(
    [string] $Message = ""
)

$ErrorActionPreference = "Stop"
$root   = (Resolve-Path "$PSScriptRoot\..")
$tmpDir = "$env:TEMP\sysview-installer-wt"

if (-not $Message) {
    $ts      = Get-Date -Format "yyyy-MM-dd HH:mm"
    $Message = "chore: sync installer [$ts]"
}

Write-Host ""
Write-Host "  SysView V6 — push branch Installeur" -ForegroundColor Cyan
Write-Host "  Message : $Message" -ForegroundColor Gray
Write-Host ""

if (Test-Path $tmpDir) {
    git -C $root worktree remove --force $tmpDir 2>$null
    Remove-Item -Recurse -Force $tmpDir -ErrorAction SilentlyContinue
}

Write-Host "  Création du worktree temporaire..." -ForegroundColor Gray
git -C $root worktree add $tmpDir Installeur
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERREUR : impossible de créer le worktree." -ForegroundColor Red
    exit 1
}

try {
    Write-Host "  Copie des fichiers..." -ForegroundColor Gray
    $dstI = "$tmpDir\installer"
    New-Item -ItemType Directory -Force $dstI | Out-Null
    Copy-Item "$root\installer\*" $dstI -Recurse -Force -Exclude "Output"

    Write-Host "  Commit..." -ForegroundColor Gray
    git -C $tmpDir add -A
    $diff = git -C $tmpDir diff --cached --stat
    if (-not $diff) {
        Write-Host "  Aucun changement à committer." -ForegroundColor Yellow
        exit 0
    }
    Write-Host $diff -ForegroundColor DarkGray
    git -C $tmpDir commit -m $Message
    if ($LASTEXITCODE -ne 0) { throw "git commit échoué" }

    Write-Host ""
    Write-Host "  Push origin/Installeur..." -ForegroundColor Gray
    git -C $tmpDir push origin Installeur
    if ($LASTEXITCODE -ne 0) { throw "git push échoué" }

    Write-Host ""
    Write-Host "  OK — branch Installeur mise à jour." -ForegroundColor Green

} finally {
    git -C $root worktree remove --force $tmpDir 2>$null
    Remove-Item -Recurse -Force $tmpDir -ErrorAction SilentlyContinue
}

Write-Host ""
