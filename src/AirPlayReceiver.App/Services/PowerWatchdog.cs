using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AirPlayReceiver.App.Services;

/// <summary>
/// Reagiert auf Windows-Sleep/Wake. Bei Suspend wird der UxPlayController
/// sauber gestoppt; bei Resume nach kurzem Delay wieder gestartet, falls vor
/// dem Sleep gestreamt wurde. Verhindert haengende AirPlay-Sessions nach
/// dem Aufwachen.
/// </summary>
public sealed class PowerWatchdog : IDisposable
{
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
                if (_wasRunningBeforeSuspend) _controller.Stop();
                break;

            case PowerModes.Resume:
                if (_wasRunningBeforeSuspend)
                {
                    _resumeCts?.Cancel();
                    _resumeCts = new CancellationTokenSource();
                    var token = _resumeCts.Token;
                    // Netzwerk braucht ein paar Sekunden nach Wake. Kurz warten,
                    // dann uxplay + mDNSResponder neu starten.
                    Task.Delay(_resumeDelay, token).ContinueWith(t =>
                    {
                        if (t.IsCanceled) return;
                        _controller.Start();
                    }, TaskScheduler.Default);
                }
                _wasRunningBeforeSuspend = false;
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _resumeCts?.Cancel();
        _resumeCts?.Dispose();
    }
}
