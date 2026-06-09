# ─────────────────────────────────────────────────────────────────────────────
# install-cert.ps1  —  Installe le certificat Astralcodes sur cette machine
#
# À exécuter UNE SEULE FOIS en tant qu'Administrateur.
# Après ça, SysViewManager affiche "Astralcodes" dans les popups UAC.
#
# Usage :
#   Clic droit → "Exécuter avec PowerShell" (en tant qu'administrateur)
#   ou : powershell -ExecutionPolicy Bypass -File .\install-cert.ps1
# ─────────────────────────────────────────────────────────────────────────────

param([string] $CerPath = "$PSScriptRoot\certs\Astralcodes.cer")

Write-Host ""
Write-Host "  Installation du certificat Astralcodes" -ForegroundColor Cyan
Write-Host ""

# ── Vérifier les droits admin ─────────────────────────────────────────────────
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "  ERREUR : Ce script doit être exécuté en tant qu'Administrateur." -ForegroundColor Red
    Write-Host "  Clic droit sur le script → Exécuter avec PowerShell en tant qu'administrateur" -ForegroundColor Yellow
    Write-Host ""
    Read-Host "  Appuyez sur Entrée pour fermer"
    exit 1
}

# ── Trouver le fichier .cer ───────────────────────────────────────────────────
if (-not (Test-Path $CerPath)) {
    # Chercher à côté de l'exe si on est dans le dossier de distribution
    $altPath = Join-Path (Split-Path $CerPath) "Astralcodes.cer"
    if (Test-Path $altPath) { $CerPath = $altPath }
    else {
        Write-Host "  ERREUR : Fichier introuvable : $CerPath" -ForegroundColor Red
        Write-Host "  Assurez-vous que Astralcodes.cer est dans le dossier certs\ à côté de ce script." -ForegroundColor Yellow
        Write-Host ""
        Read-Host "  Appuyez sur Entrée pour fermer"
        exit 1
    }
}

Write-Host "  Certificat : $CerPath" -ForegroundColor Gray

$cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($CerPath)
Write-Host "  Sujet      : $($cert.Subject)" -ForegroundColor Gray
Write-Host "  Expire le  : $($cert.NotAfter.ToString('dd/MM/yyyy'))" -ForegroundColor Gray
Write-Host "  Thumbprint : $($cert.Thumbprint)" -ForegroundColor Gray
Write-Host ""

# ── Installer dans les stores LocalMachine ────────────────────────────────────
$ok = $true
foreach ($storeName in @("Root", "TrustedPublisher")) {
    try {
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
            $storeName,
            [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)

        # Vérifier si déjà installé
        $existing = $store.Certificates | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
        if ($existing) {
            Write-Host "  ✓ LocalMachine\$storeName — déjà installé" -ForegroundColor DarkGreen
        } else {
            $store.Add($cert)
            Write-Host "  ✓ LocalMachine\$storeName — installé" -ForegroundColor Green
        }
        $store.Close()
    } catch {
        Write-Host "  ✗ LocalMachine\$storeName — erreur : $($_.Exception.Message)" -ForegroundColor Red
        $ok = $false
    }
}

Write-Host ""
if ($ok) {
    Write-Host "  Installation terminée !" -ForegroundColor Green
    Write-Host "  SysViewManager affichera désormais 'Astralcodes' dans les popups UAC." -ForegroundColor Cyan
} else {
    Write-Host "  Installation partielle — certains stores ont échoué." -ForegroundColor Yellow
}

Write-Host ""
Read-Host "  Appuyez sur Entrée pour fermer"
