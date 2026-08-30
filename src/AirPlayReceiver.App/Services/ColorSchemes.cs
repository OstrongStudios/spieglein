using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AirPlayReceiver.App.Services;

/// <summary>
/// Ein waehlbares Farbschema: Akzentfarbe fuer den Start/Stopp-Knopf und
/// Toenung fuer die Statusleiste samt Titelleiste.
///
/// Je Schema vier Farben, weil hell und dunkel getrennt gesetzt werden muessen.
/// Windows macht es genauso: heller Akzentton auf dunklem Grund, dunkler auf
/// hellem. Ein Ton fuer beides wuerde in einem der beiden Modi durchfallen.
/// </summary>
public sealed record ColorScheme(
    string Key,
    string StringKey,
    Color AccentLight,
    Color AccentDark,
    Color BarLight,
    Color BarDark);

public static class ColorSchemes
{
    /// <summary>Vorgabe, wenn in den Einstellungen nichts steht.</summary>
    public const string Default = "violett";

    private static Color C(byte r, byte g, byte b) => ColorHelper.FromArgb(255, r, g, b);

    /// <summary>
    /// Die Akzenttoene stammen aus der Standard-Palette von Windows 11, die
    /// hellen Varianten folgen deren Systematik fuer den dunklen Modus.
    ///
    /// Der fuenfte Ton heisst intern "orange" und nicht "ziegel": #F7996E liegt
    /// bei 19 Grad Farbton, das ist Orange. Windows selbst nennt #CA5010
    /// "Dunkelorange" und vergibt "Ziegelrot" an einen ganz anderen Ton (#D13438).
    ///
    /// Zum Gruen ein Hinweis: Der Statuspunkt links in der Leiste ist gruen,
    /// sobald der Empfang bereit ist, und blau bei aktiver Verbindung. Wer
    /// "Gruen" oder "Blau" waehlt, hat den Knopf in derselben Farbe wie das
    /// Signal daneben. Das ist erlaubt, aber es schwaecht die Statusanzeige —
    /// deshalb ist Violett die Vorgabe und nicht Blau.
    /// </summary>
    public static readonly IReadOnlyList<ColorScheme> All = new[]
    {
        new ColorScheme("violett", "Color_Violet", C(0x74,0x4D,0xA9), C(0xB3,0x9B,0xDB), C(0xEF,0xEA,0xF7), C(0x2A,0x23,0x40)),
        new ColorScheme("tuerkis", "Color_Teal",   C(0x03,0x83,0x87), C(0x4F,0xD5,0xDC), C(0xE7,0xF4,0xF4), C(0x16,0x2E,0x30)),
        new ColorScheme("blau",    "Color_Blue",   C(0x00,0x78,0xD4), C(0x4C,0xC2,0xFF), C(0xE8,0xF1,0xFA), C(0x1A,0x26,0x33)),
        new ColorScheme("gruen",   "Color_Green",  C(0x10,0x7C,0x10), C(0x6C,0xCB,0x8B), C(0xE9,0xF4,0xEB), C(0x1A,0x2D,0x1F)),
        new ColorScheme("orange",  "Color_Brick",  C(0xCA,0x50,0x10), C(0xF7,0x99,0x6E), C(0xFA,0xEC,0xE5), C(0x32,0x21,0x19)),
    };

    public static ColorScheme Resolve(string? key) =>
        All.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? All.First(s => s.Key == Default);

    /// <summary>
    /// Setzt die Farben der App auf das gewaehlte Schema.
    ///
    /// Bewusst ueber <c>SolidColorBrush.Color</c> und NICHT ueber den Austausch
    /// der Ressource: Ein neuer Brush im Woerterbuch erreicht bereits gezeichnete
    /// Elemente nicht — <c>{ThemeResource}</c> wertet nur beim Themenwechsel neu
    /// aus. Aendert man dagegen die Farbe des vorhandenen Brush, ziehen alle
    /// Elemente sofort mit, die ihn verwenden.
    /// </summary>
    public static void Apply(string? key, bool dark)
    {
        var s = Resolve(key);
        var accent = dark ? s.AccentDark : s.AccentLight;
        var bar    = dark ? s.BarDark    : s.BarLight;
        // Rand einen Hauch abgesetzt, damit der Knopf eine Kante behaelt.
        var border = dark ? Mix(accent, Colors.White, 0.18) : Mix(accent, Colors.Black, 0.14);

        SetBrush("AccentButtonBackground", accent);
        SetBrush("AccentButtonBackgroundPointerOver", accent);
        SetBrush("AccentButtonBackgroundPressed", accent);
        SetBrush("AccentButtonBorderBrush", border);
        SetBrush("AccentButtonBorderBrushPointerOver", border);
        SetBrush("AppToolbarBackgroundBrush", bar);
    }

    /// <summary>Farbe der Statusleiste im gewaehlten Schema — fuer die Titelleiste.</summary>
    public static Color BarColor(string? key, bool dark)
    {
        var s = Resolve(key);
        return dark ? s.BarDark : s.BarLight;
    }

    private static void SetBrush(string key, Color color)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true &&
            value is SolidColorBrush brush)
        {
            brush.Color = color;
        }
    }

    private static Color Mix(Color a, Color b, double t) => ColorHelper.FromArgb(
        255,
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
}
