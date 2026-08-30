using System;
using System.Collections.Generic;
using System.Linq;

namespace AirPlayReceiver.App.Services;

/// <summary>
/// Eine waehlbare Schriftgroesse. Alle Werte haengen zusammen und werden
/// deshalb gemeinsam gesetzt — eine groessere Schrift allein wuerde den
/// Statuspunkt zu klein und den Hinweistext zu schmal wirken lassen.
///
/// <see cref="HintMaxWidth"/> ist der wichtigste Wert: Die Verbindungsanleitung
/// ist der laengste Text der App, und sie bricht um, sobald eine Zeile breiter
/// wird. Spanisch braucht bei 15 px rund 567 px, Franzoesisch 555, Portugiesisch
/// 557. Waechst die Schrift, muss die Breite mitwachsen, sonst steht in vier
/// Sprachen ein Satz mittendrin umgebrochen.
/// </summary>
public sealed record TextSize(
    string Key,
    string StringKey,
    double Status,
    double Hint,
    double Button,
    double Dot,
    double ButtonMinWidth,
    double HintMaxWidth);

public static class TextSizes
{
    /// <summary>Vorgabe, wenn in den Einstellungen nichts steht.</summary>
    public const string Default = "standard";

    /// <summary>
    /// Die Breiten sind aus den gemessenen Textbreiten abgeleitet, nicht geschaetzt:
    /// 580 px reichen bei 15 px fuer alle sieben Sprachen, darueber proportional.
    /// </summary>
    public static readonly IReadOnlyList<TextSize> All = new[]
    {
        //                Key           StringKey        Status Hint Button Dot  MinW  MaxW
        new TextSize("standard",   "Size_Standard",   18, 15, 15, 20, 180, 580),
        new TextSize("gross",      "Size_Large",      20, 16, 16, 22, 200, 620),
        new TextSize("sehr-gross", "Size_ExtraLarge", 23, 18, 17, 25, 220, 700),
    };

    public static TextSize Resolve(string? key) =>
        All.FirstOrDefault(g => string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? All.First(g => g.Key == Default);
}
