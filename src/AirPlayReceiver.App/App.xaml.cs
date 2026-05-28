using System;
using System.IO;
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

        // Last-Resort-Catcher: Unbehandelte Exceptions (UI- und Domain-Ebene) in eine
        // Crash-Log-Datei schreiben, sodass wir bei Insights-Reports im Partner Center
        // eine Chance haben, das Problem zu reproduzieren.
        UnhandledException += (s, e) =>
        {
            WriteCrashLog("[UI] " + e.Message + "\n" + e.Exception);
            // e.Handled NICHT auf true — falls der Status korrupt ist, lieber sauber crashen.
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            WriteCrashLog("[AppDomain] " + e.ExceptionObject);
        };
    }

    private static void WriteCrashLog(string content)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AirPlayReceiver");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"--- {DateTime.Now:O} ---\n{content}\n\n");
        }
        catch { /* nichts wir tun koennen */ }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
