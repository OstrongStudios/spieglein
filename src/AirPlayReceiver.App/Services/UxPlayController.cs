using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

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

    private readonly string _uxplayPath;
    private readonly string _logPath;
    private readonly object _logLock = new();
    private readonly Queue<string> _recentLines = new();

    public AppSettings Settings { get; set; } = new();

    private Process? _process;
    private Process? _mdnsProcess;
    private StreamWriter? _logWriter;

    public UxPlayState State { get; private set; } = UxPlayState.Stopped;
    public string? LastError { get; private set; }
    public string LogPath => _logPath;
    public int? UxPlayProcessId => (_process is { HasExited: false }) ? _process.Id : (int?)null;
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

    public void Start()
    {
        if (_process is { HasExited: false }) return;

        if (!File.Exists(_uxplayPath))
        {
            LastError = $"uxplay.exe nicht gefunden: {_uxplayPath}";
            SetState(UxPlayState.Error);
            return;
        }

        try
        {
            var uxplayDir = Path.GetDirectoryName(_uxplayPath)!;
            var pluginDir = Path.Combine(uxplayDir, "gstreamer-1.0");
            var dataDir   = Path.GetDirectoryName(_logPath)!;
            var registry  = Path.Combine(dataDir, "gst-registry.bin");

            OpenLog();
            _recentLines.Clear();

            // Eigenen mDNSResponder.exe -server starten, falls noch kein
            // Bonjour-Daemon laeuft. Bietet den Named-Pipe-Endpoint, an den
            // dnssd.dll im uxplay-Prozess connectet.
            StartMdnsResponderIfNeeded(uxplayDir);

            _process = new Process
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
            _process.StartInfo.Environment["GST_PLUGIN_PATH"]        = pluginDir;
            _process.StartInfo.Environment["GST_PLUGIN_SYSTEM_PATH"] = pluginDir;
            _process.StartInfo.Environment["GST_REGISTRY"]           = registry;
            var sysRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
            _process.StartInfo.Environment["PATH"] = $"{uxplayDir};{sysRoot};{Path.GetDirectoryName(sysRoot)}";

            _process.OutputDataReceived += (_, e) => HandleOutput(e.Data, isErr: false);
            _process.ErrorDataReceived  += (_, e) => HandleOutput(e.Data, isErr: true);
            _process.Exited             += OnExited;

            WriteLog($"--- starte uxplay ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) ---");
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            LastError = null;
            SetState(UxPlayState.Ready);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            WriteLog($"FEHLER beim Start: {ex}");
            SetState(UxPlayState.Error);
        }
    }

    public void Stop()
    {
        // uxplay killen
        if (_process is not null && !_process.HasExited)
        {
            try { _process.Kill(entireProcessTree: true); _process.WaitForExit(3000); } catch { }
        }
        _process?.Dispose();
        _process = null;

        // mDNSResponder killen
        if (_mdnsProcess is not null && !_mdnsProcess.HasExited)
        {
            try { _mdnsProcess.Kill(entireProcessTree: true); _mdnsProcess.WaitForExit(3000); } catch { }
        }
        _mdnsProcess?.Dispose();
        _mdnsProcess = null;

        CloseLog();
        SetState(UxPlayState.Stopped);
    }

    private void StartMdnsResponderIfNeeded(string uxplayDir)
    {
        // Vorherige Instanz aufraeumen, falls Start() schon einmal versucht wurde.
        if (_mdnsProcess is not null)
        {
            try { if (!_mdnsProcess.HasExited) _mdnsProcess.Kill(true); } catch { }
            _mdnsProcess.Dispose();
            _mdnsProcess = null;
        }

        // Falls Apples Bonjour-Service oder eine andere mDNSResponder-Instanz
        // schon laeuft, nichts tun — dnssd.dll connectet dort.
        if (Process.GetProcessesByName("mDNSResponder").Length > 0)
        {
            WriteLog("mDNSResponder laeuft bereits, eigener Daemon wird nicht gestartet.");
            return;
        }

        var mdnsPath = Path.Combine(uxplayDir, "mDNSResponder.exe");
        if (!File.Exists(mdnsPath))
        {
            WriteLog($"mDNSResponder.exe nicht gefunden ({mdnsPath}) — AirPlay-Discovery wird wahrscheinlich fehlschlagen.");
            return;
        }

        try
        {
            _mdnsProcess = new Process
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
            _mdnsProcess.OutputDataReceived += (_, e) => { if (e.Data is not null) WriteLog("[mdns] " + e.Data); };
            _mdnsProcess.ErrorDataReceived  += (_, e) => { if (e.Data is not null) WriteLog("[mdns err] " + e.Data); };

            WriteLog("--- starte mDNSResponder -server ---");
            _mdnsProcess.Start();
            _mdnsProcess.BeginOutputReadLine();
            _mdnsProcess.BeginErrorReadLine();

            // Warten bis Named Pipe bereit ist (~1s reicht erfahrungsgemaess).
            System.Threading.Thread.Sleep(1500);
        }
        catch (Exception ex)
        {
            WriteLog($"FEHLER beim Start mDNSResponder: {ex.Message}");
            try { _mdnsProcess?.Dispose(); } catch { }
            _mdnsProcess = null;
        }
    }

    // "connection request from iPhone von Mathias (iPhone14,5) with deviceID = ..."
    private static readonly System.Text.RegularExpressions.Regex _deviceRegex =
        new(@"connection request from (.+?) \(", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private void HandleOutput(string? line, bool isErr)
    {
        if (string.IsNullOrEmpty(line)) return;

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

    private void OnExited(object? sender, EventArgs e)
    {
        var code = _process?.ExitCode ?? -1;
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

    private void OpenLog()
    {
        lock (_logLock)
        {
            _logWriter?.Dispose();
            _logWriter = new StreamWriter(_logPath, append: true) { AutoFlush = true };
        }
    }

    private void CloseLog()
    {
        lock (_logLock)
        {
            _logWriter?.Dispose();
            _logWriter = null;
        }
    }

    private void WriteLog(string line)
    {
        lock (_logLock) { _logWriter?.WriteLine(line); }
    }

    private void SetState(UxPlayState newState)
    {
        if (State == newState) return;
        State = newState;
        StateChanged?.Invoke(this, newState);
    }

    public void Dispose() => Stop();

    private string BuildArguments()
    {
        // -vs autovideosink: Standard. -vs 0 = audio only (kein Video-Fenster).
        // -nh: kein "@hostname"-Suffix am AirPlay-Namen.
        // -n <name>: Geraetename.
        // -pinxxxx: 4-stelliger statischer Pincode.
        var args = new System.Text.StringBuilder();
        args.Append(Settings.AudioOnly ? "-vs 0" : "-vs autovideosink");
        if (!string.IsNullOrWhiteSpace(Settings.DeviceName))
        {
            args.Append(" -nh -n \"").Append(Settings.DeviceName).Append('"');
        }
        if (!string.IsNullOrWhiteSpace(Settings.Pin) && Settings.Pin.Length == 4
            && Settings.Pin.All(char.IsDigit))
        {
            args.Append(" -pin ").Append(Settings.Pin);
        }
        return args.ToString();
    }
}
