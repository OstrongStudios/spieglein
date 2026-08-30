# Prueft die Farbauswahl: Dialog oeffnen, zweites Schema waehlen, speichern,
# und nachsehen ob Knopf und Leiste die neue Farbe annehmen.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;using System.Runtime.InteropServices;
public class F {
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
$bak = Join-Path $env:TEMP 'spieglein-settings-vor-farbtest.json'

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
function Klick($el){ $r=$el.Current.BoundingRectangle; [F]::ClickAt([int]($r.X+$r.Width/2),[int]($r.Y+$r.Height/2)) }
function Foto($h,$pfad){
  $r=New-Object F+RECT
  [F]::DwmGetWindowAttribute($h,9,[ref]$r,[System.Runtime.InteropServices.Marshal]::SizeOf($r))|Out-Null
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

$p = $null
try {
  $p = Start-Process $exe -PassThru
  $h=[IntPtr]::Zero
  for($i=0;$i -lt 40 -and $h -eq [IntPtr]::Zero;$i++){ Start-Sleep -Milliseconds 300; $p.Refresh(); $h=$p.MainWindowHandle }
  if($h -eq [IntPtr]::Zero){ throw 'Kein Fenster' }
  [F]::MoveWindow($h,60,40,1100,760,$true)|Out-Null
  [F]::SetForegroundWindow($h)|Out-Null
  Start-Sleep -Milliseconds 1500

  # Vorher: Vorgabe Violett
  Add-Type -AssemblyName System.Drawing
  $win=$AE::FromHandle($h)
  Klick (ById $win 'MoreButton'); Start-Sleep -Milliseconds 900
  $item = ById $AE::RootElement 'MenuSettings'
  if(-not $item){ $item = ByName $AE::RootElement 'Einstellungen…' }
  Klick $item; Start-Sleep -Milliseconds 1800

  [F]::SetCursorPos(900,700)|Out-Null; Start-Sleep -Milliseconds 500
  Foto $h "$out\schema-01-dialog.png"

  # Zweite Farbflaeche = Tuerkis
  $win=$AE::FromHandle($h)
  # Ein nacktes StackPanel hat keinen UIA-Knoten - die Flaechen ueber ihren
  # AutomationProperties.Name suchen, den das Code-Behind aus den Strings setzt.
  $namen = 'Violett','Tuerkis','Blau','Gruen','Ziegelrot'
  $gefunden = @()
  foreach($n in 'Violett',[char]0x54+'ü'+'rkis','Blau','Gr'+[char]0x00FC+'n','Ziegelrot'){
    $e = ByName $win $n
    if($e){ $gefunden += $n }
  }
  "  Farbflaechen gefunden: $($gefunden.Count)  [$($gefunden -join ', ')]"
  $tuerkis = WarteAuf { ByName ($AE::FromHandle($h)) ('T'+[char]0x00FC+'rkis') }
  if(-not $tuerkis){ throw 'Farbflaeche Tuerkis nicht gefunden' }
  Klick $tuerkis
  Start-Sleep -Milliseconds 600
  Foto $h "$out\schema-02-tuerkis-gewaehlt.png"

  Klick (ByName $AE::FromHandle($h) 'Speichern')
  Start-Sleep -Milliseconds 1800
  [F]::SetCursorPos(600,600)|Out-Null; Start-Sleep -Milliseconds 600
  Foto $h "$out\schema-03-nach-wechsel.png"
}
finally {
  Get-Process AirPlayReceiver.App,uxplay,mDNSResponder -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Milliseconds 500
}

""
"Gespeichert in settings.json:"
(Get-Content $cfg -Raw).Trim()
""
Add-Type -AssemblyName System.Drawing
$b=[System.Drawing.Image]::FromFile("$out\schema-03-nach-wechsel.png")
$leiste=$b.GetPixel(400,65); $knopf=$b.GetPixel(975,65)
"Statusleiste gemessen: #{0:X2}{1:X2}{2:X2}   (Tuerkis dunkel erwartet #162E30)" -f $leiste.R,$leiste.G,$leiste.B
"Knopf gemessen       : #{0:X2}{1:X2}{2:X2}   (Tuerkis hell erwartet #4FD5DC)" -f $knopf.R,$knopf.G,$knopf.B
$b.Dispose()
if (Test-Path $bak) { Copy-Item $bak $cfg -Force; "settings.json zurueckgestellt" }
