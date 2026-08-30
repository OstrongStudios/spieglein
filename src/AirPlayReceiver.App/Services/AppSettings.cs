using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AirPlayReceiver.App.Services;

public sealed class AppSettings
{
    public string DeviceName { get; set; } = Environment.MachineName;
    public string? Pin { get; set; }
    public bool AudioOnly { get; set; }
    /// <summary>
    /// "auto" (Systemsprache) oder ein Locale-Code, zu dem es einen Ordner unter
    /// Strings/ gibt (z. B. "de-DE", "zh-Hans"). Siehe <see cref="LocalizedStrings"/>.
    /// </summary>
    public string Language { get; set; } = "auto";
    /// <summary>Letzter erfolgreich verbundener Client (z. B. "iPhone von Mathias").</summary>
    public string? LastConnectedDevice { get; set; }

    /// <summary>
    /// Schluessel eines Farbschemas aus <see cref="ColorSchemes"/>. Unbekannte
    /// Werte fallen dort auf die Vorgabe zurueck — eine von Hand veraenderte
    /// settings.json kann die App also nicht farblos machen.
    /// </summary>
    public string ColorScheme { get; set; } = ColorSchemes.Default;

    /// <summary>
    /// Schluessel einer Schriftgroesse aus <see cref="TextSizes"/>. Unbekannte
    /// Werte fallen dort auf die Vorgabe zurueck.
    /// </summary>
    public string TextSize { get; set; } = TextSizes.Default;

    /// <summary>
    /// Vollstaendige Kopie. Der Einstellungsdialog zeigt nur einen Teil der Felder und
    /// baut sein Ergebnis auf dieser Kopie auf — alles, was er nicht kennt, bleibt
    /// dadurch stehen.
    ///
    /// Bewusst <c>MemberwiseClone</c> und KEINE Feldliste: eine Liste muesste bei
    /// jedem neuen Feld nachgezogen werden, und genau das wurde schon einmal
    /// vergessen. Die Folge war, dass jedes Speichern der Einstellungen still die
    /// "Letzte Verbindung" geloescht hat. Ein Zaehler oder Merker wuerde auf
    /// dieselbe Weise verschwinden — ohne Absturz, einfach immer wieder auf null.
    ///
    /// Flache Kopie genuegt: alle Felder sind Werttypen oder string.
    /// Wer hier ein Feld mit veraenderlichem Objekt ergaenzt, muss das aendern.
    /// </summary>
    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    [JsonIgnore]
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AirPlayReceiver",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is not null) return loaded;
            }
        }
        catch
        {
            // Korrupte Settings ignorieren, neu starten.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch
        {
            // best effort
        }
    }
}
