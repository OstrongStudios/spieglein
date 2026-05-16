# Erzeugt selbst-signiertes Dev-Cert (Subject = Publisher im Manifest), vertraut
# es lokal in TrustedPeople und signiert + installiert das MSIX.
# Fuer Store-Upload NICHT noetig — Microsoft signiert nach dem Upload mit ihrem
# eigenen Zertifikat.
#
# Ein Punkt braucht UAC: das Cert in LocalMachine\TrustedPeople importieren.

[CmdletBinding()]
param(
    [string] $Subject  = 'CN=654B9AD8-3E71-4D00-B420-E162C13CD666',
    [string] $PfxPwd   = 'dev'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$pfxPath  = Join-Path $repoRoot 'dev-cert.pfx'
$msix     = Join-Path $repoRoot 'src\AirPlayReceiver.App\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\AppPackages\AirPlayReceiver.App_1.0.0.0_Test\AirPlayReceiver.App_1.0.0.0_x64.msix'

if (-not (Test-Path $msix)) { throw "MSIX nicht gefunden: $msix. Erst build-msix.ps1 ausfuehren." }

# 1. Cert generieren oder existierendes wiederverwenden
$existing = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $Subject } | Select-Object -First 1
if ($existing) {
    Write-Host ">>> Cert existiert bereits ($($existing.Thumbprint))" -ForegroundColor DarkGray
    $cert = $existing
} else {
    Write-Host ">>> Erzeuge selbst-signiertes Dev-Cert: $Subject" -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyUsage DigitalSignature `
        -FriendlyName 'Spieglein DevSign' `
        -NotAfter ((Get-Date).AddYears(5)) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')
    Write-Host "    Thumbprint: $($cert.Thumbprint)" -ForegroundColor DarkGray
}

# 2. Export zu PFX
$pwd = ConvertTo-SecureString -String $PfxPwd -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pwd | Out-Null
Write-Host ">>> PFX exportiert: $pfxPath" -ForegroundColor DarkGray

# 3. Cert in LocalMachine\TrustedPeople importieren (UAC!)
$alreadyTrusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
                  Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
if (-not $alreadyTrusted) {
    Write-Host ">>> Vertraue Cert in LocalMachine\TrustedPeople (UAC-Prompt erscheint!)..." -ForegroundColor Cyan
    $cmd = "Import-PfxCertificate -FilePath '$pfxPath' -Password (ConvertTo-SecureString -String '$PfxPwd' -Force -AsPlainText) -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null"
    $p = Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile","-Command",$cmd -PassThru -Wait
    if ($p.ExitCode -ne 0) { throw "Cert-Trust fehlgeschlagen (Exit $($p.ExitCode))." }
} else {
    Write-Host ">>> Cert bereits in LocalMachine\TrustedPeople." -ForegroundColor DarkGray
}

# 4. signtool finden + MSIX signieren
$signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe' -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signtool) { throw "signtool.exe nicht gefunden (Windows SDK)." }
Write-Host ">>> Signiere MSIX mit $($signtool.FullName)" -ForegroundColor Cyan
& $signtool.FullName sign /fd SHA256 /f $pfxPath /p $PfxPwd $msix
if ($LASTEXITCODE -ne 0) { throw "Signieren fehlgeschlagen (Exit $LASTEXITCODE)." }

# 5. Frueheres Paket entfernen + neu installieren
Write-Host ">>> Installiere..." -ForegroundColor Cyan
Get-AppxPackage -Name '4663Ostronggames.Spieglein' -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
Add-AppxPackage -Path $msix -ForceUpdateFromAnyVersion
Write-Host ""
Write-Host "OK. Installiert. Du findest 'Spieglein' im Startmenu." -ForegroundColor Green
Get-AppxPackage -Name '4663Ostronggames.Spieglein' | Select-Object Name, Version, InstallLocation
