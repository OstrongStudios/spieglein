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
        var titleIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(titleIconPath)) _appWindow.SetIcon(titleIconPath);

        _embedder = new VideoEmbedder(appHwnd, DispatcherQueue);
        _embedder.EmbeddedChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() =>
            {
                IdleHint.Visibility = _embedder.HasEmbedded ? Visibility.Collapsed : Visibility.Visible;
                if (_embedder.HasEmbedded) UpdateEmbeddedBounds();
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
        _tray.ToggleAirPlayRequested += (_, _) => DispatcherQueue.TryEnqueue(() => ToggleButton_Click(this, new RoutedEventArgs()));
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

        _settings = dlg.Result;
        _settings.Save();
        _controller.Settings = _settings;

        // Wenn gerade gestreamt wird: uxplay neu starten, damit -n / -pin / -vs greifen.
        if (_controller.State is UxPlayState.Ready or UxPlayState.Streaming)
        {
            _controller.Stop();
            _controller.Start();
            if (_controller.UxPlayProcessId is { } pid) _embedder.StartSearchFor((uint)pid);
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

    private void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.State == UxPlayState.Stopped || _controller.State == UxPlayState.Error)
        {
            _controller.Start();
            if (_controller.UxPlayProcessId is { } pid)
            {
                _embedder.StartSearchFor((uint)pid);
            }
        }
        else
        {
            _embedder.Stop();
            _controller.Stop();
        }
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

            case UxPlayState.Ready:
                StatusIndicator.Fill = Brush(Colors.SeaGreen);
                StatusText.Text       = _strings.GetString("Status_Ready");
                ToggleButtonText.Text = _strings.GetString("Button_Stop");
                DetailText.Text       = string.Format(_strings.GetString("Detail_Ready"), _settings.DeviceName);
                break;

            case UxPlayState.Streaming:
                StatusIndicator.Fill = Brush(Colors.DodgerBlue);
                StatusText.Text       = _strings.GetString("Status_Streaming");
                ToggleButtonText.Text = _strings.GetString("Button_Stop");
                DetailText.Text       = !string.IsNullOrWhiteSpace(_controller.ConnectedDevice)
                    ? string.Format(_strings.GetString("Detail_Streaming_With"), _controller.ConnectedDevice)
                    : _strings.GetString("Detail_Streaming");
                break;

            case UxPlayState.Error:
                StatusIndicator.Fill = Brush(Colors.Crimson);
                StatusText.Text       = _strings.GetString("Status_Error");
                ToggleButtonText.Text = _strings.GetString("Button_Start");
                DetailText.Text       = _controller.LastError ?? string.Empty;
                _embedder.Stop();
                break;
        }
    }

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
