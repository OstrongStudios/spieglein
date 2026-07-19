# Datenschutzerklärung für die Anwendung „Spieglein"

*Stand: 17. Juli 2026 (gültig ab Version 1.0.4.0)*

## 1. Verantwortlicher

**Ostrong Studios** (Inhaber: Mathias Oysmüller)
Altwaldhäusl 55
3662 Münichreith-Laimbach
Niederösterreich, Österreich

E-Mail: support@ostrongstudios.de
Telefon: +43 7413 22341
Web: https://ostrongstudios.de

## 2. Zweck und Funktionsweise der Anwendung

„Spieglein" ist eine Windows-Anwendung, die AirPlay-Streams (Bildschirmsynchronisierung, Audio, Video) von Apple-Geräten (iPhone, iPad, Mac) empfängt und auf dem Bildschirm Ihres PCs darstellt.

Die gesamte Datenübertragung erfolgt **ausschließlich lokal im selben WLAN/LAN** zwischen Ihrem Apple-Gerät und dem PC, auf dem Spieglein installiert ist. Eine Übertragung von Stream-Inhalten oder Metadaten an externe Server findet **nicht** statt.

## 3. Verarbeitete Daten

Bei der Nutzung von Spieglein werden folgende Daten verarbeitet, **ausschließlich lokal auf Ihrem Gerät und ohne Übermittlung an uns oder Dritte**:

| Datenart | Speicherort | Zweck |
|----------|-------------|-------|
| Konfiguration (Gerätename, optionaler PIN-Code, Sprache, Audio-Modus, letzte Verbindung) | `%LOCALAPPDATA%\Packages\…Spieglein…\LocalCache\Local\AirPlayReceiver\settings.json` | Wiederherstellung Ihrer Einstellungen beim nächsten Programmstart |
| Diagnose-Log (technische Meldungen der internen Komponenten) | `%LOCALAPPDATA%\Packages\…Spieglein…\LocalCache\Local\AirPlayReceiver\uxplay.log` | Fehlersuche; nur lokal sichtbar |
| Crash-Log (Stack-Traces bei unerwarteten Programmabbrüchen, ab Version 1.0.3.0) | `%LOCALAPPDATA%\Packages\…Spieglein…\LocalCache\Local\AirPlayReceiver\crash.log` | Fehlersuche; nur lokal sichtbar, wird nicht automatisch versendet |
| Audio-/Video-Stream-Daten vom verbundenen Apple-Gerät | flüchtig im Arbeitsspeicher | Wiedergabe der AirPlay-Übertragung |
| Hostnamen / Gerätekennungen verbundener Apple-Geräte | flüchtig im Arbeitsspeicher + lokal in settings.json (Name der zuletzt verbundenen Geräts) | Anzeige des Verbindungsnamens während aktiver Sitzung und im Idle-Zustand |

**Keine dieser Daten verlassen Ihren PC.** Es findet keine Übermittlung an Ostrong Studios, Apple Inc., Microsoft Corporation oder Dritte statt.

## 4. Netzwerk-Kommunikation

Spieglein nutzt im Betrieb ausschließlich lokale Netzwerkverbindungen:

- mDNS / Bonjour (UDP 5353) — Bekanntgabe des AirPlay-Empfängers im lokalen Netzwerk
- AirPlay-Steuerung (TCP 7000, 7001, 7100)
- RTP-Audio/Video (UDP 6000, 6001, 7011)

Für den Betrieb der Anwendung wird **keine Internet-Verbindung aufgebaut**. Die Anwendung sendet von sich aus keine Daten nach außen — es gibt keine Server der Ostrong Studios, mit denen sie kommuniziert. Updates erfolgen ausschließlich über den Microsoft-Store-Mechanismus; dafür gilt die [Datenschutzerklärung von Microsoft](https://privacy.microsoft.com/de-de/privacystatement).

## 4a. Links zu externen Webseiten

Die Anwendung enthält an zwei Stellen Verweise auf externe Webseiten. Diese werden **ausschließlich durch Ihren Klick** und **in Ihrem Standard-Browser** geöffnet — die Anwendung selbst ruft die Seiten nicht auf und übermittelt dorthin keine Daten:

| Menüpunkt | Ziel | Zweck |
|---|---|---|
| „Kaffee spendieren" | https://www.ostrongstudios.de/kaffee | freiwillige Unterstützung; die Seite leitet weiter zum Anbieter **Buy Me a Coffee** |
| „Über Spieglein" → Quellcode | https://github.com/OstrongStudios/spieglein | Einsicht in den Quellcode (GitHub) |

Sobald Sie eine dieser Seiten aufrufen, gelten die Datenschutzbestimmungen des jeweiligen Anbieters. Dabei können — wie bei jedem Webseitenaufruf — Daten wie Ihre IP-Adresse verarbeitet werden:

- **Buy Me a Coffee** (Buy Me A Coffee Inc., USA): https://www.buymeacoffee.com/privacy-policy
- **GitHub** (GitHub Inc., USA): https://docs.github.com/site-policy/privacy-policies/github-privacy-statement

Eine Spende ist ausdrücklich **freiwillig**. Die Anwendung ist ohne sie vollständig und unbefristet nutzbar; es gibt keine kostenpflichtigen Funktionen. Zahlungsdaten werden ausschließlich beim jeweiligen Zahlungsanbieter verarbeitet, nicht in der Anwendung und nicht durch uns.

## 5. Cookies, Tracking, Analyse-Tools

Spieglein verwendet **keine Cookies, kein Tracking, keine Analyse- oder Telemetrie-Tools** (kein Google Analytics, kein Firebase, kein App Center, keine Crash-Reports an Dritte).

## 6. Drittanbieter-Komponenten (lokal ausgeführt)

Spieglein integriert folgende Open-Source-Komponenten, die ebenfalls lokal auf Ihrem PC ausgeführt werden und keine Verbindung zu externen Servern aufbauen:

- **UxPlay** — GPL v3 — https://github.com/FDH2/UxPlay
- **mDNSResponder** (Apple Open Source) — Apache 2.0 — https://github.com/apple-oss-distributions/mDNSResponder
- **GStreamer** — LGPL — https://gstreamer.freedesktop.org
- **Microsoft .NET 8 / Windows App SDK** — MIT — https://github.com/microsoft/WindowsAppSDK

Der Quellcode von Spieglein selbst steht unter GPL v3 öffentlich zur Verfügung: https://github.com/OstrongStudios/spieglein

## 7. Rechtsgrundlage

Da durch Spieglein keine personenbezogenen Daten an Ostrong Studios übermittelt oder dort verarbeitet werden, findet keine datenschutzrechtlich relevante Verarbeitung durch uns statt.

Sollten Sie uns selbst aktiv kontaktieren (z. B. Support-Anfrage an `support@ostrongstudios.de`), erfolgt die Verarbeitung Ihrer Anfrage- und Kontaktdaten auf Grundlage Ihrer Einwilligung (Art. 6 Abs. 1 lit. a DSGVO) bzw. zur Vertragsanbahnung (Art. 6 Abs. 1 lit. b DSGVO). Wir speichern Ihre Anfrage nur so lange, wie zur Bearbeitung erforderlich, längstens 3 Jahre.

## 8. Ihre Rechte als betroffene Person

Sie haben jederzeit das Recht auf:

- Auskunft über Ihre gespeicherten Daten (Art. 15 DSGVO)
- Berichtigung unrichtiger Daten (Art. 16)
- Löschung („Recht auf Vergessenwerden", Art. 17)
- Einschränkung der Verarbeitung (Art. 18)
- Datenübertragbarkeit (Art. 20)
- Widerspruch gegen die Verarbeitung (Art. 21)
- Widerruf einer erteilten Einwilligung (Art. 7 Abs. 3)

Zur Ausübung Ihrer Rechte wenden Sie sich bitte formlos an: support@ostrongstudios.de

## 9. Beschwerderecht bei der Aufsichtsbehörde

Sie haben das Recht zur Beschwerde bei der österreichischen Datenschutzbehörde:

**Österreichische Datenschutzbehörde**
Barichgasse 40–42, 1030 Wien
Telefon: +43 1 52 152-0
E-Mail: dsb@dsb.gv.at
Web: https://www.dsb.gv.at

## 10. Stand und Änderungen

Diese Datenschutzerklärung ist gültig ab dem **17. Juli 2026** (Version 1.0.4.0). Bei Anpassungen der Anwendung oder bei Änderungen gesetzlicher Vorgaben behalten wir uns vor, diese Erklärung anzupassen. Die jeweils aktuelle Fassung ist unter https://ostrongstudios.de/datenschutzerklaerung/ einsehbar.

**Vorgängerversionen:**
- 30. Mai 2026 (Version 1.0.3.0)
- 17. Mai 2026 (initial, Versionen 1.0.0.0 bis 1.0.1.0)

**Änderungen gegenüber der Fassung vom 30. Mai 2026:**
- Neuer Abschnitt 4a zu den beiden externen Links in der Anwendung („Kaffee spendieren", Quellcode-Link) samt Nennung der dortigen Anbieter
- Ergänzung der Datentabelle um das Crash-Protokoll und den Namen des zuletzt verbundenen Geräts

---

*© 2026 Ostrong Studios. Diese Vorlage wurde nach bestem Wissen erstellt, stellt jedoch keine Rechtsberatung dar.*
