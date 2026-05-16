# Erzeugt alle MSIX-Visual-Assets fuer Mirrorly aus einem quadratischen Quellbild.
# Eingabe: -Source <Pfad zu PNG (>=1240x1240)>
# Wenn keine Source: erzeugt einen Platzhalter (blauer Kreis auf transparent).
#
# Output: src/AirPlayReceiver.App/Assets/*.png

[CmdletBinding()]
param(
    [string] $Source = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot   = Split-Path -Parent $PSScriptRoot
$assetsDir  = Join-Path $repoRoot 'src\AirPlayReceiver.App\Assets'
$bgColor    = [System.Drawing.Color]::FromArgb(255, 10, 10, 10)   # #0A0A0A fuer Splash
$accent     = [System.Drawing.Color]::FromArgb(255, 30, 144, 255) # DodgerBlue

New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null

# Quelle laden oder Platzhalter erzeugen (1240x1240)
function New-PlaceholderBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $brush = New-Object System.Drawing.SolidBrush($accent)
    $pad = [int]($size * 0.10)
    $g.FillEllipse($brush, $pad, $pad, $size - 2*$pad, $size - 2*$pad)
    $brush.Dispose()
    $g.Dispose()
    return $bmp
}

if ($Source -and (Test-Path $Source)) {
    Write-Host ">>> Quelle: $Source" -ForegroundColor Cyan
    $src = [System.Drawing.Image]::FromFile((Resolve-Path $Source))
} else {
    Write-Host ">>> Keine Quelle, erzeuge Platzhalter (blauer Kreis)" -ForegroundColor Yellow
    $src = New-PlaceholderBitmap 1240
}

# Skaliert das Quellbild quadratisch in $size x $size, transparenter Hintergrund.
function Save-Square([int]$size, [string]$file) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()
    $path = Join-Path $assetsDir $file
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  $file ($size x $size)"
}

# Wide-Logo: Bild zentriert auf $w x $h, Background-Farbe.
function Save-Wide([int]$w, [int]$h, [System.Drawing.Color]$bg, [string]$file) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear($bg)
    $s = [int]($h * 0.80)
    $x = [int](($w - $s) / 2)
    $y = [int](($h - $s) / 2)
    $g.DrawImage($src, $x, $y, $s, $s)
    $g.Dispose()
    $path = Join-Path $assetsDir $file
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  $file ($w x $h)"
}

Write-Host ">>> Generiere Visual Assets in $assetsDir" -ForegroundColor Cyan

# Square 44x44 + Skalierungen + Targetsizes
Save-Square 44   'Square44x44Logo.png'
Save-Square 88   'Square44x44Logo.scale-200.png'
foreach ($ts in 16, 24, 32, 48, 256) {
    Save-Square $ts "Square44x44Logo.targetsize-$ts.png"
    Save-Square $ts "Square44x44Logo.targetsize-${ts}_altform-unplated.png"
}

# Small/Medium/Large square tiles
Save-Square 71   'SmallTile.png'
Save-Square 142  'SmallTile.scale-200.png'
Save-Square 150  'Square150x150Logo.png'
Save-Square 300  'Square150x150Logo.scale-200.png'
Save-Square 310  'LargeTile.png'
Save-Square 620  'LargeTile.scale-200.png'

# Wide-Logo
Save-Wide 310 150 $bgColor 'Wide310x150Logo.png'
Save-Wide 620 300 $bgColor 'Wide310x150Logo.scale-200.png'

# Splash + Store-Logo
Save-Wide 620 300 $bgColor 'SplashScreen.png'
Save-Wide 1240 600 $bgColor 'SplashScreen.scale-200.png'
Save-Square 50 'StoreLogo.png'
Save-Square 100 'StoreLogo.scale-200.png'

$src.Dispose()
Write-Host ""
Write-Host "OK: $(Get-ChildItem $assetsDir -Filter '*.png' | Measure-Object | Select-Object -ExpandProperty Count) Bilder erzeugt." -ForegroundColor Green
