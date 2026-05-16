# Ruft copy-runtime-dlls.sh ueber MSYS2/MINGW64 auf.
# Muss NACH build-uxplay.ps1 laufen.

[CmdletBinding()]
param(
    [string] $Msys2Root = 'C:\msys64'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$bash     = Join-Path $Msys2Root 'usr\bin\bash.exe'
$shScript = Join-Path $repoRoot 'scripts\copy-runtime-dlls.sh'

if (-not (Test-Path $bash))      { throw "MSYS2 nicht gefunden ($Msys2Root)." }
if (-not (Test-Path $shScript))  { throw "copy-runtime-dlls.sh fehlt." }

function ConvertTo-MsysPath([string]$winPath) {
    "/$($winPath.Substring(0,1).ToLower())$($winPath.Substring(2).Replace('\','/'))"
}

$shUnix     = ConvertTo-MsysPath $shScript
$tmpWrapper = Join-Path $Msys2Root 'tmp\run-copy-dlls.sh'

@"
#!/bin/bash
exec bash "$shUnix" "`$@"
"@ | Out-File -Encoding ASCII -NoNewline $tmpWrapper

$env:MSYSTEM = 'MINGW64'
& $bash -lc 'bash /tmp/run-copy-dlls.sh'
if ($LASTEXITCODE -ne 0) { throw "DLL-Copy fehlgeschlagen (Exit $LASTEXITCODE)." }
