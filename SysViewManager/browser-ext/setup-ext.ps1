# =============================================================
# setup-ext.ps1 — Installation de l'extension SysView Media Bridge
# Méthode : force-install via la politique d'entreprise Windows
# (HKLM\SOFTWARE\Policies\...\ExtensionInstallForcelist)
#
# Navigateurs supportés : Brave, Chrome, Edge
# Prérequis : PowerShell en tant qu'Administrateur
# =============================================================
#Requires -RunAsAdministrator

$ExtDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ExtDir = (Resolve-Path $ExtDir).Path

Write-Host ""
Write-Host "=== SysView Media Bridge — Installation de l'extension ===" -ForegroundColor Cyan
Write-Host ""

# ── Étape 1 : trouver le navigateur pour emballer l'extension ──
function Find-Browser {
  $candidates = @(
    "$env:LOCALAPPDATA\BraveSoftware\Brave-Browser\Application\brave.exe",
    "$env:ProgramFiles\BraveSoftware\Brave-Browser\Application\brave.exe",
    "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe",
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
    "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
    "$env:LOCALAPPDATA\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles(x86)\Microsoft\Edge\Application\msedge.exe"
  )
  foreach ($c in $candidates) {
    if (Test-Path $c) { return $c }
  }
  return $null
}

$browser = Find-Browser
if (-not $browser) {
  Write-Host "[ERREUR] Aucun navigateur Chromium trouvé (Brave/Chrome/Edge)." -ForegroundColor Red
  Write-Host "         Installe Brave ou Chrome, puis relance ce script."
  exit 1
}
Write-Host "[OK] Navigateur trouvé : $browser" -ForegroundColor Green

# ── Étape 2 : emballer l'extension en .crx ─────────────────────
$CrxPath = Join-Path $ExtDir "sysview-media.crx"
$KeyPath = Join-Path $ExtDir "sysview-media.pem"

Write-Host "[...] Emballage de l'extension..." -ForegroundColor Yellow

# Fermer le navigateur si ouvert (le packer nécessite qu'il soit fermé)
$browserName = [System.IO.Path]::GetFileNameWithoutExtension($browser)
$running = Get-Process -Name $browserName -ErrorAction SilentlyContinue
if ($running) {
  Write-Host "      Fermeture de $browserName pour le packing..." -ForegroundColor Yellow
  $running | Stop-Process -Force
  Start-Sleep -Seconds 2
}

$packArgs = @("--pack-extension=$ExtDir", "--pack-extension-key=$KeyPath")
$result = Start-Process -FilePath $browser -ArgumentList $packArgs -Wait -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 2

# Le packer crée le .crx dans le dossier PARENT de l'extension
$parentDir   = Split-Path -Parent $ExtDir
$packedCrx   = Join-Path $parentDir "browser-ext.crx"
$packedKey   = Join-Path $parentDir "browser-ext.pem"

if (Test-Path $packedCrx) {
  Move-Item -Path $packedCrx -Destination $CrxPath -Force
  if (Test-Path $packedKey) { Move-Item -Path $packedKey -Destination $KeyPath -Force }
  Write-Host "[OK] Extension emballée : $CrxPath" -ForegroundColor Green
} elseif (Test-Path $CrxPath) {
  Write-Host "[OK] Extension déjà emballée : $CrxPath" -ForegroundColor Green
} else {
  Write-Host "[ERREUR] L'emballage a échoué. Vérifie les logs du navigateur." -ForegroundColor Red
  exit 1
}

# ── Étape 3 : lire l'ID de l'extension depuis la clé .pem ──────
# L'ID Chrome est calculé depuis la clé publique RSA (SHA-256, encodé a-p)
Add-Type @"
using System;
using System.Security.Cryptography;
using System.Text;
public static class ExtIdHelper {
    public static string ComputeId(string pemPath) {
        var lines = System.IO.File.ReadAllLines(pemPath);
        var sb = new StringBuilder();
        foreach (var line in lines) {
            if (!line.StartsWith("-----")) sb.Append(line.Trim());
        }
        var der = Convert.FromBase64String(sb.ToString());
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(der);
        // Encoder sur 32 caractères a-p (0=a ... 15=p)
        var id = new char[32];
        for (int i = 0; i < 16; i++) {
            id[i*2]   = (char)('a' + (hash[i] >> 4));
            id[i*2+1] = (char)('a' + (hash[i] & 0xF));
        }
        return new string(id);
    }
}
"@ -Language CSharp

$ExtId = [ExtIdHelper]::ComputeId($KeyPath)
Write-Host "[OK] ID de l'extension : $ExtId" -ForegroundColor Green

# ── Étape 4 : copier le .crx dans le dossier SysViewManager ────
# SysViewManager servira le CRX depuis /v1/ext/ (endpoint statique)
# Pour la politique ExtensionInstallForcelist, on utilise le chemin local
$CrxUri = "file:///" + $CrxPath.Replace('\', '/')

# ── Étape 5 : ajouter la politique pour chaque navigateur ──────
$ForcedValue = "${ExtId};${CrxUri}"

function Set-BrowserPolicy($regPath) {
  if (!(Test-Path $regPath)) {
    New-Item -Path $regPath -Force | Out-Null
  }
  # Chercher un slot libre (1, 2, 3…)
  $existing = Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue
  $slot = 1
  if ($existing) {
    $props = $existing.PSObject.Properties | Where-Object { $_.Name -match '^\d+$' }
    $ids = $props | ForEach-Object { ($_.Value -split ';')[0] }
    # Déjà installé ?
    if ($ids -contains $ExtId) { return $true }
    $slots = $props | ForEach-Object { [int]$_.Name }
    if ($slots) { $slot = ($slots | Measure-Object -Maximum).Maximum + 1 }
  }
  Set-ItemProperty -Path $regPath -Name "$slot" -Value $ForcedValue
  return $true
}

$installed = @()

# Brave
if (Set-BrowserPolicy "HKLM:\SOFTWARE\Policies\BraveSoftware\Brave\ExtensionInstallForcelist") {
  $installed += "Brave"
}
# Chrome
if (Set-BrowserPolicy "HKLM:\SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist") {
  $installed += "Chrome"
}
# Edge
if (Set-BrowserPolicy "HKLM:\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist") {
  $installed += "Edge"
}

Write-Host ""
Write-Host "[OK] Politique ajoutée pour : $($installed -join ', ')" -ForegroundColor Green
Write-Host ""
Write-Host "=== Installation terminée ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "  1. Lance ton navigateur (Brave / Chrome / Edge)."
Write-Host "  2. L'extension s'installe automatiquement."
Write-Host "  3. Lance SysViewManager, puis Wallpaper Engine."
Write-Host ""
Write-Host "  Si l'extension n'apparaît pas, ouvre chrome://extensions"
Write-Host "  et vérifie que 'SysView Media Bridge' est activée."
Write-Host ""
