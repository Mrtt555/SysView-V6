# ─────────────────────────────────────────────────────────────────────────────
# publish.ps1  —  Build Release local de SysViewManager
#
# Usage :
#   .\publish.ps1                  # version 0.0.0 (dev)
#   .\publish.ps1 -Version 1.2.0   # version personnalisée
#   .\publish.ps1 -Open            # ouvre le dossier output après build
#   .\publish.ps1 -Kill            # arrête SysViewManager avant de compiler
#   .\publish.ps1 -NoSign          # skip la signature de code
# ─────────────────────────────────────────────────────────────────────────────
param(
    [string] $Version = "0.0.0",
    [switch] $Open,
    [switch] $Kill,
    [switch] $NoSign
)

$ErrorActionPreference = "Stop"

$proj    = "$PSScriptRoot\SysViewManager\SysViewManager.csproj"
$outDir  = "$PSScriptRoot\SysViewManager\bin\Release\net8.0-windows10.0.17763.0\win-x64\publish"
$exe     = "$outDir\SysViewManager.exe"
$certDir = "$PSScriptRoot\certs"
$cerFile = "$certDir\Astralcodes.cer"

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

# ── Build ─────────────────────────────────────────────────────────────────────
$sw = [System.Diagnostics.Stopwatch]::StartNew()

dotnet publish $proj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:AssemblyVersion=$Version `
    -p:FileVersion=$Version `
    -p:InformationalVersion=$Version `
    --nologo

$sw.Stop()

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

# ── Signature de code ─────────────────────────────────────────────────────────
if (-not $NoSign) {
    Write-Host ""
    Write-Host "  ── Signature de code ──────────────────────────────" -ForegroundColor DarkCyan

    # Chercher le certificat Astralcodes dans le store personnel
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -eq "CN=Astralcodes" } |
            Select-Object -First 1

    # Créer le certificat s'il n'existe pas encore
    if (-not $cert) {
        Write-Host "  Certificat introuvable — création d'un nouveau certificat Astralcodes..." -ForegroundColor Yellow

        $cert = New-SelfSignedCertificate `
            -Subject        "CN=Astralcodes" `
            -FriendlyName   "Astralcodes Code Signing" `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -KeyUsage       DigitalSignature `
            -Type           CodeSigningCert `
            -HashAlgorithm  SHA256 `
            -NotAfter       (Get-Date).AddYears(10)

        # Ajouter aux stores de confiance de la machine courante
        foreach ($storeName in @("Root", "TrustedPublisher")) {
            $store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
                $storeName,
                [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
            $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
            $store.Add($cert)
            $store.Close()
        }
        Write-Host "  Certificat créé (valide 10 ans) — Thumbprint : $($cert.Thumbprint)" -ForegroundColor Green

        # Exporter le .cer public pour les amis
        New-Item -ItemType Directory -Force -Path $certDir | Out-Null
        [System.IO.File]::WriteAllBytes(
            $cerFile,
            $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
        Write-Host "  Certificat public exporté : $cerFile" -ForegroundColor Green
        Write-Host "  → Partagez ce fichier + install-cert.ps1 avec vos amis (à exécuter une seule fois en admin)" -ForegroundColor Yellow
    } else {
        Write-Host "  Certificat trouvé — Thumbprint : $($cert.Thumbprint.Substring(0,16))..." -ForegroundColor Gray
    }

    # Signer l'exe
    Write-Host "  Signature de SysViewManager.exe..." -ForegroundColor Gray
    try {
        # Avec horodatage (la signature reste valide même après expiration du cert)
        $sig = Set-AuthenticodeSignature `
            -FilePath        $exe `
            -Certificate     $cert `
            -TimestampServer "http://timestamp.digicert.com"
    } catch {
        # Serveur d'horodatage inaccessible — signer sans timestamp
        Write-Host "  Serveur de timestamp inaccessible — signature sans horodatage" -ForegroundColor Yellow
        $sig = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert
    }

    if ($sig.Status -eq "Valid") {
        Write-Host "  Signature OK — SysViewManager.exe est signé par Astralcodes" -ForegroundColor Green
    } else {
        Write-Host "  Signature : $($sig.Status) — $($sig.StatusMessage)" -ForegroundColor Yellow
        Write-Host "  (L'exe fonctionne mais affichera toujours 'éditeur inconnu')" -ForegroundColor Gray
    }
}

Write-Host ""

if ($Open) { Start-Process explorer.exe $outDir }
