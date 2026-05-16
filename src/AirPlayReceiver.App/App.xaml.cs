using Microsoft.UI.Xaml;

namespace AirPlayReceiver.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        // Sprache wird vom LocalizedStrings-Helper aus dem AppSettings gelesen,
        // wir setzen NICHT ApplicationLanguages.PrimaryLanguageOverride — der
        // Aufruf crasht in unpackaged WinUI-3-Apps.
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
