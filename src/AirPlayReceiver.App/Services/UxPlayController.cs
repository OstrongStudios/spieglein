using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AirPlayReceiver.App.Services;

public enum UxPlayState
{
    Stopped,
    /// <summary>
    /// uxplay laeuft, hat seinen Server-Socket aber noch nicht gemeldet.
    /// Erst danach darf die UI "Bereit" behaupten — vorher waere das eine
    /// unbelegte Zusicherung, die bei blockierter Firewall schlicht falsch ist.
    /// </summary>
    Starting,
    Ready,
    Streaming,
    Error,
}

/// <summary>
/// Klassifizierte Fehlerursache aus dem uxplay-Output. Die Zuordnung zu einem
/// lokalisierten Text passiert in der UI, nicht hier.
/// </summary>
public enum UxPlayFault
{
    None,
    /// <summary>mDNS/Bonjour nicht erreichbar — praktisch immer die Firewall.</summary>
    DiscoveryBlocked,
    /// <summary>Ein anderes Geraet im Netz benutzt denselben Namen.</summary>
    NameConflict,
    /// <summary>Ein benoetigter Port ist belegt (haeufig ein verwaister uxplay).</summary>
    PortBusy,
    /// <summary>Verbindung zum Client waehrend des Streams verloren.</summary>
    NetworkDropped,
    /// <summary>Sonstiger Fehler — Rohtext steht in LastError.</summary>
    Generic,
}

public sealed class UxPlayController : IDisposable
{
    private const int RecentLineLimit = 50;

    /// <summary>Wartezeit auf einen gekillten Kindprozess, bevor wir aufgeben.</summary>
    private static readonly TimeSpan KillTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Anlaufzeit fuer den mDNS-Daemon, bis seine Named Pipe steht.</summary>
    private static readonly TimeSpan MdnsWarmup = TimeSpan.FromMilliseconds(1500);

    private readonly string _uxplayPath;
    private readonly string _logPath;
    private readonly object _logLock = new();
    private readonly Queue<string> _recentLines = new();

    /// <summary>
    /// Serialisiert Start/Stop/Restart. Aufrufer sind der UI-Thread (Button, Menue),
    /// der SystemEvents-Thread und der Threadpool (PowerWatchdog). Ohne dieses Gate
    /// ueberlappen sich die Zugriffe auf _process/_mdnsProcess und es entstehen
    /// doppelte Instanzen bzw. Zugriffe auf bereits disposete Process-Objekte.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Bindet die Kindprozesse an unser Prozessleben. Siehe <see cref="ProcessJob"/>.
    /// </summary>
    private readonly ProcessJob _job = new();

    public AppSettings Settings { get; set; } = new();

    private Process? _process;
    private Process? _mdnsProcess;
    private StreamWriter? _logWriter;
    private volatile bool _disposed;

    /// <summary>true, sobald uxplay "Initialized server socket(s)" gemeldet hat.</summary>
    private volatile bool _serverReady;

    /// <summary>Die Waisensuche laeuft nur beim ersten Start eines Programmlaufs.</summary>
    private bool _leftoversChecked;

    /// <summary>Bricht die Startueberwachung ab, wenn vorher gestoppt wird.</summary>
    private CancellationTokenSource? _startupWatch;

    /// <summary>
    /// Frist, in der uxplay seinen Server-Socket gemeldet haben muss.
    ///
    /// Bewusst sehr grosszuegig. Beim ersten Start nach einem Update baut GStreamer
    /// seine Plugin-Registry neu auf (~1,6 MB, rund 250 Plugins), weil sich der
    /// Paketpfad mit jeder Version aendert. Gemessen auf einem schnellen NVMe-PC:
    ///   warm                      2,7 s
    ///   Registry geloescht        5,2 s
    ///   erster Start nach Update 13,3 s
    /// Auf aelterer Hardware mit HDD entsprechend ein Vielfaches davon. Eine knappe
    /// Frist wuerde dem Nutzer nach jedem Store-Update faelschlich die Firewall
    /// vorwerfen — schlimmer als gar keine Meldung.
    ///
    /// Dieser Watchdog ist nur das letzte Netz. Die echten Fehlerfaelle erkennt der
    /// Parser in <see cref="ClassifyFault"/> binnen Millisekunden, und ein vorzeitiges
    /// Prozessende faengt <see cref="OnExited"/> ab.
    /// </summary>
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(120);

    public UxPlayState State { get; private set; } = UxPlayState.Stopped;
    public string? LastError { get; private set; }

    /// <summary>Klassifizierte Ursache zum aktuellen Fehler- bzw. Warnzustand.</summary>
    public UxPlayFault Fault { get; private set; } = UxPlayFault.None;

    public string LogPath => _logPath;

    /// <summary>
    /// PID des laufenden uxplay-Prozesses, sonst null. Liest das Feld bewusst nur
    /// einmal — ein paralleles Stop() darf hier keine NullReferenceException
    /// ausloesen (frueherer TOCTOU-Fehler).
    /// </summary>
    public int? UxPlayProcessId
    {
        get
        {
            var p = Volatile.Read(ref _process);
            if (p is null) return null;
            try { return p.HasExited ? null : p.Id; }
            catch { return null; }
        }
    }

    /// <summary>Name des aktuell verbundenen iOS-Geraets (z. B. "iPhone von Mathias"). Null wenn keiner.</summary>
    public string? ConnectedDevice { get; private set; }

    /// <summary>Wird gefeuert, sobald ein neuer Geraete-Name aus dem uxplay-Output geparst wurde.</summary>
    public event EventHandler<string>? DeviceConnected;

    public event EventHandler<UxPlayState>? StateChanged;

    public UxPlayController(string uxplayPath)
    {
        _uxplayPath = uxplayPath;
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AirPlayReceiver");
        Directory.CreateDirectory(dataDir);
        _logPath = Path.Combine(dataDir, "uxplay.log");
    }

    // ------------------------------------------------------------------
    // Oeffentliche API — alles asynchron, damit kein Aufrufer den UI-Thread
    // blockiert. Die frueheren synchronen Start()/Stop() haben je nach Pfad
    // 2-16 Sekunden blockiert und Windows-Hang-Meldungen erzeugt.
    // ------------------------------------------------------------------

    public async Task StartAsync()
    {
        if (_disposed) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await StartCoreAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task StopAsync()
    {
        if (_disposed) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Stop + Start unter einem einzigen Gate-Durchlauf. Wichtig fuer den
    /// Einstellungsdialog: dazwischen darf kein anderer Aufrufer starten.
    /// </summary>
    public async Task RestartAsync()
    {
        if (_disposed) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
            await StartCoreAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    // ------------------------------------------------------------------
    // Implementierung — nur unter gehaltenem _gate aufrufen.
    // ------------------------------------------------------------------

    private async Task StartCoreAsync()
    {
        var running = Volatile.Read(ref _process);
        if (running is not null)
        {
            try { if (!running.HasExited) return; }
            catch { /* disposed — unten neu aufsetzen */ }
        }

        if (!File.Exists(_uxplayPath))
        {
            LastError = $"uxplay.exe nicht gefunden: {_uxplayPath}";
            SetState(UxPlayState.Error);
            return;
        }

        Process? proc = null;
        try
        {
            var uxplayDir = Path.GetDirectoryName(_uxplayPath)!;
            var pluginDir = Path.Combine(uxplayDir, "gstreamer-1.0");
            var dataDir   = Path.GetDirectoryName(_logPath)!;
            var registry  = Path.Combine(dataDir, "gst-registry.bin");

            OpenLog();
            lock (_recentLines) { _recentLines.Clear(); }
            _serverReady = false;
            Fault = UxPlayFault.None;

            if (!_job.IsValid)
                WriteLog("WARNUNG: Job-Objekt nicht verfuegbar — Kindprozesse koennen einen Absturz ueberleben.");

            // Reste aus einem frueheren unsauberen Ende beseitigen. Betrifft
            // ausschliesslich Prozesse, die aus UNSEREM Programmverzeichnis laufen —
            // ein fremder Bonjour-Dienst von Apple bleibt unangetastet.
            //
            // Nur einmal pro Programmlauf: Waisen kann es nur vor dem ersten Start
            // geben, danach haelt das Job-Objekt alles zusammen. Der Scan kostet
            // zwei komplette Prozesslisten-Durchlaeufe, die sparen wir uns.
            if (!_leftoversChecked)
            {
                _leftoversChecked = true;
                KillLeftoversFrom(uxplayDir);
            }

            // Eigenen mDNSResponder.exe -server starten, falls noch kein
            // Bonjour-Daemon laeuft. Bietet den Named-Pipe-Endpoint, an den
            // dnssd.dll im uxplay-Prozess connectet.
            await StartMdnsResponderIfNeededAsync(uxplayDir).ConfigureAwait(false);

            proc = new Process
            {
                StartInfo =
                {
                    FileName = _uxplayPath,
                    Arguments = BuildArguments(),
                    // CreateNoWindow = true: keine Konsole fuer uxplay sichtbar.
                    // Das spaeter vom Videosink erzeugte GUI-Fenster wird per
                    // VideoEmbedder als Child unter unsere App reparented.
                    CreateNoWindow         = true,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    WorkingDirectory       = uxplayDir,
                },
                EnableRaisingEvents = true,
            };

            // GStreamer-Plugins gebundelt unter uxplay/gstreamer-1.0/.
            // Defaults ueberschreiben, damit kein evtl. installiertes System-GStreamer mitspricht.
            proc.StartInfo.Environment["GST_PLUGIN_PATH"]        = pluginDir;
            proc.StartInfo.Environment["GST_PLUGIN_SYSTEM_PATH"] = pluginDir;
            proc.StartInfo.Environment["GST_REGISTRY"]           = registry;
            var sysRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
            proc.StartInfo.Environment["PATH"] = $"{uxplayDir};{sysRoot};{Path.GetDirectoryName(sysRoot)}";

            // Die Handler bekommen den Prozess mit, zu dem sie gehoeren. Damit kann
            // ein spaet eintreffendes Event eines laengst ersetzten Prozesses
            // erkannt und verworfen werden.
            var self = proc;
            proc.OutputDataReceived += (_, e) => HandleOutput(self, e.Data, isErr: false);
            proc.ErrorDataReceived  += (_, e) => HandleOutput(self, e.Data, isErr: true);
            proc.Exited             += OnExited;

            WriteLog($"--- starte uxplay ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) ---");
            WriteLog($"    Argumente: {proc.StartInfo.Arguments}");

            LastError = null;
            // Bewusst NICHT Ready: erst wenn uxplay seinen Server-Socket meldet,
            // ist die Zusage "bereit fuer Verbindungen" gedeckt.
            //
            // Der Zustandswechsel muss VOR BeginOutputReadLine passieren: uxplay
            // meldet "Initialized server socket(s)" teils wenige Millisekunden nach
            // dem Start. Stuende hier noch Stopped, liefe die Ready-Umschaltung in
            // HandleOutput ins Leere und die App bliebe dauerhaft in "Wird gestartet".
            SetState(UxPlayState.Starting);

            Volatile.Write(ref _process, proc);
            proc.Start();
            if (!_job.Assign(proc)) WriteLog("WARNUNG: uxplay konnte dem Job-Objekt nicht zugewiesen werden.");
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // Sicherheitsnetz, falls die Meldung die Umschaltung doch ueberholt hat.
            if (_serverReady && State == UxPlayState.Starting) SetState(UxPlayState.Ready);
            else StartStartupWatchdog(proc);
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _process, null);
            try { proc?.Dispose(); } catch { }
            LastError = ex.Message;
            Fault = UxPlayFault.Generic;
            WriteLog($"FEHLER beim Start: {ex}");
            SetState(UxPlayState.Error);
        }
    }

    /// <summary>
    /// Schlaegt Alarm, wenn uxplay innerhalb der Frist keinen Server-Socket meldet.
    /// Frueher blieb die UI in so einem Fall dauerhaft auf gruen stehen.
    /// </summary>
    private void StartStartupWatchdog(Process owner)
    {
        _startupWatch?.Cancel();
        _startupWatch?.Dispose();
        _startupWatch = new CancellationTokenSource();
        var token = _startupWatch.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(StartupTimeout, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                if (_serverReady) return;
                // Gehoert der ueberwachte Prozess noch zum aktuellen Start?
                if (!ReferenceEquals(owner, Volatile.Read(ref _process))) return;
                if (State != UxPlayState.Starting) return;

                WriteLog($"WARNUNG: nach {StartupTimeout.TotalSeconds:0} s kein 'Initialized server socket(s)' — Start gilt als fehlgeschlagen.");
                Fault = UxPlayFault.DiscoveryBlocked;
                SetState(UxPlayState.Error);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { try { WriteLog($"FEHLER im Start-Watchdog: {ex}"); } catch { } }
        }, token);
    }

    private async Task StopCoreAsync()
    {
        // Felder zuerst leeren: ein danach eintreffendes Exited-Event erkennt an
        // der Identitaetspruefung, dass es veraltet ist, und tut nichts mehr.
        try { _startupWatch?.Cancel(); } catch { }

        var ux   = Interlocked.Exchange(ref _process, null);
        var mdns = Interlocked.Exchange(ref _mdnsProcess, null);

        await KillAsync(ux).ConfigureAwait(false);
        await KillAsync(mdns).ConfigureAwait(false);

        ConnectedDevice = null;
        _serverReady    = false;
        Fault           = UxPlayFault.None;
        // Erst den Zustandswechsel protokollieren, DANN das Log schliessen —
        // sonst fehlt ausgerechnet der letzte Eintrag einer Sitzung.
        SetState(UxPlayState.Stopped);
        CloseLog();
    }

    private async Task KillAsync(Process? p)
    {
        if (p is null) return;
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                using var cts = new CancellationTokenSource(KillTimeout);
                try { await p.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    WriteLog($"WARNUNG: Prozess {p.Id} hat nach {KillTimeout.TotalSeconds:0} s nicht beendet.");
                }
            }
        }
        catch (Exception ex)
        {
            WriteLog($"WARNUNG beim Beenden eines Kindprozesses: {ex.Message}");
        }
        finally
        {
            try { p.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// Synchroner Notausgang fuer den Fenster-Close-Pfad. Killt nur und wartet
    /// bewusst nicht — Kill ist TerminateProcess und wirkt sofort; Warten wuerde
    /// das Schliessen der App um Sekunden verzoegern.
    /// </summary>
    private void KillNoWait(Process? p)
    {
        if (p is null) return;
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
        try { p.Dispose(); } catch { }
    }

    /// <summary>
    /// Beendet uxplay-/mDNSResponder-Prozesse, die aus dem uebergebenen Verzeichnis
    /// stammen. Der Pfadvergleich ist die Sicherung: ein von Apple installierter
    /// Bonjour-Dienst laeuft aus System32 und wird dadurch nie angefasst.
    /// </summary>
    private void KillLeftoversFrom(string uxplayDir)
    {
        foreach (var name in new[] { "uxplay", "mDNSResponder" })
        {
            Process[] found;
            try { found = Process.GetProcessesByName(name); }
            catch { continue; }

            foreach (var p in found)
            {
                try
                {
                    string? path = null;
                    try { path = p.MainModule?.FileName; }
                    catch { /* fremder Prozess, kein Zugriff — dann erst recht nicht anfassen */ }

                    if (path is not null &&
                        path.StartsWith(uxplayDir, StringComparison.OrdinalIgnoreCase))
                    {
                        WriteLog($"Waise aus frueherem Lauf gefunden: {name} (PID {p.Id}) — wird beendet.");
                        try { p.Kill(entireProcessTree: true); } catch { }
                    }
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
        }
    }

    private async Task StartMdnsResponderIfNeededAsync(string uxplayDir)
    {
        // Vorherige Instanz aufraeumen, falls Start() schon einmal versucht wurde.
        var previous = Interlocked.Exchange(ref _mdnsProcess, null);
        await KillAsync(previous).ConfigureAwait(false);

        // Falls Apples Bonjour-Service oder eine andere mDNSResponder-Instanz
        // schon laeuft, nichts tun — dnssd.dll connectet dort.
        Process[] existing = Array.Empty<Process>();
        try { existing = Process.GetProcessesByName("mDNSResponder"); }
        catch { }
        try
        {
            if (existing.Length > 0)
            {
                WriteLog("mDNSResponder laeuft bereits, eigener Daemon wird nicht gestartet.");
                return;
            }
        }
        finally
        {
            // Process.GetProcessesByName liefert Objekte mit offenen Handles —
            // ohne Dispose leckt jeder Start ein Handle pro gefundenem Prozess.
            foreach (var e in existing) { try { e.Dispose(); } catch { } }
        }

        var mdnsPath = Path.Combine(uxplayDir, "mDNSResponder.exe");
        if (!File.Exists(mdnsPath))
        {
            WriteLog($"mDNSResponder.exe nicht gefunden ({mdnsPath}) — AirPlay-Discovery wird wahrscheinlich fehlschlagen.");
            return;
        }

        Process? mdns = null;
        try
        {
            mdns = new Process
            {
                StartInfo =
                {
                    FileName               = mdnsPath,
                    Arguments              = "-server -q",
                    CreateNoWindow         = true,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    WorkingDirectory       = uxplayDir,
                },
                EnableRaisingEvents = true,
            };
            mdns.OutputDataReceived += (_, e) => { if (e.Data is not null) WriteLog("[mdns] " + e.Data); };
            mdns.ErrorDataReceived  += (_, e) => { if (e.Data is not null) WriteLog("[mdns err] " + e.Data); };

            WriteLog("--- starte mDNSResponder -server ---");
            Volatile.Write(ref _mdnsProcess, mdns);
            mdns.Start();
            if (!_job.Assign(mdns)) WriteLog("WARNUNG: mDNSResponder konnte dem Job-Objekt nicht zugewiesen werden.");
            mdns.BeginOutputReadLine();
            mdns.BeginErrorReadLine();

            // Warten bis Named Pipe bereit ist. Frueher Thread.Sleep auf dem
            // UI-Thread — jetzt asynchron, blockiert niemanden mehr.
            await Task.Delay(MdnsWarmup).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            WriteLog($"FEHLER beim Start mDNSResponder: {ex.Message}");
            Volatile.Write(ref _mdnsProcess, null);
            try { mdns?.Dispose(); } catch { }
        }
    }

    // "connection request from iPhone von Mathias (iPhone14,5) with deviceID = ..."
    private static readonly System.Text.RegularExpressions.Regex _deviceRegex =
        new(@"connection request from (.+?) \(", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Laeuft auf einem Threadpool-Thread. Eine Exception hier wuerde ungefangen
    /// den gesamten Prozess beenden — daher komplett gekapselt.
    /// </summary>
    private void HandleOutput(Process owner, string? line, bool isErr)
    {
        try
        {
            if (string.IsNullOrEmpty(line)) return;
            // Ausgabe eines bereits ersetzten Prozesses ignorieren.
            if (!ReferenceEquals(owner, Volatile.Read(ref _process))) return;

            WriteLog(isErr ? $"[err] {line}" : line);
            lock (_recentLines)
            {
                _recentLines.Enqueue(line);
                while (_recentLines.Count > RecentLineLimit) _recentLines.Dequeue();
            }

            // Erfolgs-Marker: erst ab hier lauscht uxplay wirklich (lib/httpd.c:694).
            if (line.Contains("Initialized server socket", StringComparison.OrdinalIgnoreCase))
            {
                _serverReady = true;
                if (State == UxPlayState.Starting) SetState(UxPlayState.Ready);
                return;
            }

            if (line.Contains("Connection request from", StringComparison.OrdinalIgnoreCase))
            {
                var match = _deviceRegex.Match(line);
                if (match.Success)
                {
                    ConnectedDevice = match.Groups[1].Value.Trim();
                    DeviceConnected?.Invoke(this, ConnectedDevice);
                }
                Fault = UxPlayFault.None;
                SetState(UxPlayState.Streaming);
                return;
            }

            if (line.Contains("connection closed", StringComparison.OrdinalIgnoreCase))
            {
                ConnectedDevice = null;
                if (_serverReady) SetState(UxPlayState.Ready);
                return;
            }

            var fault = ClassifyFault(line);
            if (fault == UxPlayFault.None) return;

            if (fault == UxPlayFault.NetworkDropped)
            {
                // Nicht fatal: uxplay laeuft weiter und wartet auf eine neue Verbindung.
                // Der Nutzer soll aber erfahren, warum das Bild gerade stehengeblieben ist.
                Fault           = fault;
                ConnectedDevice = null;
                if (State == UxPlayState.Streaming) SetState(UxPlayState.Ready);
                return;
            }

            Fault     = fault;
            LastError = line.Trim();
            SetState(UxPlayState.Error);
        }
        catch (Exception ex)
        {
            try { WriteLog($"FEHLER in HandleOutput: {ex}"); } catch { }
        }
    }

    /// <summary>
    /// Ordnet eine uxplay-Ausgabezeile einer Fehlerursache zu. Die Rohtexte stammen
    /// aus build/uxplay-src/UxPlay/uxplay.cpp und lib/httpd.c — bei einem uxplay-Update
    /// bitte gegenpruefen.
    /// </summary>
    private static UxPlayFault ClassifyFault(string line)
    {
        // uxplay.cpp:1946 meldet einen ignorierten Fehlschlag als INFO — kein echter Fehler.
        if (line.Contains("ignoring because", StringComparison.OrdinalIgnoreCase))
            return UxPlayFault.None;

        if (line.Contains("No DNS-SD Server", StringComparison.OrdinalIgnoreCase)
         || line.Contains("kDNSServiceErr_Unknown", StringComparison.OrdinalIgnoreCase)
         || line.Contains("Could not initialize dnssd", StringComparison.OrdinalIgnoreCase)
         || line.Contains("dnssd_register_airplay failed", StringComparison.OrdinalIgnoreCase)
         || line.Contains("dnssd_register_raop failed", StringComparison.OrdinalIgnoreCase))
            return UxPlayFault.DiscoveryBlocked;

        if (line.Contains("kDNSServiceErr_NameConflict", StringComparison.OrdinalIgnoreCase))
            return UxPlayFault.NameConflict;

        if (line.Contains("Error initialising socket", StringComparison.OrdinalIgnoreCase)
         || line.Contains("Error listening to IPv4 socket", StringComparison.OrdinalIgnoreCase)
         || line.Contains("Error listening to IPv6 socket", StringComparison.OrdinalIgnoreCase)
         || line.Contains("using same network ports", StringComparison.OrdinalIgnoreCase))
            return UxPlayFault.PortBusy;

        if (line.Contains("lost connection with client", StringComparison.OrdinalIgnoreCase)
         || line.Contains("client may be offline", StringComparison.OrdinalIgnoreCase))
            return UxPlayFault.NetworkDropped;

        return UxPlayFault.None;
    }

    /// <summary>
    /// Laeuft im Threadpool-Wait-Callback. Der ist in der BCL NICHT mit try/catch
    /// umschlossen — eine Exception hier beendet den ganzen Prozess ohne Dialog.
    /// Deshalb: nur ueber sender arbeiten, Identitaet pruefen, alles kapseln.
    /// </summary>
    private void OnExited(object? sender, EventArgs e)
    {
        try
        {
            if (sender is not Process p) return;

            // Gehoert das Event noch zum aktuellen Prozess? Nach Stop()/Restart()
            // ist _process bereits null bzw. ein anderer Prozess — dann ist dieses
            // Event veraltet und darf den Zustand nicht mehr anfassen.
            if (!ReferenceEquals(p, Volatile.Read(ref _process))) return;

            int code;
            try { code = p.ExitCode; } catch { code = -1; }

            WriteLog($"--- uxplay beendet, exit={code}, serverReady={_serverReady}, fault={Fault} ---");

            // Der Exit-Code allein taugt NICHT als Erfolgskriterium: uxplay ruft bei
            // jedem Discovery-Fehler cleanup() auf, und cleanup() endet mit exit(0)
            // (uxplay.cpp:3310). Frueher zeigte die App in genau diesem Fall einfach
            // "AirPlay-Empfang aus" — ohne jede Fehlermeldung.
            bool failed = code != 0 || !_serverReady || Fault != UxPlayFault.None;

            if (failed)
            {
                if (Fault == UxPlayFault.None)
                {
                    // Beendet, ohne je zu lauschen, und ohne erkannte Meldung —
                    // mit Abstand haeufigste Ursache ist die blockierte Discovery.
                    Fault = _serverReady ? UxPlayFault.Generic : UxPlayFault.DiscoveryBlocked;
                }

                string[] tail;
                lock (_recentLines) { tail = _recentLines.ToArray(); }
                LastError = tail.Length == 0
                    ? $"uxplay.exe beendet mit Exit-Code {code}. Siehe Log: {_logPath}"
                    : $"uxplay.exe beendet (Exit {code}). Letzte Meldungen:\n" +
                      string.Join("\n", tail.TakeLast(5));

                SetState(UxPlayState.Error);
            }
            else
            {
                SetState(UxPlayState.Stopped);
            }
        }
        catch (Exception ex)
        {
            try { WriteLog($"FEHLER in OnExited: {ex}"); } catch { }
        }
    }

    private void OpenLog()
    {
        lock (_logLock)
        {
            _logWriter?.Dispose();
            try { _logWriter = new StreamWriter(_logPath, append: true) { AutoFlush = true }; }
            catch { _logWriter = null; }
        }
    }

    private void CloseLog()
    {
        lock (_logLock)
        {
            try { _logWriter?.Dispose(); } catch { }
            _logWriter = null;
        }
    }

    /// <summary>Erlaubt anderen Komponenten (VideoEmbedder), ins selbe Log zu schreiben.</summary>
    public void AppendLog(string line) => WriteLog(line);

    private void WriteLog(string line)
    {
        lock (_logLock)
        {
            try { _logWriter?.WriteLine(line); } catch { }
        }
    }

    private void SetState(UxPlayState newState)
    {
        if (State == newState) return;
        // Zustandswechsel protokollieren: bei Nutzerberichten ist die Abfolge das
        // Erste, was man wissen will — die Partner-Center-Telemetrie sagt nur "Unknown".
        WriteLog($"[state] {State} -> {newState} (thread {Environment.CurrentManagedThreadId})");
        State = newState;
        StateChanged?.Invoke(this, newState);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Bewusst ohne Gate und ohne Warten: dieser Pfad laeuft beim Schliessen
        // des Fensters auf dem UI-Thread und darf nicht blockieren.
        KillNoWait(Interlocked.Exchange(ref _process, null));
        KillNoWait(Interlocked.Exchange(ref _mdnsProcess, null));
        // Letzte Instanz: schliesst das Job-Handle und raeumt alles ab, was oben
        // wider Erwarten noch laeuft.
        _job.Dispose();
        CloseLog();
        // _gate wird bewusst NICHT disposed: ein noch laufender Hintergrund-Stop
        // koennte sonst beim WaitAsync eine ObjectDisposedException bekommen.
        // SemaphoreSlim ohne angeforderten WaitHandle braucht kein Dispose.
    }

    private string BuildArguments()
    {
        // -p: feste Legacy-Ports TCP 7100/7000/7001 und UDP 7011/6001/6000.
        //     Ohne -p vergibt uxplay ZUFAELLIGE Ports; damit greift keine
        //     Firewall-Regel und die UxPlay-Doku sagt ausdruecklich, dass es
        //     mit laufender Firewall dann nicht funktioniert. Muss zu den
        //     Regeln in Package.appxmanifest passen.
        // -vs autovideosink: Standard. -vs 0 = audio only (kein Video-Fenster).
        // -nh: kein "@hostname"-Suffix am AirPlay-Namen.
        // -n <name>: Geraetename.
        // -pin <1234>: 4-stelliger statischer Pincode (Leerzeichen ist Pflicht).
        var args = new System.Text.StringBuilder();
        args.Append("-p ");
        args.Append(Settings.AudioOnly ? "-vs 0" : "-vs autovideosink");

        var name = SanitizeDeviceName(Settings.DeviceName);
        if (!string.IsNullOrWhiteSpace(name))
        {
            args.Append(" -nh -n \"").Append(name).Append('"');
        }
        if (!string.IsNullOrWhiteSpace(Settings.Pin) && Settings.Pin.Length == 4
            && Settings.Pin.All(char.IsDigit))
        {
            args.Append(" -pin ").Append(Settings.Pin);
        }
        return args.ToString();
    }

    /// <summary>
    /// Der Geraetename landet in einem Kommandozeilen-String in Anfuehrungszeichen.
    /// Ein enthaltenes Anfuehrungszeichen oder ein abschliessender Backslash wuerde
    /// das Quoting zerlegen und uxplay mit unsinnigen Argumenten starten.
    /// </summary>
    private static string SanitizeDeviceName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var cleaned = new string(raw.Where(c => c != '"' && c != '\\' && !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length > 63 ? cleaned[..63] : cleaned;
    }
}
