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
        if (_embedded != IntPtr.Zero)
        {
            if (_childSubclassProc is not null)
            {
                RemoveWindowSubclass(_embedded, _childSubclassProc, _childSubclassId);
                _childSubclassProc = null;
            }
            _embedded = IntPtr.Zero;
            EmbeddedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Schiebt das eingebettete Fenster auf die uebergebenen Client-Coords.</summary>
    public void ApplyBounds(int x, int y, int width, int height)
    {
        if (_embedded == IntPtr.Zero || width <= 0 || height <= 0) return;
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
        ShowWindow(_embedded, visible ? SW_SHOWNA : SW_HIDE);
    }

    private const int SW_HIDE    = 0;
    private const int SW_SHOWNA  = 8;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

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
