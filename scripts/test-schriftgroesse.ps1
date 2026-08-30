# Prueft die Schriftgroessen-Auswahl: Dialog oeffnen, "Sehr gross" waehlen,
# speichern, und nachsehen ob Schrift, Statuspunkt und Breiten mitziehen.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;using System.Runtime.InteropServices;
public class G {
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int hg,bool r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint dx,uint dy,uint d,IntPtr e);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h,int a,out RECT v,int s);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
  public static void ClickAt(int x,int y){ SetCursorPos(x,y); System.Threading.Thread.Sleep(150);
    mouse_event(0x02,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(80); mouse_event(0x04,0,0,0,IntPtr.Zero); }
}
"@

$exe = 'D:\Spieglein\AirPlayReceiver\src\AirPlayReceiver.App\bin\Debug\net8.0-windows10.0.19041.0\win-x64\AirPlayReceiver.App.exe'
$out = 'D:\Temp\claude\D--Spieglein\d4a75b59-3ebc-4dcf-8e8f-8c2870a960f0\scratchpad'
$cfg = "$env:LOCALAPPDATA\AirPlayReceiver\settings.json"
$bak = Join-Path $env:TEMP 'spieglein-settings-vor-groessentest.json'

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
function ById($r,[string]$id){ $r.FindFirst($TS::Descendants,(New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty,$id))) }
function ByName($r,[string]$n){ $r.FindFirst($TS::Descendants,(New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty,$n))) }
# Wartet, bis ein Element auftaucht. Ohne das schlaegt der Test gelegentlich
# fehl, weil der Dialog noch nicht fertig aufgebaut ist - ein Testlauf, der
# mal geht und mal nicht, ist schlimmer als keiner.
function WarteAuf([scriptblock]$sucher, [int]$maxMs = 6000) {
  $bis = (Get-Date).AddMilliseconds($maxMs)
  do {
    $e = & $sucher
    if ($e) { return $e }
    Start-Sleep -Milliseconds 250
  } while ((Get-Date) -lt $bis)
  return $null
}
function Klick($el){ $r=$el.Current.BoundingRectangle; [G]::ClickAt([int]($r.X+$r.Width/2),[int]($r.Y+$r.Height/2)) }
function Foto($h,$pfad){
  $r=New-Object G+RECT
  [G]::DwmGetWindowAttribute($h,9,[ref]$r,[System.Runtime.InteropServices.Marshal]::SizeOf($r))|Out-Null
  $w=$r.Right-$r.Left; $hg=$r.Bottom-$r.Top
  $b=New-Object System.Drawing.Bitmap $w,$hg
  $g=[System.Drawing.Graphics]::FromImage($b)
  $g.CopyFromScreen($r.Left,$r.Top,0,0,(New-Object System.Drawing.Size($w,$hg)))
  $g.Dispose(); $b.Save($pfad,[System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
  "  $pfad"
}

if (Test-Path $cfg) { Copy-Item $cfg $bak -Force }
'{"DeviceName":"GAME-PC","Pin":null,"AudioOnly":false,"Language":"de-DE","LastConnectedDevice":null}' |
  Out-File $cfg -Encoding utf8
Get-Process AirPlayReceiver.App,uxplay,mDNSResponder -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$p=$null
try {
  $p = Start-Process $exe -PassThru
  $h=[IntPtr]::Zero
  for($i=0;$i -lt 40 -and $h -eq [IntPtr]::Zero;$i++){ Start-Sleep -Milliseconds 300; $p.Refresh(); $h=$p.MainWindowHandle }
  if($h -eq [IntPtr]::Zero){ throw 'Kein Fenster' }
  [G]::MoveWindow($h,60,40,1100,800,$true)|Out-Null
  [G]::SetForegroundWindow($h)|Out-Null
  Start-Sleep -Milliseconds 1500

  $win=$AE::FromHandle($h)
  Klick (ById $win 'MoreButton'); Start-Sleep -Milliseconds 900
  $item = ById $AE::RootElement 'MenuSettings'
  if(-not $item){ $item = ByName $AE::RootElement 'Einstellungen…' }
  Klick $item; Start-Sleep -Milliseconds 1800

  # Auswahlliste aufklappen und "Sehr gross" waehlen
  $win=$AE::FromHandle($h)
  $combo = WarteAuf { ById ($AE::FromHandle($h)) 'TextSizeCombo' }
  if(-not $combo){ throw 'TextSizeCombo nicht gefunden' }
  "  Auswahlliste gefunden, aktuell: '$($combo.Current.Name)'"
  Klick $combo; Start-Sleep -Milliseconds 900
  $ziel = WarteAuf { ByName $AE::RootElement ('Sehr gro'+[char]0x00DF) }
  if(-not $ziel){ throw 'Eintrag "Sehr gross" nicht gefunden' }
  Klick $ziel; Start-Sleep -Milliseconds 700

  [G]::SetCursorPos(950,760)|Out-Null; Start-Sleep -Milliseconds 400
  Foto $h "$out\groesse-01-dialog.png"

  Klick (ByName $AE::FromHandle($h) 'Speichern')
  Start-Sleep -Milliseconds 1800
  [G]::SetCursorPos(600,650)|Out-Null; Start-Sleep -Milliseconds 600
  Foto $h "$out\groesse-02-sehr-gross.png"
}
finally {
  Get-Process AirPlayReceiver.App,uxplay,mDNSResponder -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Milliseconds 500
}

""
"Gespeichert:"
(Get-Content $cfg -Raw).Trim()
if (Test-Path $bak) { Copy-Item $bak $cfg -Force; ""; "settings.json zurueckgestellt" }
