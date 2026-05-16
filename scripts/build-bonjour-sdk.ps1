# Baut Apples Open-Source mDNSResponder (Apache-2.0) und arrangiert die Artefakte
# in der Bonjour-SDK-Verzeichnisstruktur, die UxPlay's CMake erwartet.
#
# Output:
#   vendor/bonjour-sdk/Include/dns_sd.h
#   vendor/bonjour-sdk/Lib/x64/dnssd.lib
#   vendor/bonjour-sdk/Bin/x64/dnssd.dll
#
# Setze danach BONJOUR_SDK_HOME=<repo>/vendor/bonjour-sdk bevor du UxPlay baust.
#
# Voraussetzung: Visual Studio 2022 Community + Workload "Desktopentwicklung mit C++".

[CmdletBinding()]
param(
    [string] $MdnsRef = 'rel/mDNSResponder-2881'
)

$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$buildDir   = Join-Path $repoRoot 'build'
$srcDir     = Join-Path $buildDir 'mdns2881'
$patchFile  = Join-Path $buildDir '2881.patch'
$sdkDir     = Join-Path $repoRoot 'vendor\bonjour-sdk'

$includeDir = Join-Path $sdkDir 'Include'
$libDir     = Join-Path $sdkDir 'Lib\x64'
$binDir     = Join-Path $sdkDir 'Bin\x64'

New-Item -ItemType Directory -Force -Path $buildDir, $includeDir, $libDir, $binDir | Out-Null

# 1. MSBuild via vswhere finden
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) {
    throw "vswhere.exe nicht gefunden. Visual Studio 2022 nicht installiert?"
}

$vsInstall = & $vswhere -latest -requires Microsoft.VisualStudio.Workload.NativeDesktop -property installationPath
if (-not $vsInstall) {
    throw "Workload 'Desktopentwicklung mit C++' fehlt. VS Installer -> Modify -> NativeDesktop hinzufuegen."
}
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
           Select-Object -First 1
if (-not $msbuild -or -not (Test-Path $msbuild)) {
    throw "MSBuild.exe nicht gefunden."
}
Write-Host "MSBuild: $msbuild" -ForegroundColor DarkGray

# 2. mDNSResponder klonen (falls noetig)
if (-not (Test-Path (Join-Path $srcDir '.git'))) {
    Write-Host ">>> mDNSResponder klonen ($MdnsRef)..." -ForegroundColor Cyan
    git clone --branch $MdnsRef --depth 1 --single-branch `
        https://github.com/apple-oss-distributions/mDNSResponder.git $srcDir
} else {
    Write-Host ">>> mDNSResponder bereits geklont, ueberspringe." -ForegroundColor DarkGray
}

# 3. Leapbtw-Patch downloaden
if (-not (Test-Path $patchFile)) {
    Write-Host ">>> Patch herunterladen..." -ForegroundColor Cyan
    Invoke-WebRequest -UseBasicParsing `
        -Uri 'https://raw.githubusercontent.com/leapbtw/uxplay-windows/main/mdnsresponder-patches/2881.patch' `
        -OutFile $patchFile
}

# 4. Patch anwenden (idempotent via Marker-File)
$patchMarker = Join-Path $srcDir '.leapbtw-patch-applied'
if (-not (Test-Path $patchMarker)) {
    Write-Host ">>> Patch anwenden..." -ForegroundColor Cyan
    Push-Location $srcDir
    try {
        & git apply --verbose $patchFile
        if ($LASTEXITCODE -ne 0) { throw "git apply fehlgeschlagen." }
        Set-Content -Path $patchMarker -Value (Get-Date -Format 'o')
    } finally {
        Pop-Location
    }
} else {
    Write-Host ">>> Patch bereits angewendet, ueberspringe." -ForegroundColor DarkGray
}

# 5. dnssd.dll + mDNSResponder.exe bauen
Write-Host ">>> MSBuild: dnssd.vcxproj (Release x64)..." -ForegroundColor Cyan
& $msbuild (Join-Path $srcDir 'mDNSWindows\DLL\dnssd.vcxproj') `
    /m /t:Build /p:Configuration=Release /p:Platform=x64 /v:minimal
if ($LASTEXITCODE -ne 0) { throw "MSBuild dnssd.vcxproj fehlgeschlagen ($LASTEXITCODE)." }

Write-Host ">>> MSBuild: mDNSResponder.vcxproj (Release x64)..." -ForegroundColor Cyan
& $msbuild (Join-Path $srcDir 'mDNSWindows\SystemService\mDNSResponder.vcxproj') `
    /m /t:Build /p:Configuration=Release /p:Platform=x64 /v:minimal
if ($LASTEXITCODE -ne 0) { throw "MSBuild mDNSResponder.vcxproj fehlgeschlagen ($LASTEXITCODE)." }

# 6. Artefakte einsammeln
$dllOut    = Join-Path $srcDir 'mDNSWindows\DLL\x64\Release\dnssd.dll'
$libOut    = Join-Path $srcDir 'mDNSWindows\DLL\x64\Release\dnssd.lib'
$header    = Join-Path $srcDir 'mDNSShared\dns_sd.h'
$exeOut    = Join-Path $srcDir 'mDNSWindows\SystemService\x64\Release\mDNSResponder.exe'

foreach ($f in @($dllOut, $libOut, $header, $exeOut)) {
    if (-not (Test-Path $f)) { throw "Erwartetes Artefakt fehlt: $f" }
}

Copy-Item -Force $header $includeDir
Copy-Item -Force $libOut $libDir
Copy-Item -Force $dllOut $binDir
Copy-Item -Force $exeOut $binDir

Write-Host ""
Write-Host "OK: Bonjour-SDK arrangiert unter $sdkDir" -ForegroundColor Green
Write-Host "  - $includeDir\dns_sd.h"
Write-Host "  - $libDir\dnssd.lib"
Write-Host "  - $binDir\dnssd.dll"
Write-Host "  - $binDir\mDNSResponder.exe"
