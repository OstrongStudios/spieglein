# Entwirft Icon-Kandidaten fuer Mirrorly. Speichert PNGs unter Assets/source/.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sourceDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'Assets\source'
New-Item -ItemType Directory -Force -Path $sourceDir | Out-Null

[int]$N = 1240
[int]$pad = [int]($N * 0.06)
[int]$rad = [int]($N * 0.22)

function New-Background([System.Drawing.Color]$c1, [System.Drawing.Color]$c2) {
    $bmp = New-Object System.Drawing.Bitmap($script:N, $script:N)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    [int]$x = $script:pad
    [int]$y = $script:pad
    [int]$w = $script:N - 2 * $script:pad
    [int]$h = $script:N - 2 * $script:pad
    [int]$r = $script:rad

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x,           $y,           $r, $r, 180, 90)
    $path.AddArc($x + $w - $r, $y,           $r, $r, 270, 90)
    $path.AddArc($x + $w - $r, $y + $h - $r, $r, $r,   0, 90)
    $path.AddArc($x,           $y + $h - $r, $r, $r,  90, 90)
    $path.CloseFigure()

    $pt1 = New-Object System.Drawing.PointF 0, 0
    $pt2 = New-Object System.Drawing.PointF ([single]$script:N), ([single]$script:N)
    $br  = New-Object System.Drawing.Drawing2D.LinearGradientBrush $pt1, $pt2, $c1, $c2
    $g.FillPath($br, $path)
    $br.Dispose()
    return @{ Bmp = $bmp; G = $g }
}

function Save([hashtable]$h, [string]$name) {
    $path = Join-Path $sourceDir $name
    $h.Bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $h.G.Dispose(); $h.Bmp.Dispose()
    Write-Host "  $name"
}

# ---------- Option A: Bold "S" blau ----------
$cTop = [System.Drawing.Color]::FromArgb(255,  30, 144, 255)  # DodgerBlue
$cBot = [System.Drawing.Color]::FromArgb(255,  60,  90, 200)
$o = New-Background $cTop $cBot
$font = New-Object System.Drawing.Font 'Segoe UI', 720, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$br   = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$fmt  = New-Object System.Drawing.StringFormat
$fmt.Alignment     = [System.Drawing.StringAlignment]::Center
$fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
$rectF = New-Object System.Drawing.RectangleF 0, 0, ([single]$N), ([single]$N)
$o.G.DrawString('S', $font, $br, $rectF, $fmt)
$font.Dispose(); $br.Dispose()
Save $o 'option-a-letter-s-blau.png'

# ---------- Option A2: "S" violett/lila (passt zu Maerchen-Vibe) ----------
$cTop2 = [System.Drawing.Color]::FromArgb(255, 138,  43, 226)  # BlueViolet
$cBot2 = [System.Drawing.Color]::FromArgb(255,  75,   0, 130)  # Indigo
$o = New-Background $cTop2 $cBot2
$font = New-Object System.Drawing.Font 'Segoe UI', 720, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$br   = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$o.G.DrawString('S', $font, $br, $rectF, $fmt)
$font.Dispose(); $br.Dispose()
Save $o 'option-a2-letter-s-violett.png'

# ---------- Option B: Bildschirm + Play-Triangle ----------
$dark1 = [System.Drawing.Color]::FromArgb(255,  20,  25,  35)
$dark2 = [System.Drawing.Color]::FromArgb(255,  40,  55,  85)
$o = New-Background $dark1 $dark2

# Bildschirm-Rahmen (weiss)
[float]$fx = $N * 0.22; [float]$fy = $N * 0.30
[float]$fw = $N * 0.56; [float]$fh = $N * 0.34
[float]$fr = $N * 0.05
[float]$thick = $N * 0.04

$fp = New-Object System.Drawing.Drawing2D.GraphicsPath
$fp.AddArc($fx,              $fy,              $fr, $fr, 180, 90)
$fp.AddArc($fx + $fw - $fr,  $fy,              $fr, $fr, 270, 90)
$fp.AddArc($fx + $fw - $fr,  $fy + $fh - $fr,  $fr, $fr,   0, 90)
$fp.AddArc($fx,              $fy + $fh - $fr,  $fr, $fr,  90, 90)
$fp.CloseFigure()
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $thick
$pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$o.G.DrawPath($pen, $fp)
$pen.Dispose()

# Play-Dreieck unter dem Bildschirm, Spitze zeigt nach OBEN
$tri = [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF ([single]($N * 0.50)), ([single]($N * 0.70))),
    (New-Object System.Drawing.PointF ([single]($N * 0.40)), ([single]($N * 0.85))),
    (New-Object System.Drawing.PointF ([single]($N * 0.60)), ([single]($N * 0.85)))
)
$brW = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$o.G.FillPolygon($brW, $tri)
$brW.Dispose()
Save $o 'option-b-screen-arrow.png'

# ---------- Option D: Maerchen-Spiegel mit Funkeln (gefuellte Silhouette) ----------
function New-MirrorIcon([System.Drawing.Color]$bgTop, [System.Drawing.Color]$bgBot,
                        [System.Drawing.Color]$mirror, [System.Drawing.Color]$inside,
                        [string]$file) {
    $o = New-Background $bgTop $bgBot
    $g = $o.G

    # Spiegel-Oval (gefuellt, Frame-Look durch innere Ellipse)
    [float]$ox = $script:N * 0.26; [float]$oy = $script:N * 0.16
    [float]$ow = $script:N * 0.48; [float]$oh = $script:N * 0.60
    $brF = New-Object System.Drawing.SolidBrush $mirror
    $g.FillEllipse($brF, $ox, $oy, $ow, $oh)
    $brF.Dispose()

    # Innere Glasflaeche (etwas heller, suggeriert Reflektion)
    [float]$mb = $script:N * 0.05
    $brIn = New-Object System.Drawing.SolidBrush $inside
    $g.FillEllipse($brIn, $ox + $mb, $oy + $mb, $ow - 2*$mb, $oh - 2*$mb)
    $brIn.Dispose()

    # Highlight auf Spiegelglas (kleine helle Sichel oben-links)
    $brHi = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 255, 255, 255))
    [float]$hx = $ox + $ow * 0.18; [float]$hy = $oy + $oh * 0.12
    [float]$hw = $ow * 0.35; [float]$hh = $oh * 0.22
    $g.FillEllipse($brHi, $hx, $hy, $hw, $hh)
    $brHi.Dispose()

    # Verbindungsstueck zwischen Spiegel und Griff (trapezfoermig, gold)
    $brG = New-Object System.Drawing.SolidBrush $mirror
    $trap = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ([single]($script:N * 0.42)), ([single]($script:N * 0.74))),
        (New-Object System.Drawing.PointF ([single]($script:N * 0.58)), ([single]($script:N * 0.74))),
        (New-Object System.Drawing.PointF ([single]($script:N * 0.55)), ([single]($script:N * 0.78))),
        (New-Object System.Drawing.PointF ([single]($script:N * 0.45)), ([single]($script:N * 0.78)))
    )
    $g.FillPolygon($brG, $trap)

    # Griff
    [float]$gx = $script:N * 0.45; [float]$gy = $script:N * 0.78
    [float]$gw = $script:N * 0.10; [float]$gh = $script:N * 0.13
    $g.FillRectangle($brG, $gx, $gy, $gw, $gh)
    # Abrundung am Griffende
    $g.FillEllipse($brG, $gx, $gy + $gh - $gw * 0.5, $gw, $gw)
    $brG.Dispose()

    # Funkeln (kleine 4-Punkte-Sterne als Kreuze)
    function Add-Sparkle([System.Drawing.Graphics]$gr, [single]$cx, [single]$cy, [single]$s) {
        $br = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
        # vertikaler Strich
        $gr.FillRectangle($br, $cx - $s * 0.10, $cy - $s, $s * 0.20, $s * 2)
        # horizontaler Strich
        $gr.FillRectangle($br, $cx - $s, $cy - $s * 0.10, $s * 2, $s * 0.20)
        # zentraler Punkt
        $gr.FillEllipse($br, $cx - $s * 0.18, $cy - $s * 0.18, $s * 0.36, $s * 0.36)
        $br.Dispose()
    }
    Add-Sparkle $g ([single]($script:N * 0.18)) ([single]($script:N * 0.34)) ([single]($script:N * 0.06))
    Add-Sparkle $g ([single]($script:N * 0.82)) ([single]($script:N * 0.30)) ([single]($script:N * 0.045))
    Add-Sparkle $g ([single]($script:N * 0.85)) ([single]($script:N * 0.66)) ([single]($script:N * 0.055))
    Add-Sparkle $g ([single]($script:N * 0.16)) ([single]($script:N * 0.78)) ([single]($script:N * 0.04))

    Save $o $file
}

# Variante D1: Violett-Märchen
New-MirrorIcon `
    ([System.Drawing.Color]::FromArgb(255,  90,  40, 150)) `
    ([System.Drawing.Color]::FromArgb(255, 200, 130, 230)) `
    ([System.Drawing.Color]::FromArgb(255, 255, 215,  60)) `
    ([System.Drawing.Color]::FromArgb(255, 240, 240, 255)) `
    'option-d1-spiegel-violett.png'

# Variante D2: Tiefblau-edel
New-MirrorIcon `
    ([System.Drawing.Color]::FromArgb(255,  15,  25,  70)) `
    ([System.Drawing.Color]::FromArgb(255,  40,  70, 140)) `
    ([System.Drawing.Color]::FromArgb(255, 240, 220, 100)) `
    ([System.Drawing.Color]::FromArgb(255, 220, 230, 255)) `
    'option-d2-spiegel-tiefblau.png'

# Variante D3: Rosa-verspielt
New-MirrorIcon `
    ([System.Drawing.Color]::FromArgb(255, 200,  80, 130)) `
    ([System.Drawing.Color]::FromArgb(255, 255, 180, 200)) `
    ([System.Drawing.Color]::FromArgb(255, 255, 255, 255)) `
    ([System.Drawing.Color]::FromArgb(255, 255, 220, 235)) `
    'option-d3-spiegel-rosa.png'

# ---------- Option C: AirPlay-aehnliches Motif (Bildschirm mit Dreieck DARUNTER, viel groesser) ----------
$cTop = [System.Drawing.Color]::FromArgb(255,  10, 132, 255)  # Apple-blau-ish
$cBot = [System.Drawing.Color]::FromArgb(255,   0,  80, 200)
$o = New-Background $cTop $cBot

# grosser Bildschirm-Rahmen
[float]$fx = $N * 0.18; [float]$fy = $N * 0.22
[float]$fw = $N * 0.64; [float]$fh = $N * 0.38
[float]$fr = $N * 0.06
[float]$thick = $N * 0.06

$fp = New-Object System.Drawing.Drawing2D.GraphicsPath
$fp.AddArc($fx,              $fy,              $fr, $fr, 180, 90)
$fp.AddArc($fx + $fw - $fr,  $fy,              $fr, $fr, 270, 90)
$fp.AddArc($fx + $fw - $fr,  $fy + $fh - $fr,  $fr, $fr,   0, 90)
$fp.AddArc($fx,              $fy + $fh - $fr,  $fr, $fr,  90, 90)
$fp.CloseFigure()
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $thick
$pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$o.G.DrawPath($pen, $fp)
$pen.Dispose()

# Grosses Play-Dreieck darunter, zeigt nach oben in den Bildschirm
$triC = [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF ([single]($N * 0.50)), ([single]($N * 0.65))),
    (New-Object System.Drawing.PointF ([single]($N * 0.34)), ([single]($N * 0.88))),
    (New-Object System.Drawing.PointF ([single]($N * 0.66)), ([single]($N * 0.88)))
)
$brW = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$o.G.FillPolygon($brW, $triC)
$brW.Dispose()
Save $o 'option-c-airplay-style.png'

Write-Host ""
Write-Host "OK." -ForegroundColor Green
