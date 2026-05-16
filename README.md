# Spieglein

**Kostenloser AirPlay-Receiver für Windows 10/11.** Spiegle iPhone, iPad oder Mac auf deinen PC — Bild und Ton.

[![Microsoft Store](https://img.shields.io/badge/Microsoft%20Store-Spieglein-blue)](https://apps.microsoft.com/detail/9PL8FXP2VT14)

## Was es kann

- **AirPlay-Bildschirmsynchronisierung** aus dem iOS-Kontrollzentrum empfangen
- Video im **App-Fenster eingebettet** (kein zweites Fenster) oder im Fullscreen (Alt+Enter)
- **Audio mit** Bildübertragung (Mirror-Mode AAC-ELD)
- **Optionaler 4-stelliger PIN-Code** zum Schutz vor fremden Verbindungen
- **Tray-Icon** mit Schnellzugriff, App-Beenden über Tray
- **Sleep-/Wake-Watchdog** — verbindet nach PC-Standby automatisch wieder
- **Mehrsprachig** (Deutsch / Englisch)

## Wie es funktioniert

Spieglein bündelt im MSIX-Paket:

| Komponente | Lizenz | Zweck |
|---|---|---|
| **[UxPlay](https://github.com/FDH2/UxPlay) 1.73.6+** | GPL v3 | AirPlay-Protokoll-Implementierung (RTSP, RTP, HLS) |
| **[mDNSResponder](https://github.com/apple-oss-distributions/mDNSResponder)** (Apple OSS) | Apache 2.0 | Bonjour-Service-Discovery für AirPlay |
| **GStreamer 1.24+** | LGPL | Audio-/Video-Pipeline |
| **WinUI 3** (.NET 8) | MIT | Native Windows-UI |

Die WinUI-App startet UxPlay und mDNSResponder als Hintergrund-Kindprozesse und reparented das von GStreamer erzeugte Videofenster in den Content-Bereich (Win32-`SetParent`).

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
# 1. mDNSResponder (Bonjour-Equivalent) bauen, ~2 Min
./scripts/build-bonjour-sdk.ps1

# 2. UxPlay via MSYS2/MINGW64 bauen, ~15 Min
./scripts/build-uxplay.ps1

# 3. Runtime-DLLs sammeln (~232 MB)
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

## Lizenz

**GPL v3.** Da Spieglein UxPlay (GPL v3) einbindet, steht das gesamte Projekt unter GPL v3. Siehe [LICENSE](LICENSE).

Drittanbieter-Lizenzen siehe [NOTICE.md](NOTICE.md).

## Disclaimer

Spieglein ist **nicht von Apple Inc.** AirPlay ist eine eingetragene Marke von Apple Inc.

---

© 2026 Ostrong Studios — kontakt: support@ostrongstudios.de
