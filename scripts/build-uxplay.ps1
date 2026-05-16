# Orchestriert den vollstaendigen UxPlay-Build auf Windows:
#   1. Stellt sicher, dass die Bonjour-SDK-Artefakte gebaut wurden
#   2. Ruft das MSYS2-Bash-Script auf, das UxPlay via MINGW64 baut
#   3. Kopiert uxplay.exe in den App-Output
#
# Voraussetzungen:
#   - MSYS2 unter C:\msys64 mit den UxPlay-Build-Paketen (siehe scripts/install-prereqs.md)
#   - Visual Studio 2022 + Workload "Desktopentwicklung mit C++" (fuer mDNSResponder)
#   - Git im PATH (Windows-seitig fuer den Bootstrap)

[CmdletBinding()]
param(
    [string] $UxPlayRef  = 'master',
    [string] $Msys2Root  = 'C:\msys64',
    [switch] $SkipBonjour
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$bash     = Join-Path $Msys2Root 'usr\bin\bash.exe'
$sdkDir   = Join-Path $repoRoot 'vendor\bonjour-sdk'
$shScript = Join-Path $repoRoot 'scripts\build-uxplay.sh'

if (-not (Test-Path $bash))  { throw "MSYS2 nicht gefunden ($Msys2Root)." }
if (-not (Test-Path $shScript)) { throw "build-uxplay.sh fehlt: $shScript" }

# 1. Bonjour-SDK bauen, falls nicht vorhanden
$dnssdH = Join-Path $sdkDir 'Include\dns_sd.h'
if (-not $SkipBonjour -and -not (Test-Path $dnssdH)) {
    Write-Host ">>> Bonjour-SDK (mDNSResponder) bauen..." -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'build-bonjour-sdk.ps1')
}

function ConvertTo-MsysPath([string]$winPath) {
    $drive = $winPath.Substring(0,1).ToLower()
    $rest  = $winPath.Substring(2).Replace('\','/')
    return "/$drive$rest"
}

# 2. UxPlay-Build via MSYS2/MINGW64
# Wir schreiben einen kleinen Wrapper nach /tmp, weil PowerShell Quoting + bash -lc
# bei Pfaden mit Leerzeichen unzuverlaessig ist.
$tmpWrapper   = Join-Path $Msys2Root 'tmp\run-uxplay-build.sh'
$shScriptUnix = ConvertTo-MsysPath $shScript
$bonjourUnix  = ConvertTo-MsysPath $sdkDir

@"
#!/bin/bash
exec bash "$shScriptUnix" "`$@"
"@ | Out-File -Encoding ASCII -NoNewline $tmpWrapper

$env:MSYSTEM         = 'MINGW64'
$env:BONJOUR_SDK_HOME = $sdkDir

Write-Host ">>> UxPlay bauen (BONJOUR_SDK_HOME=$bonjourUnix)..." -ForegroundColor Cyan
& $bash -lc "BONJOUR_SDK_HOME='$bonjourUnix' bash /tmp/run-uxplay-build.sh '$UxPlayRef'"
if ($LASTEXITCODE -ne 0) { throw "UxPlay-Build fehlgeschlagen (Exit $LASTEXITCODE)." }

Write-Host ""
Write-Host "OK: uxplay.exe -> $repoRoot\src\AirPlayReceiver.App\uxplay\" -ForegroundColor Green
Write-Host "Naechster Schritt: ./scripts/copy-runtime-dlls.ps1 (kopiert GStreamer + dnssd.dll)" -ForegroundColor Yellow
