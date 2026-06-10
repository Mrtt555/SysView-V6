# =============================================================
# push-backend.ps1 — Pousse SysViewManager/ + scripts/ vers
# la branche GitHub "SysViewManager" SANS changer de branche
# dans le dossier de travail principal.
#
# Usage :
#   .\push-backend.ps1
#   .\push-backend.ps1 -Message "fix: correction bug X"
# =============================================================
param(
    [string] $Message = ""
)

$ErrorActionPreference = "Stop"
$root    = (Resolve-Path "$PSScriptRoot\..")
$tmpDir  = "$env:TEMP\sysview-backend-wt"

# ── Message de commit ─────────────────────────────────────────
if (-not $Message) {
    $ts      = Get-Date -Format "yyyy-MM-dd HH:mm"
    $Message = "chore: sync SysViewManager + scripts [$ts]"
}

Write-Host ""
Write-Host "  SysView V6 — push branch SysViewManager" -ForegroundColor Cyan
Write-Host "  Message : $Message" -ForegroundColor Gray
Write-Host ""

# ── Nettoyer un éventuel worktree orphelin ────────────────────
if (Test-Path $tmpDir) {
    Write-Host "  Nettoyage worktree précédent..." -ForegroundColor DarkGray
    git -C $root worktree remove --force $tmpDir 2>$null
    Remove-Item -Recurse -Force $tmpDir -ErrorAction SilentlyContinue
}

# ── Créer le worktree sur la branche SysViewManager ──────────
Write-Host "  Création du worktree temporaire..." -ForegroundColor Gray
git -C $root worktree add $tmpDir SysViewManager
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERREUR : impossible de créer le worktree." -ForegroundColor Red
    exit 1
}

try {
    # ── Copier les fichiers vers le worktree ──────────────────
    Write-Host "  Copie des fichiers..." -ForegroundColor Gray

    # SysViewManager/ (sans bin/ et obj/)
    $src = "$root\SysViewManager"
    $dst = "$tmpDir\SysViewManager"
    New-Item -ItemType Directory -Force $dst | Out-Null
    Get-ChildItem $src -File | Copy-Item -Destination $dst -Force
    Get-ChildItem $src -Directory |
        Where-Object { $_.Name -notin @('bin','obj') } |
        ForEach-Object { Copy-Item $_.FullName $dst -Recurse -Force }

    # scripts/
    $srcS = "$root\scripts"
    $dstS = "$tmpDir\scripts"
    New-Item -ItemType Directory -Force $dstS | Out-Null
    Copy-Item "$srcS\*" $dstS -Recurse -Force

    # ── Commit dans le worktree ───────────────────────────────
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

    # ── Push ─────────────────────────────────────────────────
    Write-Host ""
    Write-Host "  Push origin/SysViewManager..." -ForegroundColor Gray
    git -C $tmpDir push origin SysViewManager
    if ($LASTEXITCODE -ne 0) { throw "git push échoué" }

    Write-Host ""
    Write-Host "  OK — branch SysViewManager mise à jour." -ForegroundColor Green

} finally {
    # ── Toujours nettoyer le worktree ─────────────────────────
    git -C $root worktree remove --force $tmpDir 2>$null
    Remove-Item -Recurse -Force $tmpDir -ErrorAction SilentlyContinue
}

Write-Host ""
