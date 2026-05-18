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
    /// <summary>"auto", "de-DE" oder "en-US".</summary>
    public string Language { get; set; } = "auto";
    /// <summary>Letzter erfolgreich verbundener Client (z. B. "iPhone von Mathias").</summary>
    public string? LastConnectedDevice { get; set; }

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
