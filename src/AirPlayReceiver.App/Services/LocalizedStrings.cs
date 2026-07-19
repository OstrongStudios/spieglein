using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace AirPlayReceiver.App.Services;

/// <summary>
/// Liest die .resw-Dateien aus dem Strings/-Ordner direkt zur Laufzeit. Umgeht
/// damit das WinAppSDK-PRI-System, das in unpackaged Apps Language-Override-
/// Probleme hat (siehe App.xaml.cs).
///
/// NEUE SPRACHE HINZUFUEGEN:
///   1. Strings/&lt;locale&gt;/Resources.resw anlegen (Kopie von en-US, uebersetzt)
///   2. &lt;Resource Language="&lt;locale&gt;"/&gt; im Package.appxmanifest eintragen
/// Sonst nichts. Loader und Sprachauswahl finden die Sprache von selbst.
/// </summary>
public sealed class LocalizedStrings
{
    /// <summary>Sprache fuer alle Systeme, deren Locale wir nicht mitliefern.</summary>
    public const string DefaultLanguage = "en-US";

    /// <summary>Wert in AppSettings.Language, der "nimm die Systemsprache" bedeutet.</summary>
    public const string AutoLanguage = "auto";

    private const string StringsFolder = "Strings";
    private const string ReswFileName  = "Resources.resw";

    /// <summary>Nachschlage-Kette: gewaehlte Sprache zuerst, dann die Fallbacks.</summary>
    private readonly List<Dictionary<string, string>> _chain = new();

    /// <summary>Tatsaechlich geladene Sprache (kann von der angefragten abweichen).</summary>
    public string ActiveLanguage { get; }

    public LocalizedStrings(string requestedLanguage)
    {
        ActiveLanguage = Resolve(requestedLanguage);

        // Fallback-Kette: gewaehlt -> Englisch -> Deutsch (Ursprungssprache, immer komplett).
        foreach (var locale in new[] { ActiveLanguage, DefaultLanguage, "de-DE" }.Distinct())
        {
            var map = Load(locale);
            if (map.Count > 0) _chain.Add(map);
        }
    }

    /// <summary>
    /// Alle Sprachen, fuer die eine Resources.resw vorliegt — aus dem Dateisystem
    /// gelesen, nicht aus einer gepflegten Liste. Sortiert nach nativem Namen.
    /// </summary>
    public static IReadOnlyList<LanguageOption> AvailableLanguages()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, StringsFolder);
        if (!Directory.Exists(dir)) return Array.Empty<LanguageOption>();

        var list = new List<LanguageOption>();
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            if (!File.Exists(Path.Combine(sub, ReswFileName))) continue;
            var code = Path.GetFileName(sub);
            list.Add(new LanguageOption(code, NativeNameOf(code)));
        }
        return list.OrderBy(l => l.DisplayName, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>"Deutsch", "English", "Español" … — der Name in der Sprache selbst.</summary>
    private static string NativeNameOf(string code)
    {
        try
        {
            var native = CultureInfo.GetCultureInfo(code).NativeName;
            // .NET liefert "Deutsch (Deutschland)" — die Region interessiert hier nicht.
            var cut = native.IndexOf(" (", StringComparison.Ordinal);
            if (cut > 0) native = native[..cut];
            return CultureInfo.GetCultureInfo(code).TextInfo.ToTitleCase(native);
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }

    /// <summary>
    /// Findet die beste vorhandene Sprache: exakter Treffer, sonst gleiche
    /// Sprachfamilie (fr-CA findet fr-FR), sonst <see cref="DefaultLanguage"/>.
    /// </summary>
    private static string Resolve(string requested)
    {
        var available = AvailableLanguages().Select(l => l.Code).ToList();
        if (available.Count == 0) return DefaultLanguage;

        var wanted = string.IsNullOrWhiteSpace(requested) || requested == AutoLanguage
            ? CultureInfo.CurrentUICulture.Name
            : requested;

        var exact = available.FirstOrDefault(c => c.Equals(wanted, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        // Sprachfamilie: "fr-CA" -> "fr", passt auf "fr-FR".
        // Bei Chinesisch faengt das auch zh-CN/zh-TW auf das vorhandene zh-Hans.
        var family = wanted.Split('-')[0];
        var relative = available.FirstOrDefault(c =>
            c.Split('-')[0].Equals(family, StringComparison.OrdinalIgnoreCase));
        if (relative is not null) return relative;

        return available.Contains(DefaultLanguage) ? DefaultLanguage : available[0];
    }

    private static Dictionary<string, string> Load(string locale)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = Path.Combine(AppContext.BaseDirectory, StringsFolder, locale, ReswFileName);
        if (!File.Exists(path)) return map;

        try
        {
            foreach (var data in XDocument.Load(path).Descendants("data"))
            {
                var name  = data.Attribute("name")?.Value;
                var value = data.Element("value")?.Value;
                if (name is not null && value is not null) map[name] = value;
            }
        }
        catch
        {
            // Korrupte resw: lieber leer als Absturz — die Fallback-Kette springt ein.
        }
        return map;
    }

    public string GetString(string key)
    {
        foreach (var map in _chain)
            if (map.TryGetValue(key, out var value)) return value;
        return key;
    }

    /// <summary>Eine auswaehlbare Sprache: "de-DE" + "Deutsch".</summary>
    public sealed record LanguageOption(string Code, string DisplayName);
}
