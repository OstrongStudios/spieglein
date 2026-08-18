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
    Ready,
    Streaming,
    Error,
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

    public AppSettings Settings { get; set; } = new();

    private Process? _process;
    private Process? _mdnsProcess;
    private StreamWriter? _logWriter;
    private volatile bool _disposed;

    public UxPlayState State { get; private set; } = UxPlayState.Stopped;
    public string? LastError { get; private set; }
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

            Volatile.Write(ref _process, proc);
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            LastError = null;
            SetState(UxPlayState.Ready);
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _process, null);
            try { proc?.Dispose(); } catch { }
            LastError = ex.Message;
            WriteLog($"FEHLER beim Start: {ex}");
            SetState(UxPlayState.Error);
        }
    }

    private async Task StopCoreAsync()
    {
        // Felder zuerst leeren: ein danach eintreffendes Exited-Event erkennt an
        // der Identitaetspruefung, dass es veraltet ist, und tut nichts mehr.
        var ux   = Interlocked.Exchange(ref _process, null);
        var mdns = Interlocked.Exchange(ref _mdnsProcess, null);

        await KillAsync(ux).ConfigureAwait(false);
        await KillAsync(mdns).ConfigureAwait(false);

        ConnectedDevice = null;
        CloseLog();
        SetState(UxPlayState.Stopped);
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

            if (line.Contains("Connection request from", StringComparison.OrdinalIgnoreCase))
            {
                var match = _deviceRegex.Match(line);
                if (match.Success)
                {
                    ConnectedDevice = match.Groups[1].Value.Trim();
                    DeviceConnected?.Invoke(this, ConnectedDevice);
                }
                SetState(UxPlayState.Streaming);
            }
            else if (line.Contains("connection closed", StringComparison.OrdinalIgnoreCase))
            {
                SetState(UxPlayState.Ready);
            }
        }
        catch (Exception ex)
        {
            try { WriteLog($"FEHLER in HandleOutput: {ex}"); } catch { }
        }
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

            WriteLog($"--- uxplay beendet, exit={code} ---");

            if (code != 0)
            {
                string[] tail;
                lock (_recentLines) { tail = _recentLines.ToArray(); }
                var diag = tail.Length == 0
                    ? $"uxplay.exe beendet mit Exit-Code {code}. Siehe Log: {_logPath}"
                    : $"uxplay.exe beendet (Exit {code}). Letzte Meldungen:\n" +
                      string.Join("\n", tail.TakeLast(5));
                LastError = diag;
            }

            SetState(code == 0 ? UxPlayState.Stopped : UxPlayState.Error);
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
