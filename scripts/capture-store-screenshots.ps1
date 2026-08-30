# Nimmt Store-Screenshots der App in allen vorhandenen Sprachen auf.
# Pro Sprache zwei Bilder: Startzustand (01-start) und Bereit-Zustand mit
# 3-Schritt-Anleitung (02-ready).
#
# Ablauf je Sprache: settings.json auf die Sprache setzen -> App starten ->
# Fenster auf feste Groesse -> abfotografieren -> "AirPlay starten" per
# UI-Automation klicken -> erneut abfotografieren -> App schliessen.

param(
  # Nur diese Sprachen aufnehmen, z. B. -Only ja-JP. Leer = alle.
  [string[]] $Only = @()
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WinCap {
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int hgt,bool repaint);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int cmd);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,IntPtr extra);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h,int attr,out RECT val,int size);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
  public static void ClickAt(int x,int y){ SetCursorPos(x,y); System.Threading.Thread.Sleep(120); mouse_event(0x02,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(60); mouse_event(0x04,0,0,0,IntPtr.Zero); }
}
"@

$exe        = 'D:\Spieglein\AirPlayReceiver\src\AirPlayReceiver.App\bin\Debug\net8.0-windows10.0.19041.0\win-x64\AirPlayReceiver.App.exe'
$settings   = "$env:LOCALAPPDATA\AirPlayReceiver\settings.json"
$outRoot    = 'D:\Spieglein\AirPlayReceiver\Assets\store\screenshots'
$winX = 60; $winY = 40; $winW = 1600; $winH = 900
# Maus tief im schwarzen Video-Bereich parken -> keine Tooltips ueber Toolbar-Buttons
$parkX = $winX + [int]($winW / 2); $parkY = $winY + [int]($winH * 0.80)

# Sprache -> localized text of the "start" button (zum Anklicken per UIA)
$langs = [ordered]@{
  'de-DE'   = 'AirPlay starten'
  'en-US'   = 'Start AirPlay'
  'es-ES'   = 'Activar AirPlay'
  'fr-FR'   = 'Activer AirPlay'
  'ja-JP'   = "AirPlay $([char]0x3092)$([char]0x958B)$([char]0x59CB)"   # AirPlay を開始
  'pt-BR'   = 'Ativar AirPlay'
  'zh-Hans' = "$([char]0x542F)$([char]0x52A8) AirPlay"   # 启动 AirPlay
}

function Stop-All {
  Get-Process AirPlayReceiver.App, uxplay, mDNSResponder -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Milliseconds 500
}

function Capture([IntPtr]$hwnd, [string]$path) {
  $r = New-Object WinCap+RECT
  $size = [System.Runtime.InteropServices.Marshal]::SizeOf($r)
  [WinCap]::DwmGetWindowAttribute($hwnd, 9, [ref]$r, $size) | Out-Null   # DWMWA_EXTENDED_FRAME_BOUNDS
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  if ($w -le 0 -or $h -le 0) { throw "Ungueltige Fenstergroesse $w x $h" }
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
  $g.Dispose()
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
}

# Findet ein Element per Name und klickt es mit einem ECHTEN Mausklick auf seine
# Bildschirm-Mitte. Bewusst kein UIA-Invoke: das wuerde WinUI als Tastatur-Aktion
# werten und den "Esc"-Accelerator-Hinweis einblenden.
function Click-ByName([IntPtr]$hwnd, [string]$name) {
  $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
  $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)
  $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
  if (-not $el) { return $false }
  $r = $el.Current.BoundingRectangle    # Bildschirm-Koordinaten
  $cx = [int]($r.X + $r.Width / 2); $cy = [int]($r.Y + $r.Height / 2)
  [WinCap]::ClickAt($cx, $cy)
  return $true
}

$codes = if ($Only.Count) { $langs.Keys | Where-Object { $Only -contains $_ } } else { $langs.Keys }
if (-not $codes) { throw "Keine passende Sprache in -Only: $($Only -join ', ')" }

foreach ($code in $codes) {
  $btn = $langs[$code]
  Write-Host ">>> $code" -ForegroundColor Cyan
  Stop-All

  $dir = Join-Path $outRoot $code
  New-Item -ItemType Directory -Force -Path $dir | Out-Null
  New-Item -ItemType Directory -Force -Path (Split-Path $settings) | Out-Null

  # settings.json: Sprache fix, sauberer Startzustand, sprechender Geraetename
  $json = "{`"DeviceName`":`"GAME-PC`",`"Pin`":null,`"AudioOnly`":false,`"Language`":`"$code`",`"LastConnectedDevice`":null}"
  [System.IO.File]::WriteAllText($settings, $json)

  $p = Start-Process $exe -PassThru
  # auf Fenster-Handle warten
  $hwnd = [IntPtr]::Zero
  for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 300
    $p.Refresh()
    if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $p.MainWindowHandle; break }
  }
  if ($hwnd -eq [IntPtr]::Zero) { Write-Host "  KEIN Fenster, ueberspringe" -ForegroundColor Red; continue }

  [WinCap]::ShowWindow($hwnd, 9) | Out-Null          # SW_RESTORE
  [WinCap]::MoveWindow($hwnd, $winX, $winY, $winW, $winH, $true) | Out-Null
  [WinCap]::SetForegroundWindow($hwnd) | Out-Null
  [WinCap]::SetCursorPos($parkX, $parkY) | Out-Null
  Start-Sleep -Milliseconds 1500

  # 1) Startzustand
  Capture $hwnd (Join-Path $dir '01-start.png')
  Write-Host "  01-start.png"

  # 2) "AirPlay starten" per echtem Mausklick -> Bereit-Zustand
  $clicked = Click-ByName $hwnd $btn
  if (-not $clicked) { Write-Host "  Button '$btn' nicht gefunden" -ForegroundColor Yellow }
  Start-Sleep -Seconds 4               # uxplay-Start + State-Wechsel + Layout
  [WinCap]::SetForegroundWindow($hwnd) | Out-Null
  [WinCap]::SetCursorPos($parkX, $parkY) | Out-Null   # Maus aus der Toolbar, in den schwarzen Bereich
  Start-Sleep -Milliseconds 1500
  Capture $hwnd (Join-Path $dir '02-ready.png')
  Write-Host "  02-ready.png"

  Stop-All
}

# Am Ende: Sprache zurueck auf auto
[System.IO.File]::WriteAllText($settings, '{"DeviceName":"GAME-PC","Pin":null,"AudioOnly":false,"Language":"auto","LastConnectedDevice":null}')
Write-Host ""
Write-Host "Fertig. Screenshots unter $outRoot" -ForegroundColor Green
