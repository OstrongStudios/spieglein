# Erzeugt die Store-Listing-Logos in den von Partner Center geforderten Formaten.
# Output: Assets/store/*.png

[CmdletBinding()]
param(
    [string] $Source = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repoRoot 'Assets\store'
New-Item -ItemType Directory -Force -Path $out | Out-Null

if (-not $Source) {
    $Source = Join-Path $repoRoot 'Assets\source\option-d2-spiegel-tiefblau.png'
}
if (-not (Test-Path $Source)) { throw "Source nicht gefunden: $Source" }
Write-Host ">>> Source: $Source" -ForegroundColor DarkGray

$src = [System.Drawing.Image]::FromFile((Resolve-Path $Source))
# Hintergrundfarbe fuer das 9:16-Poster (passend zum Icon-Dunkelblau)
$bg = [System.Drawing.Color]::FromArgb(255, 15, 25, 70)

function Save-Square([int]$size, [string]$file) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()
    $bmp.Save((Join-Path $out $file), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  $file ($size x $size)"
}

# Poster im 9:16-Format, Icon mittig auf dunklem Hintergrund
function Save-Poster([int]$w, [int]$h, [string]$file) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear($bg)
    # Icon: 60 % der Breite, zentriert
    [int]$s = [int]($w * 0.60)
    [int]$x = [int](($w - $s) / 2)
    [int]$y = [int](($h - $s) / 2)
    $g.DrawImage($src, $x, $y, $s, $s)
    $g.Dispose()
    $bmp.Save((Join-Path $out $file), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  $file ($w x $h)"
}

# --- Was Partner Center will ---

# Postergrafik 9:16 (erforderlich/empfohlen)
Save-Poster 720  1080 'PosterArt-720x1080.png'
Save-Poster 1440 2160 'PosterArt-1440x2160.png'

# Verpackungsgrafik 1:1 (empfohlen)
Save-Square 1080 'PackageGraphic-1080.png'
Save-Square 2160 'PackageGraphic-2160.png'

# 1:1-App-Kachelsymbol 300x300 (empfohlen)
Save-Square 300  'AppTile-300.png'

# Optional: separate 150x150 / 71x71 (wir haben die schon in den MSIX-Assets, hier nochmal als Store-Override)
Save-Square 150  'StoreTile-150.png'
Save-Square 71   'StoreTile-71.png'

$src.Dispose()
Write-Host ""
Write-Host "OK: $(Get-ChildItem $out -Filter '*.png' | Measure-Object | Select-Object -ExpandProperty Count) Logos in $out" -ForegroundColor Green
