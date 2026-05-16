using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace AirPlayReceiver.App.Services;

/// <summary>
/// Schlanker Resource-Loader, der die .resw-Dateien aus dem Strings/-Ordner
/// direkt zur Laufzeit liest. Wir umgehen damit das WinAppSDK-PRI-System,
/// das in unpackaged Apps Language-Override-Probleme hat.
/// </summary>
public sealed class LocalizedStrings
{
    private readonly Dictionary<string, string> _map = new();
    private readonly Dictionary<string, string> _fallback = new();

    public LocalizedStrings(string language)
    {
        var target = language switch
        {
            "de-DE" => "de-DE",
            "en-US" => "en-US",
            _       => GuessFromSystem(),
        };
        Load(target, _map);
        if (target != "de-DE") Load("de-DE", _fallback); // de-DE als Fallback fuer fehlende Keys
    }

    private static string GuessFromSystem()
    {
        var name = CultureInfo.CurrentUICulture.Name;
        if (name.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en-US";
        return "de-DE";
    }

    private static void Load(string locale, Dictionary<string, string> target)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Strings", locale, "Resources.resw");
        if (!File.Exists(path)) return;
        try
        {
            var doc = XDocument.Load(path);
            foreach (var data in doc.Descendants("data"))
            {
                var name = data.Attribute("name")?.Value;
                var value = data.Element("value")?.Value;
                if (name is not null && value is not null) target[name] = value;
            }
        }
        catch
        {
            // korrupte resw -> ignorieren
        }
    }

    public string GetString(string key)
    {
        if (_map.TryGetValue(key, out var v))       return v;
        if (_fallback.TryGetValue(key, out var fb))  return fb;
        return key;
    }
}
