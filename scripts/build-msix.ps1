# Baut die App als MSIX-Paket fuer Sideload-Test oder Store-Upload.
# Voraussetzungen:
#   - scripts/generate-msix-assets.ps1 wurde mit der gewuenschten Icon-Quelle ausgefuehrt
#   - Package.appxmanifest enthaelt die korrekte Identity (Partner Center)
#
# Output: src/AirPlayReceiver.App/bin/Release/<rid>/AppPackages/<Name>_<Ver>.msix
#         und zugehoerige .appxsym / .msixupload (Store-Submit-Format)

[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Platform      = 'x64',
    [switch] $SkipAssetCheck,
    [switch] $StoreUpload      # baut .msixupload statt .msix (fuer Store-Submit)
)

$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$proj       = Join-Path $repoRoot 'src\AirPlayReceiver.App\AirPlayReceiver.App.csproj'
$assetsDir  = Join-Path $repoRoot 'src\AirPlayReceiver.App\Assets'

if (-not $SkipAssetCheck) {
    $required = @('StoreLogo.png','Square44x44Logo.png','Square150x150Logo.png',
                  'Wide310x150Logo.png','SplashScreen.png','LargeTile.png','SmallTile.png')
    $missing = $required | Where-Object { -not (Test-Path (Join-Path $assetsDir $_)) }
    if ($missing) {
        throw "Fehlen: $($missing -join ', '). Erst scripts/generate-msix-assets.ps1 ausfuehren."
    }
}

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'

Write-Host ">>> NuGet-Restore..." -ForegroundColor Cyan
& $dotnet restore $proj --runtime "win-$($Platform.ToLower())"

$mode = if ($StoreUpload) { 'StoreUpload' } else { 'SideloadOnly' }
Write-Host ">>> Build MSIX ($Configuration $Platform, Mode=$mode)..." -ForegroundColor Cyan
& $dotnet build $proj `
    -c $Configuration `
    --runtime "win-$($Platform.ToLower())" `
    -p:WindowsPackageType=MSIX `
    -p:GenerateAppxPackageOnBuild=true `
    -p:AppxPackageSigningEnabled=false `
    -p:UapAppxPackageBuildMode=$mode `
    -p:AppxBundle=Never `
    -p:Platform=$Platform

if ($LASTEXITCODE -ne 0) { throw "MSIX-Build fehlgeschlagen (Exit $LASTEXITCODE)." }

$pkgDir = Join-Path $repoRoot "src\AirPlayReceiver.App\bin\$Platform\$Configuration\net8.0-windows10.0.19041.0\win-$($Platform.ToLower())\AppPackages"
Write-Host ""
Write-Host "OK. Pakete liegen unter:" -ForegroundColor Green
Write-Host "  $pkgDir"
if (Test-Path $pkgDir) {
    Get-ChildItem $pkgDir -Recurse -Include '*.msix','*.msixbundle','*.msixupload' |
        ForEach-Object { Write-Host "    $($_.FullName) ($([int]($_.Length/1MB)) MB)" }
}
if ($StoreUpload) {
    Write-Host ""
    Write-Host "Naechster Schritt: im Partner Center unter 'Pakete' die .msixupload-Datei hochladen." -ForegroundColor Yellow
}
