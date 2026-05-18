# Erzeugt eine Multi-Size .ico (16/24/32/48/64/128/256) aus dem Spieglein-PNG.
# Notwendig fuer scharfe Anzeige in Win11-Titelbar (16x16) und Taskbar (24/32).

[CmdletBinding()]
param(
    [string] $Source = '',
    [string] $Out    = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Source) { $Source = Join-Path $repoRoot 'Assets\source\option-d2-spiegel-tiefblau.png' }
if (-not $Out)    { $Out    = Join-Path $repoRoot 'src\AirPlayReceiver.App\Assets\AppIcon.ico' }

$src = [System.Drawing.Image]::FromFile((Resolve-Path $Source))
$sizes = @(16, 24, 32, 48, 64, 128, 256)

# Multi-Size .ico bauen: header + N image entries + PNG payloads.
# Format-Doku: https://learn.microsoft.com/en-us/previous-versions/ms997538(v=msdn.10)
$ms = New-Object System.IO.MemoryStream

function Write-Le16([System.IO.BinaryWriter]$w, [uint16]$v) { $w.Write([uint16]$v) }
function Write-Le32([System.IO.BinaryWriter]$w, [uint32]$v) { $w.Write([uint32]$v) }

$bw = New-Object System.IO.BinaryWriter $ms
# ICONDIR (6 bytes)
Write-Le16 $bw 0           # reserved
Write-Le16 $bw 1           # type: 1 = ICO
Write-Le16 $bw $sizes.Count

# Wir bauen pro Groesse erstmal das PNG in einen MemoryStream, dann schreiben wir
# ICONDIRENTRY + Payload sequentiell.
$payloads = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, $s, $s)
    $g.Dispose()

    $pngMs = New-Object System.IO.MemoryStream
    $bmp.Save($pngMs, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $payloads += , @{ Size = $s; Bytes = $pngMs.ToArray() }
    $pngMs.Dispose()
}

# Offset des ersten Payloads = 6 (ICONDIR) + N * 16 (ICONDIRENTRY)
$offset = 6 + $sizes.Count * 16
foreach ($p in $payloads) {
    [byte]$sizeByte = if ($p.Size -ge 256) { 0 } else { [byte]$p.Size }
    $bw.Write($sizeByte)                       # width  (0 = 256)
    $bw.Write($sizeByte)                       # height (0 = 256)
    $bw.Write([byte]0)                          # color count
    $bw.Write([byte]0)                          # reserved
    Write-Le16 $bw 1                            # color planes
    Write-Le16 $bw 32                           # bpp
    Write-Le32 $bw $p.Bytes.Length              # size of payload
    Write-Le32 $bw $offset                      # offset
    $offset += $p.Bytes.Length
}
foreach ($p in $payloads) { $bw.Write($p.Bytes) }
$bw.Flush()

[System.IO.File]::WriteAllBytes($Out, $ms.ToArray())
$bw.Dispose(); $ms.Dispose(); $src.Dispose()

Write-Host "OK: $Out ($([math]::Round((Get-Item $Out).Length/1KB,1)) KB, $($sizes.Count) Groessen)"
