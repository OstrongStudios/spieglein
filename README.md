# Spieglein

**Kostenloser AirPlay-Receiver für Windows 10/11.** Spiegle iPhone, iPad oder Mac auf deinen PC — Bild und Ton.

[![Microsoft Store](https://img.shields.io/badge/Microsoft%20Store-Spieglein-blue)](https://apps.microsoft.com/detail/9PL8FXP2VT14)
[![Releases](https://img.shields.io/github/v/release/OstrongStudios/spieglein?label=GitHub%20Release)](https://github.com/OstrongStudios/spieglein/releases)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)

## Was es kann

- **AirPlay-Bildschirmsynchronisierung** aus dem iOS-Kontrollzentrum empfangen
- Video im **App-Fenster eingebettet** (kein zweites Fenster) oder im Fullscreen (Alt+Enter)
- **Audio mit** Bildübertragung (Mirror-Mode AAC-ELD)
- **Optionaler 4-stelliger PIN-Code** zum Schutz vor fremden Verbindungen
- **Anzeige des verbundenen Geräts** während des Streams + „Letzte Verbindung"-Hinweis
- **Tray-Icon** mit Schnellzugriff, App-Beenden über Tray
- **Sleep-/Wake-Watchdog** — verbindet nach PC-Standby automatisch wieder
- **Bildschirm bleibt an** während einer Übertragung — kein Standby mitten im Film
- **Firewall-Freigabe ohne Nachfrage** — die Regeln stehen im MSIX-Manifest und werden bei der Installation angelegt
- **Läuft auch ohne Audioausgabe** — dann eben ohne Ton, das Bild kommt trotzdem
- **Auch auf älterer Grafik** — fester D3D11-Videosink statt `autovideosink`, damit Intel-Grafik ohne D3D12 (vor Haswell) mitspielt
- **Verständliche Fehlermeldungen** statt stiller Ausfälle
- **Mehrsprachig** — Deutsch, Englisch, Spanisch, Französisch, Portugiesisch, Chinesisch (vereinfacht), Japanisch
- **Lokal-only** — keine Cloud, kein Tracking, keine Telemetrie

## Systemvoraussetzungen

| Komponente | Anforderung |
|---|---|
| Betriebssystem | Windows 10 Version 2004 (Build 19041) oder neuer, Windows 11 |
| Architektur | x64 |
| CPU | Beliebige x86-64-CPU ab ca. 2008 (auch Sandy Bridge, Ivy Bridge) |
| Speicher | ~250 MB Festplatte (eingebundene Media-Runtime), 4 GB RAM empfohlen |
| Netzwerk | iPhone/iPad/Mac muss im selben WLAN/LAN sein |
| iOS-Version | iOS 12 / iPadOS 13 / macOS Mojave oder neuer |

## Wie es funktioniert

Spieglein bündelt im MSIX-Paket:

| Komponente | Lizenz | Zweck |
|---|---|---|
| **[UxPlay](https://github.com/FDH2/UxPlay) 1.73.6+** | GPL v3 | AirPlay-Protokoll-Implementierung (RTSP, RTP, HLS) |
| **[mDNSResponder](https://github.com/apple-oss-distributions/mDNSResponder)** (Apple OSS) | Apache 2.0 | Bonjour-Service-Discovery für AirPlay |
| **GStreamer 1.24+** | LGPL | Audio-/Video-Pipeline |
| **WinUI 3** (.NET 8) | MIT | Native Windows-UI |

Die WinUI-App startet UxPlay und mDNSResponder als Hintergrund-Kindprozesse und reparented das von GStreamer erzeugte Videofenster in den Content-Bereich (Win32-`SetParent`).

## Übersetzungen beisteuern

Die Oberflächentexte liegen als `.resw`-XML unter `src/AirPlayReceiver.App/Strings/<locale>/Resources.resw` — rund 45 kurze Strings. Eine neue Sprache hinzuzufügen erfordert **keine Code-Änderung**:

1. `Strings/en-US/Resources.resw` in einen neuen Ordner kopieren, z. B. `Strings/it-IT/`
2. Die `<value>`-Inhalte übersetzen (Schlüssel unverändert lassen)
3. Im `Package.appxmanifest` eine Zeile ergänzen: `<Resource Language="it-IT" />`

Die App findet die Sprache beim Start von selbst und zeigt sie in der Sprachauswahl unter ihrem nativen Namen an. Fällt eine Systemsprache aus dem Raster, greift die Kette *exakte Sprache → Sprachfamilie (`fr-CA` findet `fr-FR`) → Englisch*.

Bei der Verbindungsanleitung bitte auf die **offizielle iOS-Terminologie der Zielsprache** achten — z. B. heißt „Bildschirmsynchronisierung" auf Spanisch *Duplicar pantalla*, auf Französisch *Recopie de l'écran*. Wörtliche Übersetzungen führen Nutzer sonst zu Menüpunkten, die es auf ihrem iPhone nicht gibt.

## Build aus Quelle

### Voraussetzungen

| Tool | Zweck |
|---|---|
| Windows 10/11 64-bit | Zielplattform |
| .NET 8 SDK | App-Build |
| Visual Studio 2022 + Workload „Desktopentwicklung mit C++" | mDNSResponder-Build |
| MSYS2 + MINGW64 mit GStreamer/Toolchain | UxPlay-Build |
| Git | Quellen klonen |

### Vollständiger Build (PowerShell, aus Repo-Root)

```powershell
# 1. mDNSResponder (Bonjour-Equivalent) bauen, ~5 Min
./scripts/build-bonjour-sdk.ps1

# 2. UxPlay via MSYS2/MINGW64 bauen, ~15 Min
#    Script setzt -DNO_MARCH_NATIVE=ON für CPU-portable Binaries
./scripts/build-uxplay.ps1

# 3. Runtime-DLLs sammeln (~232 MB)
#    Script hat eine DENY_LIST und entfernt z. B. libgstcodec2json.dll
./scripts/copy-runtime-dlls.ps1

# 4. Visual Assets generieren
./scripts/generate-msix-assets.ps1 -Source Assets/source/spieglein.png

# 5. App + MSIX bauen
./scripts/build-msix.ps1 -StoreUpload
```

Resultat: `src/AirPlayReceiver.App/bin/x64/Release/.../AppPackages/.../AirPlayReceiver.App_*.msix`

## Bekannte Limitationen

- **Manche iOS-Spiele übertragen kein Audio über AirPlay** — die Game-Engine entscheidet das (häufig wegen Voice-Chat oder Audio-Latenz-Anforderungen). YouTube, Filme, Musik, System-Audio funktionieren wie erwartet. Auch kommerzielle Receiver wie AirServer haben dasselbe Verhalten.
- **AirPlay-2-Multi-Room-Audio** wird nicht unterstützt (ist auch nicht die Zielsetzung — dafür gibt's shairport-sync).
- **DRM-geschützte Inhalte** (Apple TV+, Netflix-DRM) können nicht gespiegelt werden — Apple verschlüsselt diese Streams.

## Fehlersuche

Wenn Spieglein nicht startet oder Verbindungsprobleme auftreten, helfen die lokalen Log-Dateien unter:

```
%LOCALAPPDATA%\Packages\4663Ostronggames.Spieglein_e5a5qvsqnd7j6\LocalCache\Local\AirPlayReceiver\
```

| Datei | Inhalt |
|---|---|
| `uxplay.log` | uxplay-Output (Verbindungsanfragen, GStreamer-Pipeline-State, Warnungen) |
| `crash.log` | Stack-Traces unbehandelter Exceptions seit 1.0.3 |
| `settings.json` | Deine Konfiguration (kann zum Reset gelöscht werden) |

Häufige Erst-Setup-Probleme:
- **Erster Start nach einem Update dauert 10–20 Sekunden**, bis der Status auf grün springt. GStreamer baut einmal pro Paketversion seine Plugin-Registry neu auf (~250 Plugins). Danach sind es 2–3 Sekunden. Das ist kein Hänger.
- **iPhone findet PC nicht:** Ab 1.0.5.0 legt Windows die Firewall-Regeln bei der Installation selbst an (siehe `windows.firewallRules` im Manifest) — eine Nachfrage sollte nicht mehr kommen. Bleibt es trotzdem dabei, ist meist das WLAN als **„Öffentlich"** eingestuft; unter *Einstellungen → Netzwerk und Internet* auf „Privat" umstellen. Prüfen, ob die Regeln existieren:
  ```powershell
  Get-NetFirewallApplicationFilter | Where-Object { $_.Program -match 'uxplay|mDNSResponder' }
  ```
  Achtung: Wer vor 1.0.5.0 einen Firewall-Dialog *ohne* Administratorrechte beantwortet hat, hat dabei eine **Block-Regel** erzeugt. Block schlägt Allow — diese Regel muss von Hand gelöscht werden.
- **„Aktive Verbindung" aber kein Bild:** Du hast wahrscheinlich AirPlay über die *Lautstärke-Auswahl* statt *Bildschirmsynchronisierung* gestartet (siehe iOS-Kontrollzentrum, oben links das Doppelfenster-Symbol).

## Releases & Changelog

Siehe **[GitHub Releases](https://github.com/OstrongStudios/spieglein/releases)** — jede Version mit Bullet-Liste der Änderungen.

Aktuelle Veröffentlichung über den **[Microsoft Store](https://apps.microsoft.com/detail/9PL8FXP2VT14)** — Auto-Update beim User.

## Lizenz

**GPL v3.** Da Spieglein UxPlay (GPL v3) einbindet, steht das gesamte Projekt unter GPL v3. Siehe [LICENSE](LICENSE).

Drittanbieter-Lizenzen siehe [NOTICE.md](NOTICE.md).

## Disclaimer

Spieglein ist **nicht von Apple Inc.** AirPlay ist eine eingetragene Marke von Apple Inc.

---

© 2026 Ostrong Studios — Kontakt: support@ostrongstudios.de
