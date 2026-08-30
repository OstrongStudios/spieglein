using System;
using System.Linq;
using AirPlayReceiver.App.Services;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AirPlayReceiver.App.Views;

public sealed partial class SettingsDialog : ContentDialog
{
    private readonly AppSettings _current;

    public SettingsDialog(XamlRoot xamlRoot, AppSettings current, LocalizedStrings s, bool darkTheme)
    {
        InitializeComponent();
        XamlRoot = xamlRoot;
        _current = current;
        Result   = current.Clone();

        // Lokalisierte Texte
        Title                            = s.GetString("Settings_Title");
        PrimaryButtonText                = s.GetString("Settings_Save");
        SecondaryButtonText              = s.GetString("Settings_Cancel");
        DeviceNameBox.Header             = s.GetString("Settings_DeviceName");
        DeviceNameBox.PlaceholderText    = s.GetString("Settings_DeviceName_Placeholder");
        AudioOnlySwitch.Header           = s.GetString("Settings_AudioOnly");
        AudioOnlySwitch.OnContent        = s.GetString("Settings_AudioOnly_On");
        AudioOnlySwitch.OffContent       = s.GetString("Settings_AudioOnly_Off");
        PinBox.Header                    = s.GetString("Settings_Pin");
        PinBox.PlaceholderText           = s.GetString("Settings_Pin_Placeholder");
        LanguageCombo.Header             = s.GetString("Settings_Language");
        LangHint.Text                    = s.GetString("Settings_Language_Hint");
        DiscoveryHint.Text               = s.GetString("Settings_Discovery_Hint");
        ColorSchemeRow.Header            = s.GetString("Settings_ColorScheme");
        TextSizeCombo.Header             = s.GetString("Settings_TextSize");

        BuildColorSchemeRow(s, current.ColorScheme, darkTheme);

        foreach (var groesse in TextSizes.All)
        {
            TextSizeCombo.Items.Add(new ComboBoxItem
            {
                Content = s.GetString(groesse.StringKey),
                Tag     = groesse.Key,
            });
        }
        TextSizeCombo.SelectedIndex = IndexOfTextSize(TextSizes.Resolve(current.TextSize).Key);

        // Sprachliste: "Automatisch" zuerst, danach alles, was unter Strings/ liegt.
        LanguageCombo.Items.Add(new ComboBoxItem
        {
            Content = s.GetString("Settings_Language_Auto"),
            Tag     = LocalizedStrings.AutoLanguage,
        });
        foreach (var lang in LocalizedStrings.AvailableLanguages())
        {
            LanguageCombo.Items.Add(new ComboBoxItem { Content = lang.DisplayName, Tag = lang.Code });
        }

        // Werte einfuellen
        DeviceNameBox.Text   = current.DeviceName;
        AudioOnlySwitch.IsOn = current.AudioOnly;
        PinBox.Text          = current.Pin ?? string.Empty;
        LanguageCombo.SelectedIndex = IndexOfLanguage(current.Language);
    }


    /// <summary>
    /// Baut die Farbauswahl aus <see cref="ColorSchemes.All"/>.
    ///
    /// Jeder Eintrag zeigt den Ton, den der Nutzer im aktuellen Hell-/Dunkelmodus
    /// auch wirklich bekommt — das Thema wird uebergeben und nicht aus ActualTheme
    /// gelesen, das im Konstruktor noch nicht aufgeloest sein kann.
    ///
    /// Auswahlzustand, Position ("1 von 5") und Rolle liefert RadioButtons von
    /// selbst an die Sprachausgabe, lokalisiert. Deshalb steht hier nur noch der
    /// Farbname.
    /// </summary>
    private void BuildColorSchemeRow(LocalizedStrings s, string? gewaehlt, bool darkTheme)
    {
        ColorSchemeRow.Items.Clear();
        int index = 0, treffer = 0;
        var aktiv = ColorSchemes.Resolve(gewaehlt).Key;

        foreach (var schema in ColorSchemes.All)
        {
            var name = s.GetString(schema.StringKey);
            var knopf = new RadioButton
            {
                Tag = schema.Key,
                MinWidth = 0,
                Style = (Style)Resources["FarbflaecheStyle"],
                Content = new Ellipse
                {
                    Width = 26,
                    Height = 26,
                    Fill = new SolidColorBrush(darkTheme ? schema.AccentDark : schema.AccentLight),
                },
            };
            ToolTipService.SetToolTip(knopf, name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(knopf, name);

            if (schema.Key == aktiv) treffer = index;
            ColorSchemeRow.Items.Add(knopf);
            index++;
        }

        ColorSchemeRow.SelectedIndex = treffer;
    }

    /// <summary>Position der gespeicherten Schriftgroesse; 0 (= Standard) als Rueckfall.</summary>
    private int IndexOfTextSize(string key)
    {
        for (int i = 0; i < TextSizeCombo.Items.Count; i++)
        {
            if (TextSizeCombo.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), key, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return 0;
    }

    /// <summary>Position der gespeicherten Sprache in der Liste; 0 (= Automatisch) als Rueckfall.</summary>
    private int IndexOfLanguage(string code)
    {
        for (int i = 0; i < LanguageCombo.Items.Count; i++)
        {
            if (LanguageCombo.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), code, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return 0;
    }

    public AppSettings Result { get; private set; }
    public bool SaveRequested { get; private set; }

    private void ContentDialog_PrimaryButtonClick(object sender, ContentDialogButtonClickEventArgs args)
    {
        // Auf einer KOPIE der aktuellen Einstellungen arbeiten, nicht auf einem frischen
        // Objekt: sonst fallen alle Felder, die dieser Dialog nicht anzeigt, auf ihren
        // Standardwert zurueck. Und nicht auf der Instanz selbst, denn MenuSettings_Click
        // vergleicht hinterher das alte gegen das neue Objekt, um zu entscheiden, ob
        // uxplay neu starten muss — bei derselben Instanz waere der Vergleich immer falsch.
        var result = _current.Clone();

        result.DeviceName = string.IsNullOrWhiteSpace(DeviceNameBox.Text)
                            ? Environment.MachineName
                            : DeviceNameBox.Text.Trim();
        result.AudioOnly  = AudioOnlySwitch.IsOn;
        result.Pin        = string.IsNullOrWhiteSpace(PinBox.Text) ? null : PinBox.Text.Trim();
        result.Language   = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                            ?? LocalizedStrings.AutoLanguage;
        result.ColorScheme = (ColorSchemeRow.SelectedItem as RadioButton)?.Tag?.ToString()
                             ?? ColorSchemes.Default;
        result.TextSize    = (TextSizeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                             ?? TextSizes.Default;

        Result        = result;
        SaveRequested = true;
    }
}
