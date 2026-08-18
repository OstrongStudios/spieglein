using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AirPlayReceiver.App.Services;

/// <summary>
/// Bindet uxplay.exe und mDNSResponder.exe an ein Windows-Job-Objekt mit
/// JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE.
///
/// Warum: Kindprozesse ueberleben sonst jedes unsaubere Ende der App — Absturz
/// durch eine unbehandelte Exception, Abschuss im Task-Manager, harter Neustart.
/// Beim naechsten Start haelt der verwaiste uxplay dann TCP 7000, der neue
/// scheitert daran ("Is another instance running ... using same network ports?"),
/// und StartMdnsResponderIfNeeded findet die Waise und startet bewusst keinen
/// eigenen Daemon. Ergebnis: die App ist dauerhaft kaputt, bis der Nutzer sich
/// abmeldet.
///
/// Mit dem Job-Objekt raeumt Windows selbst auf, sobald unser letztes Handle
/// darauf geschlossen wird — und das passiert bei JEDER Art von Prozessende,
/// auch bei TerminateProcess.
/// </summary>
internal sealed class ProcessJob : IDisposable
{
    private IntPtr _handle = IntPtr.Zero;
    private bool _disposed;

    public bool IsValid => _handle != IntPtr.Zero;

    /// <summary>Legt das Job-Objekt an. Schlaegt das fehl, arbeitet die App wie bisher weiter.</summary>
    public ProcessJob()
    {
        try
        {
            _handle = CreateJobObject(IntPtr.Zero, null);
            if (_handle == IntPtr.Zero) return;

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

            int len = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr ptr = Marshal.AllocHGlobal(len);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                if (!SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, ptr, (uint)len))
                {
                    CloseHandle(_handle);
                    _handle = IntPtr.Zero;
                }
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        catch
        {
            _handle = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Haengt einen Prozess in den Job. Gibt false zurueck, wenn das nicht geht —
    /// dann bleibt es beim bisherigen Verhalten, die App laeuft trotzdem.
    /// </summary>
    public bool Assign(Process process)
    {
        if (_handle == IntPtr.Zero) return false;
        try
        {
            if (process.HasExited) return false;
            return AssignProcessToJobObject(_handle, process.Handle);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Schliesst das Handle -> Windows beendet alle noch laufenden Mitglieder.
        if (_handle != IntPtr.Zero)
        {
            try { CloseHandle(_handle); } catch { }
            _handle = IntPtr.Zero;
        }
    }

    // ----- P/Invoke -----

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    private const int  JobObjectExtendedLimitInformation  = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long   PerProcessUserTimeLimit;
        public long   PerJobUserTimeLimit;
        public uint   LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint   ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint   PriorityClass;
        public uint   SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass,
                                                       IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
