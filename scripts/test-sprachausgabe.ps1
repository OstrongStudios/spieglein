# Liest aus, was die Bildschirmsprachausgabe an der Farbauswahl vorfindet.
# Frueher stand dort der Auswahlzustand als Hilfetext "1" bzw. "0" — vorgelesen
# wurde daraus "Violett, eins". Erwartet wird jetzt: Rolle Optionsfeld,
# echter Auswahlzustand, Gruppenname, kein Hilfetext.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;using System.Runtime.InteropServices;
public class S {
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int hg,bool r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint dx,uint dy,uint d,IntPtr e);
  public static void ClickAt(int x,int y){ SetCursorPos(x,y); System.Threading.Thread.Sleep(150);
    mouse_event(0x02,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(80); mouse_event(0x04,0,0,0,IntPtr.Zero); }
}
"@

$exe = 'D:\Spieglein\AirPlayReceiver\src\AirPlayReceiver.App\bin\Debug\net8.0-windows10.0.19041.0\win-x64\AirPlayReceiver.App.exe'
$cfg = "$env:LOCALAPPDATA\AirPlayReceiver\settings.json"
$bak = Join-Path $env:TEMP 'spieglein-settings-vor-uiatest.json'
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
function ById($r,[string]$id){ $r.FindFirst($TS::Descendants,(New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty,$id))) }
function ByName($r,[string]$n){ $r.FindFirst($TS::Descendants,(New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty,$n))) }
function Klick($el){ $r=$el.Current.BoundingRectangle; [S]::ClickAt([int]($r.X+$r.Width/2),[int]($r.Y+$r.Height/2)) }

if (Test-Path $cfg) { Copy-Item $cfg $bak -Force }
'{"DeviceName":"GAME-PC","Pin":null,"AudioOnly":false,"Language":"de-DE","LastConnectedDevice":null}' |
  Out-File $cfg -Encoding utf8
Get-Process AirPlayReceiver.App -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

$p=$null
try {
  $p = Start-Process $exe -PassThru
  $h=[IntPtr]::Zero
  for($i=0;$i -lt 40 -and $h -eq [IntPtr]::Zero;$i++){ Start-Sleep -Milliseconds 300; $p.Refresh(); $h=$p.MainWindowHandle }
  [S]::MoveWindow($h,60,40,1100,760,$true)|Out-Null; [S]::SetForegroundWindow($h)|Out-Null
  Start-Sleep -Milliseconds 1500

  $win=$AE::FromHandle($h)
  Klick (ById $win 'MoreButton'); Start-Sleep -Milliseconds 900
  $item = ById $AE::RootElement 'MenuSettings'
  if(-not $item){ $item = ByName $AE::RootElement ('Einstellungen'+[char]0x2026) }
  Klick $item; Start-Sleep -Milliseconds 1800

  $win=$AE::FromHandle($h)
  $alle = $win.FindAll($TS::Descendants,(New-Object System.Windows.Automation.PropertyCondition(
            $AE::ControlTypeProperty,[System.Windows.Automation.ControlType]::RadioButton)))
  "Gefundene Optionsfelder: $($alle.Count)"
  ""
  "{0,-12} {1,-14} {2,-12} {3}" -f 'Name','Rolle','Ausgewaehlt','Hilfetext'
  "-" * 58
  foreach($e in $alle){
    $c = $e.Current
    $sel = '?'
    try {
      $sp = $e.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
      $sel = $sp.Current.IsSelected
    } catch { $sel = 'kein Muster' }
    $help = if ([string]::IsNullOrEmpty($c.HelpText)) { '(leer)' } else { "'" + $c.HelpText + "'" }
    "{0,-12} {1,-14} {2,-12} {3}" -f $c.Name, $c.LocalizedControlType, $sel, $help
  }
  ""
  # Gruppenname: RadioButtons traegt den Header als Beschriftung
  $gruppe = ById $win 'ColorSchemeRow'
  if($gruppe){ "Gruppe: Name='$($gruppe.Current.Name)'  Rolle='$($gruppe.Current.LocalizedControlType)'" }
  else { "Gruppe: kein eigener UIA-Knoten" }
}
finally {
  Get-Process AirPlayReceiver.App -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Milliseconds 400
  if (Test-Path $bak) { Copy-Item $bak $cfg -Force }
}
