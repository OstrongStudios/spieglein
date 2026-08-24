using System;
using System.IO;
using AirPlayReceiver.App.Services;
using AirPlayReceiver.App.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using WinRT.Interop;

namespace AirPlayReceiver.App;

public sealed partial class MainWindow : Window
{
    private readonly UxPlayController _controller;
    private readonly LocalizedStrings _strings;
    private readonly VideoEmbedder _embedder;
    private readonly AppWindow _appWindow;
    private readonly TrayIcon _tray;
    private readonly PowerWatchdog _watchdog;
    private AppSettings _settings = AppSettings.Load();
    private bool _isFullscreen;
    private bool _quitRequested;
    /// <summary>Sperre gegen ueberlappende Start/Stop-Vorgaenge (Button, Tray, Einstellungen).</summary>
    private bool _transitioning;

    /// <summary>
    /// Ueberwacht, ob nach dem Verbindungsaufbau tatsaechlich ein Videofenster
    /// erscheint. Siehe <see cref="StartVideoWatch"/>.
    /// </summary>
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _videoWatch;

    /// <summary>
    /// Frist, in der nach dem Wechsel auf "Aktive Verbindung" ein Videofenster
    /// aufgetaucht sein muss. Grosszuegig, weil das erste Bild bei langsamen
    /// Rechnern dauern kann — es geht nur darum, den Totalausfall zu erkennen.
    /// </summary>
    private static readonly TimeSpan VideoWatchDelay = TimeSpan.FromSeconds(20);

    public MainWindow()
    {
        InitializeComponent();
        _strings = new LocalizedStrings(_settings.Language);
        ApplyStrings();

        var uxplayPath = Path.Combine(AppContext.BaseDirectory, "uxplay", "uxplay.exe");
        _controller = new UxPlayController(uxplayPath) { Settings = _settings };
        _controller.StateChanged    += OnStateChanged;
        _controller.DeviceConnected += OnDeviceConnected;
        _watchdog = new PowerWatchdog(_controller);

        var appHwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(appHwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        // Spieglein-Icon in der Titelleiste setzen (Win32-Fensterklassen-Icon).
        // Auf manchen Win10-Builds (22H2 + alter Hardware) kann SetIcon zicken — daher
        // weich umschliessen, im Worst-Case bleibt halt das Default-Icon.
        try
        {
            var titleIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(titleIconPath)) _appWindow.SetIcon(titleIconPath);
        }
        catch { /* ignore, Default-Icon ist okay */ }

        _embedder = new VideoEmbedder(appHwnd, DispatcherQueue) { Log = _controller.AppendLog };
        _embedder.EmbeddedChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() =>
            {
                IdleHint.Visibility = _embedder.HasEmbedded ? Visibility.Collapsed : Visibility.Visible;
                if (_embedder.HasEmbedded)
                {
                    // Bild ist da — die Ausfallwarnung ist damit gegenstandslos.
                    StopVideoWatch();
                    UpdateEmbeddedBounds();
                }
            });
        _embedder.EscapePressed += (_, _) =>
            DispatcherQueue.TryEnqueue(() => { if (_isFullscreen) SetFullscreen(false); });
        _embedder.FullscreenTogglePressed += (_, _) =>
            DispatcherQueue.TryEnqueue(ToggleFullscreen);

        // Close-Button faengt ins Tray statt zu beenden. Echtes Beenden nur ueber Tray-Menue.
        _appWindow.Closing += AppWindow_Closing;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        _tray = new TrayIcon(appHwnd, _strings.GetString("AppTitle"), iconPath);
        _tray.SetLabels(
            _strings.GetString("Tray_Show"),
            _strings.GetString("Button_Start"),
            _strings.GetString("Tray_Quit"));
        _tray.LeftClicked            += (_, _) => DispatcherQueue.TryEnqueue(ShowWindowFromTray);
        _tray.ToggleAirPlayRequested += (_, _) => DispatcherQueue.TryEnqueue(() => _ = ToggleAsync());
        _tray.QuitRequested          += (_, _) => DispatcherQueue.TryEnqueue(() => { _quitRequested = true; this.Close(); });

        UpdateUi(UxPlayState.Stopped);

        Closed += (_, _) =>
        {
            _watchdog.Dispose();
            _embedder.Stop();
            _controller.Dispose();
            _tray.Dispose();
        };
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_quitRequested) return;
        args.Cancel = true;
        sender.Hide();
    }

    private void ShowWindowFromTray()
    {
        _appWindow.Show();
        // Activate bringt das Fenster nach vorne und gibt ihm Focus.
        this.Activate();
    }


    private void FullscreenButton_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void OnFullscreenAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ToggleFullscreen();
        args.Handled = true;
    }

    private void OnEscapeAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_isFullscreen)
        {
            SetFullscreen(false);
            args.Handled = true;
        }
    }

    private void ToggleFullscreen() => SetFullscreen(!_isFullscreen);

    private void ApplyStrings()
    {
        Title = _strings.GetString("AppTitle");
        ToolTipService.SetToolTip(FullscreenButton, _strings.GetString("Tooltip_Fullscreen"));
        ToolTipService.SetToolTip(MoreButton,        _strings.GetString("Tooltip_More"));
        MenuSettings.Text = _strings.GetString("Menu_Settings");
        MenuLog.Text      = _strings.GetString("Menu_Log");
        MenuCoffee.Text   = _strings.GetString("Menu_Coffee");
        MenuAbout.Text    = _strings.GetString("Menu_About");
    }

    private void SetFullscreen(bool on)
    {
        if (_isFullscreen == on) return;
        _isFullscreen = on;
        _appWindow.SetPresenter(on ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default);
        Toolbar.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        FullscreenIcon.Symbol = on ? Symbol.BackToWindow : Symbol.FullScreen;
        // VideoHost-SizeChanged feuert beim Layoutwechsel und triggert UpdateEmbeddedBounds().
    }

    private async System.Threading.Tasks.Task<ContentDialogResult> ShowDialogSafelyAsync(ContentDialog dlg)
    {
        // Embedded uxplay window blockiert die XAML-Composition-Layer.
        // Vor jedem Dialog ausblenden, danach wieder einblenden.
        _embedder.SetEmbeddedVisible(false);
        try     { return await dlg.ShowAsync(); }
        finally { _embedder.SetEmbeddedVisible(true); }
    }

    private async void MenuSettings_Click(object sender, RoutedEventArgs e)
    {
        var oldLang = _settings.Language;
        var dlg = new SettingsDialog(this.Content.XamlRoot, _settings, _strings);
        await ShowDialogSafelyAsync(dlg);
        if (!dlg.SaveRequested) return;

        var previous = _settings;
        _settings = dlg.Result;
        // Der Dialog baut ein frisches AppSettings nur aus seinen vier Feldern.
        // Ohne diese Zeile loescht jedes Speichern die "Letzte Verbindung".
        _settings.LastConnectedDevice = previous.LastConnectedDevice;
        _settings.Save();
        _controller.Settings = _settings;

        // Nur neu starten, wenn sich etwas geaendert hat, das in die uxplay-
        // Kommandozeile eingeht — ein Sprachwechsel allein braucht keinen Neustart.
        bool needsRestart = previous.DeviceName != _settings.DeviceName
                         || previous.Pin        != _settings.Pin
                         || previous.AudioOnly  != _settings.AudioOnly;

        if (needsRestart && _controller.State is UxPlayState.Starting or UxPlayState.Ready or UxPlayState.Streaming)
        {
            BeginTransition();
            try
            {
                _embedder.Stop();
                await _controller.RestartAsync();
                if (_controller.UxPlayProcessId is { } pid) _embedder.StartSearchFor((uint)pid);
            }
            catch (Exception ex) { DetailText.Text = ex.Message; }
            finally { EndTransition(); }
        }

        // Sprache geaendert -> Auto-Restart anbieten (Strings sind beim Start ausgelesen).
        if (_settings.Language != oldLang)
        {
            var ask = new ContentDialog
            {
                XamlRoot            = this.Content.XamlRoot,
                Title               = _strings.GetString("AppTitle"),
                Content             = _strings.GetString("Restart_Question"),
                PrimaryButtonText   = _strings.GetString("Restart_Yes"),
                SecondaryButtonText = _strings.GetString("Restart_No"),
                DefaultButton       = ContentDialogButton.Primary,
            };
            var choice = await ShowDialogSafelyAsync(ask);
            if (choice == ContentDialogResult.Primary) RestartApp();
        }
    }

    private void RestartApp()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = exePath,
                UseShellExecute = true,
            });
        }
        catch { return; }
        _quitRequested = true;
        this.Close();
    }

    private void MenuCoffee_Click(object sender, RoutedEventArgs e)
    {
        // Freiwillige Spende ueber die eigene Domain (leitet auf Buy Me a Coffee weiter).
        // Oeffnet den Standardbrowser; kein In-App-Kauf, daher Store-konform.
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = "https://www.ostrongstudios.de/kaffee",
                UseShellExecute = true,
            });
        }
        catch { /* kein Browser verfuegbar — dann passiert eben nichts */ }
    }

    private async void MenuLog_Click(object sender, RoutedEventArgs e)
    {
        var logPath = _controller.LogPath;
        var dir = System.IO.Path.GetDirectoryName(logPath)!;
        System.IO.Directory.CreateDirectory(dir);

        string content;
        try { content = System.IO.File.Exists(logPath) ? System.IO.File.ReadAllText(logPath) : string.Empty; }
        catch (System.Exception ex) { content = $"<read error: {ex.Message}>"; }
        if (string.IsNullOrEmpty(content)) content = "(empty)";

        var textBox = new TextBox
        {
            Text                = content,
            IsReadOnly          = true,
            AcceptsReturn       = true,
            TextWrapping        = TextWrapping.NoWrap,
            FontFamily          = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize            = 12,
            Height              = 420,
            MinWidth            = 720,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(textBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(textBox, ScrollBarVisibility.Auto);

        var dlg = new ContentDialog
        {
            XamlRoot            = this.Content.XamlRoot,
            Title               = _strings.GetString("Menu_Log"),
            Content             = textBox,
            CloseButtonText     = _strings.GetString("About_Ok"),
            PrimaryButtonText   = _strings.GetString("Log_Copy"),
            SecondaryButtonText = _strings.GetString("Log_OpenFolder"),
            DefaultButton       = ContentDialogButton.Close,
        };
        // Default-Maxwidth umgehen, sodass der Log nicht in eine schmale Spalte gequetscht wird.
        dlg.Resources["ContentDialogMaxWidth"] = 1100.0;

        dlg.PrimaryButtonClick   += (_, args) =>
        {
            args.Cancel = true; // Dialog offen lassen nach Copy
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(content);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        };
        dlg.SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true; // Dialog offen lassen
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = dir,
                UseShellExecute = true,
            });
        };

        await ShowDialogSafelyAsync(dlg);
    }

    private async void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        var content = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 480 };
        var body    = _strings.GetString("About_Body");
        bool first  = true;
        foreach (var line in body.Split('\n'))
        {
            if (!first) content.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
            content.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = line });
            first = false;
        }
        content.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
        content.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
        content.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            { Text = $"{_strings.GetString("About_VersionLabel")} {GetAppVersion()}" });
        content.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
        content.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            { Text = _strings.GetString("About_SourceLabel") + " " });
        var link = new Microsoft.UI.Xaml.Documents.Hyperlink
            { NavigateUri = new Uri("https://github.com/OstrongStudios/spieglein") };
        link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            { Text = "github.com/OstrongStudios/spieglein" });
        content.Inlines.Add(link);

        var dlg = new ContentDialog
        {
            XamlRoot        = this.Content.XamlRoot,
            Title           = _strings.GetString("AppTitle"),
            Content         = content,
            CloseButtonText = _strings.GetString("About_Ok"),
        };
        await ShowDialogSafelyAsync(dlg);
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs e) => await ToggleAsync();

    /// <summary>
    /// Start und Stop laufen asynchron und sind gegen Mehrfachklicks gesperrt.
    /// Frueher lief beides synchron auf dem UI-Thread (Stop bis 10 s, Start bis 6 s)
    /// und ohne Reentranz-Schutz — gestaute Klicks wurden danach als
    /// Start/Stop/Start abgearbeitet und Windows meldete einen Hang.
    /// </summary>
    private async System.Threading.Tasks.Task ToggleAsync()
    {
        if (_transitioning) return;
        BeginTransition();
        try
        {
            if (_controller.State is UxPlayState.Stopped or UxPlayState.Error)
            {
                await _controller.StartAsync();
                if (_controller.UxPlayProcessId is { } pid) _embedder.StartSearchFor((uint)pid);
            }
            else
            {
                _embedder.Stop();
                await _controller.StopAsync();
            }
        }
        catch (Exception ex)
        {
            // Dieser Handler ist async void — eine durchgereichte Exception wuerde
            // die App beenden. Der Controller meldet Fehler ohnehin ueber seinen
            // Zustand, hier bleibt nur der Sicherheitsnetz-Fall.
            DetailText.Text = ex.Message;
        }
        finally
        {
            EndTransition();
        }
    }

    /// <summary>
    /// Sperrt die Bedienelemente waehrend eines Start/Stop-Vorgangs.
    ///
    /// Die beiden Protokollzeilen sind Absicht: Sie machen im Log sichtbar, ob ein
    /// Vorgang sauber zurueckkehrt. Auf einer Test-VM sah es einmal so aus, als
    /// wuerde der Button Klicks schlucken — die Ursache war am Ende die
    /// RDP-Fokusverwaltung der erweiterten Sitzung, nicht die App. Mit diesen
    /// Zeilen laesst sich das beim naechsten Mal in einer Minute unterscheiden,
    /// statt in einer Stunde.
    /// </summary>
    private void BeginTransition()
    {
        _transitioning         = true;
        ToggleButton.IsEnabled = false;
        MenuSettings.IsEnabled = false;
        _controller.AppendLog($"[ui] Uebergang gesperrt ({DateTime.Now:HH:mm:ss.fff})");
    }

    private void EndTransition()
    {
        _transitioning         = false;
        ToggleButton.IsEnabled = true;
        MenuSettings.IsEnabled = true;
        _controller.AppendLog($"[ui] Uebergang frei ({DateTime.Now:HH:mm:ss.fff})");
    }

    private void VideoHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateEmbeddedBounds();

    private void UpdateEmbeddedBounds()
    {
        if (!_embedder.HasEmbedded) return;
        if (VideoHost.XamlRoot is null) return;

        var topLeft = VideoHost.TransformToVisual(null).TransformPoint(new Point(0, 0));
        var scale = VideoHost.XamlRoot.RasterizationScale;
        int x = (int)Math.Round(topLeft.X * scale);
        int y = (int)Math.Round(topLeft.Y * scale);
        int w = (int)Math.Round(VideoHost.ActualWidth * scale);
        int h = (int)Math.Round(VideoHost.ActualHeight * scale);
        _embedder.ApplyBounds(x, y, w, h);
    }

    private void OnStateChanged(object? sender, UxPlayState state)
        => DispatcherQueue.TryEnqueue(() => UpdateUi(state));

    private void OnDeviceConnected(object? sender, string deviceName)
        => DispatcherQueue.TryEnqueue(() =>
        {
            _settings.LastConnectedDevice = deviceName;
            _settings.Save();
            // Falls wir schon im Streaming-State sind, Detail-Text aktualisieren.
            if (_controller.State == UxPlayState.Streaming)
                DetailText.Text = string.Format(_strings.GetString("Detail_Streaming_With"), deviceName);
        });

    private void UpdateUi(UxPlayState state)
    {
        // Button bleibt im AccentButtonStyle (kein Background-Override) — sonst gerät
        // das Template in einen Visual-State-Mismatch, in dem Klicks nicht mehr durchgehen.
        switch (state)
        {
            case UxPlayState.Stopped:
                StatusIndicator.Fill = Brush(Colors.Gray);
                StatusText.Text       = _strings.GetString("Status_Off");
                ToggleButtonText.Text = _strings.GetString("Button_Start");
                DetailText.Text       = !string.IsNullOrWhiteSpace(_settings.LastConnectedDevice)
                    ? string.Format(_strings.GetString("Detail_Off_LastDevice"), _settings.LastConnectedDevice)
                    : _strings.GetString("Detail_Off");
                _embedder.Stop();
                break;

            case UxPlayState.Starting:
                // Gelb statt gruen: uxplay laeuft, hat aber noch nicht bestaetigt,
                // dass es tatsaechlich lauscht.
                StatusIndicator.Fill  = Brush(Colors.Goldenrod);
                StatusText.Text       = _strings.GetString("Status_Starting");
                ToggleButtonText.Text = _strings.GetString("Button_Stop");
                DetailText.Text       = _strings.GetString("Detail_Starting");
                break;

            case UxPlayState.Ready:
                StatusIndicator.Fill  = Brush(Colors.SeaGreen);
                StatusText.Text       = _strings.GetString("Status_Ready");
                ToggleButtonText.Text = _strings.GetString("Button_Stop");
                // Nach einem Verbindungsabbruch den Grund zeigen statt der Anleitung,
                // die der Nutzer gerade erfolgreich befolgt hatte.
                DetailText.Text       = _controller.Fault == UxPlayFault.NetworkDropped
                    ? _strings.GetString("Warn_NetworkDropped")
                    : string.Format(_strings.GetString("Detail_Ready"), _settings.DeviceName);
                break;

            case UxPlayState.Streaming:
                StatusIndicator.Fill  = Brush(Colors.DodgerBlue);
                StatusText.Text       = _strings.GetString("Status_Streaming");
                ToggleButtonText.Text = _strings.GetString("Button_Stop");
                DetailText.Text       = !string.IsNullOrWhiteSpace(_controller.ConnectedDevice)
                    ? string.Format(_strings.GetString("Detail_Streaming_With"), _controller.ConnectedDevice)
                    : _strings.GetString("Detail_Streaming");
                StartVideoWatch();
                break;

            case UxPlayState.Error:
                StatusIndicator.Fill  = Brush(Colors.Crimson);
                StatusText.Text       = _strings.GetString("Status_Error");
                ToggleButtonText.Text = _strings.GetString("Button_Start");
                DetailText.Text       = FaultMessage();
                _embedder.Stop();
                break;
        }

        // Bildueberwachung laeuft nur waehrend einer aktiven Verbindung.
        if (state != UxPlayState.Streaming) StopVideoWatch();

        // Bildschirm und System nur waehrend einer laufenden Uebertragung wachhalten.
        // Muss vom UI-Thread kommen — SetThreadExecutionState wirkt pro Thread.
        KeepAwake.Set(state == UxPlayState.Streaming);
    }

    /// <summary>
    /// Uebersetzt die klassifizierte Fehlerursache in einen handlungsanweisenden Text.
    /// Ohne erkannte Ursache bleibt der uxplay-Rohtext als letzte Rueckfallebene.
    /// </summary>
    /// <summary>
    /// Startet die Ueberwachung der Bildausgabe. Hintergrund: uxplay nimmt die
    /// Verbindung an und meldet "Begin streaming to GStreamer video pipeline",
    /// aber wenn danach die Pipeline nicht hochkommt, erfaehrt die App davon
    /// nichts — sie zeigt weiter "Aktive Verbindung" in Blau, waehrend beim
    /// Nutzer nichts ankommt.
    ///
    /// Beobachtet auf einer Hyper-V-VM ohne Audiogeraet: Die Sitzung wird nie
    /// fertig ausgehandelt, das iPhone haengt auf "Connecting", das Bild bleibt
    /// beim ersten Frame stehen. Dasselbe trifft echte Nutzer ohne funktionierende
    /// Audio- oder Grafikausgabe.
    /// </summary>
    private void StartVideoWatch()
    {
        // Im Audio-Only-Modus entsteht bewusst kein Videofenster.
        if (_settings.AudioOnly) return;

        _videoWatch ??= DispatcherQueue.CreateTimer();
        _videoWatch.Interval    = VideoWatchDelay;
        _videoWatch.IsRepeating = false;
        _videoWatch.Tick -= OnVideoWatchTick;
        _videoWatch.Tick += OnVideoWatchTick;
        _videoWatch.Start();
    }

    private void StopVideoWatch() => _videoWatch?.Stop();

    private void OnVideoWatchTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (_controller.State != UxPlayState.Streaming) return;
        if (_embedder.HasEmbedded) return;

        // Verbindung steht, aber es kam nie ein Bild an.
        StatusIndicator.Fill = Brush(Colors.Goldenrod);
        DetailText.Text      = _strings.GetString("Warn_NoVideoOutput");
    }

    private string FaultMessage() => _controller.Fault switch
    {
        UxPlayFault.DiscoveryBlocked => _strings.GetString("Error_DiscoveryBlocked"),
        UxPlayFault.NameConflict     => string.Format(_strings.GetString("Error_NameConflict"), _settings.DeviceName),
        UxPlayFault.PortBusy         => _strings.GetString("Error_PortBusy"),
        UxPlayFault.NetworkDropped   => _strings.GetString("Warn_NetworkDropped"),
        _                            => _controller.LastError ?? string.Empty,
    };

    private static string GetAppVersion()
    {
        try
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }
        catch
        {
            var asm = typeof(MainWindow).Assembly.GetName().Version;
            return asm?.ToString() ?? "?";
        }
    }

    private static SolidColorBrush Brush(Windows.UI.Color color) => new(color);
}
