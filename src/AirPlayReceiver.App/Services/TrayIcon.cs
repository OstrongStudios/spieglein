using System;
using System.Runtime.InteropServices;

namespace AirPlayReceiver.App.Services;

/// <summary>
/// Schlanke Shell_NotifyIcon-Wrapper. Erzeugt ein Tray-Icon, subclasst das
/// Hauptfenster, um Klick/Rechtsklick-Events zu empfangen, und blendet bei
/// Rechtsklick ein natives Popup-Menue ein.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    public event EventHandler? LeftClicked;
    public event EventHandler? ToggleAirPlayRequested;
    public event EventHandler? QuitRequested;

    private const uint WM_TRAY_CALLBACK = 0x8001; // WM_USER + 1
    private const uint NIM_ADD     = 0x00;
    private const uint NIM_MODIFY  = 0x01;
    private const uint NIM_DELETE  = 0x02;
    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON    = 0x02;
    private const uint NIF_TIP     = 0x04;
    private const uint WM_LBUTTONUP   = 0x0202;
    private const uint WM_RBUTTONUP   = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;

    private const uint MF_STRING    = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint TPM_LEFTALIGN  = 0x0000;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD  = 0x0100;

    private const int MENU_SHOW    = 1;
    private const int MENU_TOGGLE  = 2;
    private const int MENU_QUIT    = 3;

    private readonly IntPtr _hwnd;
    private readonly SubclassProc _subclassProc;     // GC-anchor
    private readonly IntPtr _subclassId = new(0x4150); // beliebig, "AP"
    private NOTIFYICONDATA _nid;
    private IntPtr _hIcon = IntPtr.Zero;
    private string _showLabel = "Fenster anzeigen";
    private string _toggleLabel = "AirPlay starten/stoppen";
    private string _quitLabel = "Beenden";
    private bool _added;
    private bool _disposed;

    public TrayIcon(IntPtr hwnd, string tooltip, string iconPath)
    {
        _hwnd = hwnd;
        _hIcon = LoadImage(IntPtr.Zero, iconPath, 1 /*IMAGE_ICON*/, 0, 0,
                           0x10 /*LR_LOADFROMFILE*/ | 0x40 /*LR_DEFAULTSIZE*/);

        _nid = new NOTIFYICONDATA
        {
            cbSize           = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd             = _hwnd,
            uID              = 1,
            uFlags           = NIF_MESSAGE | NIF_TIP | (_hIcon != IntPtr.Zero ? NIF_ICON : 0),
            uCallbackMessage = WM_TRAY_CALLBACK,
            hIcon            = _hIcon,
            szTip            = tooltip,
        };

        _subclassProc = WndProc;
        SetWindowSubclass(_hwnd, _subclassProc, _subclassId, IntPtr.Zero);

        if (Shell_NotifyIcon(NIM_ADD, ref _nid))
            _added = true;
    }

    public void SetLabels(string show, string toggle, string quit)
    {
        _showLabel = show;
        _toggleLabel = toggle;
        _quitLabel = quit;
    }

    public void SetTooltip(string tip)
    {
        _nid.szTip = tip;
        _nid.uFlags = NIF_MESSAGE | NIF_TIP | (_hIcon != IntPtr.Zero ? NIF_ICON : 0);
        Shell_NotifyIcon(NIM_MODIFY, ref _nid);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_added) Shell_NotifyIcon(NIM_DELETE, ref _nid);
        RemoveWindowSubclass(_hwnd, _subclassProc, _subclassId);
        if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
        _hIcon = IntPtr.Zero;
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
                           IntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (msg == WM_TRAY_CALLBACK)
        {
            var ev = (uint)(lParam.ToInt64() & 0xFFFF);
            if (ev == WM_LBUTTONUP)
            {
                LeftClicked?.Invoke(this, EventArgs.Empty);
            }
            else if (ev == WM_RBUTTONUP || ev == WM_CONTEXTMENU)
            {
                ShowContextMenu();
            }
            return IntPtr.Zero;
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        GetCursorPos(out POINT pt);
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;

        AppendMenu(menu, MF_STRING,    (IntPtr)MENU_SHOW,   _showLabel);
        AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero,         null);
        AppendMenu(menu, MF_STRING,    (IntPtr)MENU_TOGGLE, _toggleLabel);
        AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero,         null);
        AppendMenu(menu, MF_STRING,    (IntPtr)MENU_QUIT,   _quitLabel);

        // SetForegroundWindow ist Pflicht, sonst schliesst sich das Menue sofort.
        SetForegroundWindow(_hwnd);

        int cmd = TrackPopupMenu(menu,
            TPM_LEFTALIGN | TPM_RIGHTBUTTON | TPM_RETURNCMD,
            pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);

        DestroyMenu(menu);
        // Dummy-Message, damit das Menue zuverlaessig schliesst (Microsoft-Empfehlung).
        PostMessage(_hwnd, 0x0000 /*WM_NULL*/, IntPtr.Zero, IntPtr.Zero);

        switch (cmd)
        {
            case MENU_SHOW:   LeftClicked?.Invoke(this, EventArgs.Empty); break;
            case MENU_TOGGLE: ToggleAirPlayRequested?.Invoke(this, EventArgs.Empty); break;
            case MENU_QUIT:   QuitRequested?.Invoke(this, EventArgs.Empty); break;
        }
    }

    // ----- P/Invoke -----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint   cbSize;
        public IntPtr hWnd;
        public uint   uID;
        public uint   uFlags;
        public uint   uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint   dwState;
        public uint   dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint   uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint   dwInfoFlags;
        public Guid   guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
                                         IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA pnid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint type,
                                           int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("Comctl32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass,
                                                 IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("Comctl32.dll", CharSet = CharSet.Unicode)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass,
                                                    IntPtr uIdSubclass);

    [DllImport("Comctl32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y,
                                             int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
