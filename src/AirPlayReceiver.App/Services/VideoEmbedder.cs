using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace AirPlayReceiver.App.Services;

/// <summary>
/// Sucht ein Toplevel-Fenster eines gegebenen Prozesses (uxplay) per Polling und
/// reparented es als Child unter die WinUI-App. Border/Titlebar weg, Position
/// folgt einem XAML-Bereich (VideoHost).
/// </summary>
public sealed class VideoEmbedder
{
    private readonly IntPtr _appHwnd;
    private readonly DispatcherQueue _dispatcher;

    private DispatcherQueueTimer? _searchTimer;
    private uint _targetPid;
    private IntPtr _embedded = IntPtr.Zero;
    private SubclassProc? _childSubclassProc;            // GC-Anchor
    private readonly IntPtr _childSubclassId = new(0x4156); // beliebig, "AV"

    /// <summary>Optionale Protokollsenke; wird von MainWindow an das uxplay-Log gehaengt.</summary>
    public Action<string>? Log { get; set; }

    public event EventHandler? EmbeddedChanged;
    public event EventHandler? EscapePressed;
    public event EventHandler? FullscreenTogglePressed;

    public bool HasEmbedded => _embedded != IntPtr.Zero;

    public VideoEmbedder(IntPtr appHwnd, DispatcherQueue dispatcher)
    {
        _appHwnd = appHwnd;
        _dispatcher = dispatcher;
    }

    public void StartSearchFor(uint pid)
    {
        _targetPid = pid;
        _embedded = IntPtr.Zero;
        EmbeddedChanged?.Invoke(this, EventArgs.Empty);

        _searchTimer ??= _dispatcher.CreateTimer();
        _searchTimer.Interval = TimeSpan.FromMilliseconds(400);
        _searchTimer.Tick -= OnTick;
        _searchTimer.Tick += OnTick;
        if (!_searchTimer.IsRunning) _searchTimer.Start();
    }

    public void Stop()
    {
        _searchTimer?.Stop();
        if (_embedded == IntPtr.Zero) return;

        var child = _embedded;
        // Feld zuerst leeren: ApplyBounds/SetEmbeddedVisible sollen das Fenster
        // ab jetzt nicht mehr anfassen, egal was unten noch passiert.
        _embedded = IntPtr.Zero;

        if (Native.IsWindow(child))
        {
            if (_childSubclassProc is not null)
            {
                try { RemoveWindowSubclass(child, _childSubclassProc, _childSubclassId); } catch { }
            }

            // Kopplung loesen. Solange ein fremdes Fenster Child unseres Fensters
            // ist, haengen die Eingabewarteschlangen beider Threads aneinander:
            // blockiert der GStreamer-Thread in uxplay, blockiert unsere WinUI-
            // Nachrichtenschleife mit — obwohl bei uns kein eigener Stackframe
            // beteiligt ist. Genau das erzeugt Hangs ohne verwertbaren Stack.
            // Vorher ausblenden, damit das Fenster nicht kurz frei aufblitzt.
            try
            {
                Native.SetWindowPos(child, IntPtr.Zero, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER |
                    Native.SWP_NOACTIVATE | Native.SWP_HIDEWINDOW | Native.SWP_ASYNCWINDOWPOS);
                Native.SetParent(child, IntPtr.Zero);
            }
            catch { }
        }

        _childSubclassProc = null;
        EmbeddedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Schiebt das eingebettete Fenster auf die uebergebenen Client-Coords.</summary>
    public void ApplyBounds(int x, int y, int width, int height)
    {
        if (_embedded == IntPtr.Zero || width <= 0 || height <= 0) return;
        if (!Native.IsWindow(_embedded)) return;
        Native.SetWindowPos(_embedded, IntPtr.Zero, x, y, width, height,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
    }

    /// <summary>
    /// Blendet das eingebettete Videofenster aus/ein. Brauchen wir, um WinUI-Dialoge
    /// sichtbar zu machen — der Win32-Child rendert sonst ueber der Composition-Layer.
    /// </summary>
    public void SetEmbeddedVisible(bool visible)
    {
        if (_embedded == IntPtr.Zero) return;
        if (!Native.IsWindow(_embedded)) return;

        // ACHTUNG — hier NICHT experimentieren. Stand 1.0.4.0, funktioniert.
        //
        // Versucht und wieder verworfen, jeweils schlechter als das hier:
        //  - SetWindowPos mit SWP_SHOWWINDOW/SWP_HIDEWINDOW: blendet aus, holt aber
        //    nicht zuverlaessig zurueck.
        //  - ShowWindowAsync: gleiches Ergebnis wie ShowWindow, kein Gewinn.
        //  - Fenster aus dem Elternfenster herausschieben statt verstecken, und beim
        //    Zurueckholen die Breite kurz aendern, um ein WM_SIZE zu erzwingen:
        //    behebt das Schwarz NICHT und stoert zusaetzlich die Mirror-Verbindung.
        //    Im uxplay-Log erscheint dann "raop_rtp_mirror->running is no longer true";
        //    der Client verbindet zwar von selbst neu, aber der Stream reisst ab.
        //    Unterm Strich schlechter als der Zustand vorher.
        //
        // Bekannte Einschraenkung, die es auch schon vor 1.0.5.0 gab: Nach dem
        // Schliessen eines Dialogs bleibt die Videoflaeche schwarz, bis das iOS-Geraet
        // von sich aus ein neues Vollbild schickt. Der D3D-Videosink praesentiert nicht
        // von selbst neu. Das ist unschoen, aber harmlos — Ton und Verbindung laufen
        // durch. Eine Loesung dafuer gehoert in eine eigene Version und braucht
        // vermutlich einen anderen Videosink oder gst_video_overlay statt SetParent.
        ShowWindow(_embedded, visible ? SW_SHOWNA : SW_HIDE);
    }

    private const int SW_HIDE   = 0;
    private const int SW_SHOWNA = 8;   // anzeigen, ohne den Fokus zu stehlen

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>Zustand eines Fensters kompakt fuer das Log.</summary>
    private static string Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "(null)";
        if (!Native.IsWindow(hwnd)) return $"0x{hwnd.ToInt64():X} ZERSTOERT";
        var cls = new System.Text.StringBuilder(256);
        Native.GetClassName(hwnd, cls, cls.Capacity);
        bool vis = Native.IsWindowVisible(hwnd);
        long style = Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64();
        Native.GetWindowRect(hwnd, out var r);
        var parent = Native.GetParent(hwnd);
        return $"0x{hwnd.ToInt64():X} cls='{cls}' sichtbar={vis} " +
               $"WS_VISIBLE={(style & Native.WS_VISIBLE) != 0} WS_CHILD={(style & Native.WS_CHILD) != 0} " +
               $"rect=({r.Left},{r.Top})-({r.Right},{r.Bottom}) parent=0x{parent.ToInt64():X}";
    }

    /// <summary>
    /// Listet alle direkten Kindfenster unseres App-Fensters in Z-Reihenfolge auf.
    /// Entscheidend fuer die Frage, ob das Videofenster hinter der XAML-Insel liegt.
    /// </summary>
    private string DescribeZOrder()
    {
        var sb = new System.Text.StringBuilder();
        var child = Native.GetWindow(_appHwnd, Native.GW_CHILD);
        int i = 0;
        while (child != IntPtr.Zero && i < 20)
        {
            var marke = child == _embedded ? "  <== VIDEO" : "";
            sb.Append($"           {i}: {Describe(child)}{marke}\n");
            child = Native.GetWindow(child, Native.GW_HWNDNEXT);
            i++;
        }
        return sb.Length == 0 ? "           (keine Kindfenster)" : sb.ToString().TrimEnd();
    }


    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (_embedded != IntPtr.Zero) return;
        var hwnd = FindTopLevelWindowForProcess(_targetPid);
        if (hwnd == IntPtr.Zero) return;
        if (Embed(hwnd))
        {
            _embedded = hwnd;
            _searchTimer?.Stop();
            EmbeddedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static IntPtr FindTopLevelWindowForProcess(uint pid)
    {
        IntPtr found = IntPtr.Zero;
        Native.EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner != pid) return true;
            if (!Native.IsWindowVisible(hwnd)) return true;
            if (Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero) return true;
            found = hwnd;
            return false; // gefunden, EnumWindows abbrechen
        }, IntPtr.Zero);
        return found;
    }

    private bool Embed(IntPtr child)
    {
        // Style ueberschreiben: Toplevel-Dekoration weg, als Child markieren.
        long style = Native.GetWindowLongPtr(child, Native.GWL_STYLE).ToInt64();
        style &= ~(Native.WS_POPUP | Native.WS_CAPTION | Native.WS_THICKFRAME
                 | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX | Native.WS_SYSMENU
                 | Native.WS_DLGFRAME | Native.WS_BORDER);
        style |= Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPSIBLINGS;
        Native.SetWindowLongPtr(child, Native.GWL_STYLE, new IntPtr(style));

        long ex = Native.GetWindowLongPtr(child, Native.GWL_EXSTYLE).ToInt64();
        ex &= ~(Native.WS_EX_APPWINDOW | Native.WS_EX_WINDOWEDGE | Native.WS_EX_CLIENTEDGE
              | Native.WS_EX_DLGMODALFRAME | Native.WS_EX_STATICEDGE);
        Native.SetWindowLongPtr(child, Native.GWL_EXSTYLE, new IntPtr(ex));

        var prevParent = Native.SetParent(child, _appHwnd);
        if (prevParent == IntPtr.Zero) return false;

        // Style-Aenderung committen (Frame neu berechnen)
        Native.SetWindowPos(child, IntPtr.Zero, 0, 0, 0, 0,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);

        // Tastatur-Events am Child-Fenster abfangen (Esc, Alt+Enter).
        // Solange der Stream laeuft, hat dieses Fenster meist den Fokus.
        _childSubclassProc = ChildWndProc;
        SetWindowSubclass(child, _childSubclassProc, _childSubclassId, IntPtr.Zero);
        return true;
    }

    private const uint WM_KEYDOWN    = 0x0100;
    private const uint WM_SYSKEYDOWN = 0x0104;
    private const int  VK_ESCAPE     = 0x1B;
    private const int  VK_RETURN     = 0x0D;

    private IntPtr ChildWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
                                IntPtr idSubclass, IntPtr refData)
    {
        if (msg == WM_KEYDOWN && wParam.ToInt32() == VK_ESCAPE)
        {
            EscapePressed?.Invoke(this, EventArgs.Empty);
            return IntPtr.Zero;
        }
        if (msg == WM_SYSKEYDOWN && wParam.ToInt32() == VK_RETURN)
        {
            FullscreenTogglePressed?.Invoke(this, EventArgs.Empty);
            return IntPtr.Zero;
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
                                         IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("Comctl32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass,
                                                 IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("Comctl32.dll", CharSet = CharSet.Unicode)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass,
                                                    IntPtr uIdSubclass);

    [DllImport("Comctl32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
}
