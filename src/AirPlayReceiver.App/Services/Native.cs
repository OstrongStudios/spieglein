using System;
using System.Runtime.InteropServices;

namespace AirPlayReceiver.App.Services;

/// <summary>
/// Win32-Interop, das wir fuer das Einbetten eines fremden Toplevel-Fensters
/// (uxplay-Videosink) als Child-Window unter unsere WinUI-App brauchen.
/// </summary>
internal static class Native
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    public const int GWL_STYLE   = -16;
    public const int GWL_EXSTYLE = -20;

    public const long WS_CAPTION      = 0x00C00000L;
    public const long WS_THICKFRAME   = 0x00040000L;
    public const long WS_MINIMIZEBOX  = 0x00020000L;
    public const long WS_MAXIMIZEBOX  = 0x00010000L;
    public const long WS_SYSMENU      = 0x00080000L;
    public const long WS_POPUP        = unchecked((long)0x80000000L);
    public const long WS_CHILD        = 0x40000000L;
    public const long WS_VISIBLE      = 0x10000000L;
    public const long WS_CLIPSIBLINGS = 0x04000000L;
    public const long WS_DLGFRAME     = 0x00400000L;
    public const long WS_BORDER       = 0x00800000L;

    public const long WS_EX_APPWINDOW   = 0x00040000L;
    public const long WS_EX_WINDOWEDGE  = 0x00000100L;
    public const long WS_EX_CLIENTEDGE  = 0x00000200L;
    public const long WS_EX_DLGMODALFRAME = 0x00000001L;
    public const long WS_EX_STATICEDGE  = 0x00020000L;

    public const uint SWP_NOZORDER       = 0x0004;
    public const uint SWP_NOACTIVATE     = 0x0010;
    public const uint SWP_FRAMECHANGED   = 0x0020;
    public const uint SWP_ASYNCWINDOWPOS = 0x4000;

    public const uint GW_OWNER = 4;
}
