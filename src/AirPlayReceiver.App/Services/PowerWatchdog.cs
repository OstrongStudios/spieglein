using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AirPlayReceiver.App.Services;

/// <summary>
/// Reagiert auf Windows-Sleep/Wake. Bei Suspend wird der UxPlayController
/// gestoppt; bei Resume nach kurzem Delay wieder gestartet, falls vor
/// dem Sleep gestreamt wurde. Verhindert haengende AirPlay-Sessions nach
/// dem Aufwachen.
/// </summary>
public sealed class PowerWatchdog : IDisposable
{
    /// <summary>
    /// Obergrenze fuers Aufraeumen im Suspend-Handler. Der laeuft auf dem
    /// SystemEvents-Thread, und Windows wartet auf dessen Rueckkehr, bevor es
    /// den Rechner schlafen legt. Frueher konnte das bis zu 8 s dauern.
    /// </summary>
    private static readonly TimeSpan SuspendBudget = TimeSpan.FromSeconds(2);

    private readonly UxPlayController _controller;
    private readonly TimeSpan _resumeDelay = TimeSpan.FromSeconds(3);
    private bool _wasRunningBeforeSuspend;
    private CancellationTokenSource? _resumeCts;
    private bool _disposed;

    public PowerWatchdog(UxPlayController controller)
    {
        _controller = controller;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                _wasRunningBeforeSuspend = _controller.State != UxPlayState.Stopped &&
                                           _controller.State != UxPlayState.Error;
                if (_wasRunningBeforeSuspend)
                {
                    // Bewusst mit hartem Zeitbudget: lieber ein nicht ganz
                    // abgeraeumter Kindprozess als ein Rechner, der beim
                    // Zuklappen sekundenlang nicht einschlaeft.
                    try { _controller.StopAsync().Wait(SuspendBudget); } catch { }
                }
                break;

            case PowerModes.Resume:
                if (_wasRunningBeforeSuspend)
                {
                    var previous = _resumeCts;
                    _resumeCts = new CancellationTokenSource();
                    previous?.Cancel();
                    previous?.Dispose();
                    _ = ResumeAsync(_resumeCts.Token);
                }
                _wasRunningBeforeSuspend = false;
                break;
        }
    }

    private async Task ResumeAsync(CancellationToken token)
    {
        try
        {
            // Netzwerk braucht ein paar Sekunden nach Wake. Kurz warten,
            // dann uxplay + mDNSResponder neu starten.
            await Task.Delay(_resumeDelay, token).ConfigureAwait(false);
            if (token.IsCancellationRequested || _disposed) return;
            await _controller.StartAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* Dispose oder erneutes Resume */ }
        catch { /* Start meldet Fehler selbst ueber den Controller-Zustand */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        try { _resumeCts?.Cancel(); } catch { }
        try { _resumeCts?.Dispose(); } catch { }
        _resumeCts = null;
    }
}
