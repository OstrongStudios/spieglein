# Prueft, ob das Speichern der Einstellungen Felder erhaelt, die der Dialog nicht anzeigt.
#
# Ablauf: settings.json mit einer "letzten Verbindung" vorbereiten -> App starten ->
# ueber die Oberflaeche Einstellungen oeffnen, Geraetenamen aendern, speichern ->
# settings.json wieder lesen. Erwartet: Name geaendert, letzte Verbindung erhalten.
#
# Gesucht wird ueber AutomationId (= x:Name im XAML), nicht ueber die Beschriftung:
# die haengt an der eingestellten Sprache, die AutomationId nicht.
#
# ACHTUNG, Aussagekraft: So wie er hier steht, prueft der Test nur
# LastConnectedDevice - und das Feld war schon vor der Korrektur durch eine
# Handreparatur in MainWindow.xaml.cs geschuetzt. Der Test besteht deshalb auch
# mit dem alten, fehlerhaften Dialog.
#
# Um wirklich zu pruefen, ob AppSettings.Clone() greift, voruebergehend ein
# beliebiges Feld an AppSettings haengen, das der Dialog NICHT anzeigt:
#
#     public int ProbeZaehler { get; set; }
#
# dann hier ProbeZaehler=42 in die vorbereitete settings.json aufnehmen und
# hinterher pruefen. Mit Clone() bleibt 42 stehen, ohne faellt es auf 0.
# Danach das Feld wieder entfernen. Siehe Handbuch 7.29.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;using System.Runtime.InteropServices;
public class T {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint dx,uint dy,uint d,IntPtr e);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int hg,bool r);
  public static void ClickAt(int x,int y){ SetCursorPos(x,y); System.Threading.Thread.Sleep(150);
    mouse_event(0x02,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(80); mouse_event(0x04,0,0,0,IntPtr.Zero); }
}
"@

$exe = 'D:\Spieglein\AirPlayReceiver\src\AirPlayReceiver.App\bin\Debug\net8.0-windows10.0.19041.0\win-x64\AirPlayReceiver.App.exe'
$cfg = "$env:LOCALAPPDATA\AirPlayReceiver\settings.json"
$bak = Join-Path $env:TEMP 'spieglein-settings-vor-clonetest.json'

$ZEUGE = 'iPhone von Mathias'
$ALT   = 'NAME-VORHER'
$NEU   = 'NAME-NACHHER'

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

function ById($root, [string]$id) {
  $root.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)))
}
function ByName($root, [string]$n) {
  $root.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $n)))
}
function Klick($el) {
  $r = $el.Current.BoundingRectangle
  [T]::ClickAt([int]($r.X + $r.Width/2), [int]($r.Y + $r.Height/2))
}
function Dump($root, [string]$titel) {
  Write-Host "  --- $titel ---"
  $root.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
    Select-Object -First 40 | ForEach-Object {
      "    {0,-22} id={1,-18} name=[{2}]" -f $_.Current.ControlType.ProgrammaticName, $_.Current.AutomationId, $_.Current.Name
    }
}

# --- Ausgangslage ---------------------------------------------------------
if (Test-Path $cfg) { Copy-Item $cfg $bak -Force }
New-Item -ItemType Directory -Force -Path (Split-Path $cfg) | Out-Null
([ordered]@{ DeviceName=$ALT; Pin=$null; AudioOnly=$false; Language='de-DE'; LastConnectedDevice=$ZEUGE } |
  ConvertTo-Json) | Out-File $cfg -Encoding utf8
Write-Host "VORHER : $((Get-Content $cfg -Raw) -replace '\s+',' ')"

Get-Process AirPlayReceiver.App -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

$p = $null
try {
  $p = Start-Process $exe -PassThru
  $h = [IntPtr]::Zero
  for ($i=0; $i -lt 40 -and $h -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 300; $p.Refresh(); $h = $p.MainWindowHandle }
  if ($h -eq [IntPtr]::Zero) { throw 'Kein Fenster' }
  [T]::MoveWindow($h, 80, 60, 1200, 800, $true) | Out-Null
  [T]::SetForegroundWindow($h) | Out-Null
  Start-Sleep -Milliseconds 1500

  $win = $AE::FromHandle($h)
  $more = ById $win 'MoreButton'
  if (-not $more) { Dump $win 'Hauptfenster'; throw 'MoreButton nicht gefunden' }
  Klick $more
  Start-Sleep -Milliseconds 1000

  $desktop = $AE::RootElement
  $item = ById $desktop 'MenuSettings'
  if (-not $item) { $item = ByName $desktop 'Einstellungen…' }
  if (-not $item) { Dump $desktop 'Desktop nach Menueklick'; throw 'MenuSettings nicht gefunden' }
  Klick $item
  Start-Sleep -Milliseconds 1800

  $win = $AE::FromHandle($h)
  $box = ById $win 'DeviceNameBox'
  if (-not $box) { Dump $win 'Fenster mit Dialog'; throw 'DeviceNameBox nicht gefunden' }
  $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($NEU)
  Start-Sleep -Milliseconds 500

  $save = ByName $win 'Speichern'
  if (-not $save) { Dump $win 'Fenster mit Dialog'; throw 'Speichern nicht gefunden' }
  Klick $save
  Start-Sleep -Milliseconds 2000
}
finally {
  if ($p) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
  Start-Sleep -Milliseconds 500
}

# --- Auswertung -----------------------------------------------------------
$nachher = Get-Content $cfg -Raw
Write-Host "NACHHER: $($nachher -replace '\s+',' ')"
$j = $nachher | ConvertFrom-Json
Write-Host ''
Write-Host ("Geraetename geaendert      : {0,-5}  [{1}]" -f ($j.DeviceName -eq $NEU), $j.DeviceName)
Write-Host ("Letzte Verbindung erhalten : {0,-5}  [{1}]" -f ($j.LastConnectedDevice -eq $ZEUGE), $j.LastConnectedDevice)
Write-Host ("Sprache erhalten           : {0,-5}  [{1}]" -f ($j.Language -eq 'de-DE'), $j.Language)

if (Test-Path $bak) { Copy-Item $bak $cfg -Force; Write-Host ''; Write-Host 'settings.json zurueckgestellt' }
