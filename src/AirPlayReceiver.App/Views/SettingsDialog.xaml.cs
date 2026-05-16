using AirPlayReceiver.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AirPlayReceiver.App.Views;

public sealed partial class SettingsDialog : ContentDialog
{
    public SettingsDialog(XamlRoot xamlRoot, AppSettings current, LocalizedStrings s)
    {
        InitializeComponent();
        XamlRoot = xamlRoot;

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
        LangAuto.Content                 = s.GetString("Settings_Language_Auto");
        LangHint.Text                    = s.GetString("Settings_Language_Hint");
        DiscoveryHint.Text               = s.GetString("Settings_Discovery_Hint");

        // Werte einfuellen
        DeviceNameBox.Text   = current.DeviceName;
        AudioOnlySwitch.IsOn = current.AudioOnly;
        PinBox.Text          = current.Pin ?? string.Empty;
        LanguageCombo.SelectedIndex = current.Language switch
        {
            "de-DE" => 1,
            "en-US" => 2,
            _       => 0,
        };
    }

    public AppSettings Result { get; private set; } = new();
    public bool SaveRequested { get; private set; }

    private void ContentDialog_PrimaryButtonClick(object sender, ContentDialogButtonClickEventArgs args)
    {
        Result = new AppSettings
        {
            DeviceName = string.IsNullOrWhiteSpace(DeviceNameBox.Text)
                         ? System.Environment.MachineName
                         : DeviceNameBox.Text.Trim(),
            AudioOnly  = AudioOnlySwitch.IsOn,
            Pin        = string.IsNullOrWhiteSpace(PinBox.Text) ? null : PinBox.Text.Trim(),
            Language   = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto",
        };
        SaveRequested = true;
    }
}
