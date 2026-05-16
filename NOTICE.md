# Third-Party Notices

Spieglein bundles or links to the following third-party software. The full text of each license is referenced.

## GPL v3

- **[UxPlay](https://github.com/FDH2/UxPlay)** — AirPlay protocol implementation.
  Copyright © 2019–2026 The UxPlay authors.
  Source: https://github.com/FDH2/UxPlay
  License: GPL v3 — see [LICENSE](LICENSE)

Because Spieglein dynamically links UxPlay, **the entire Spieglein binary distribution is licensed under GPL v3**.

## Apache 2.0

- **[mDNSResponder](https://github.com/apple-oss-distributions/mDNSResponder)** — Open-source mDNS daemon by Apple.
  Copyright © Apple Inc.
  Source: https://github.com/apple-oss-distributions/mDNSResponder (branch `rel/mDNSResponder-2881`)
  License: Apache 2.0 — https://www.apache.org/licenses/LICENSE-2.0

  **Modifications:** A patch from [leapbtw/uxplay-windows](https://github.com/leapbtw/uxplay-windows/blob/main/mdnsresponder-patches/2881.patch) (Windows build fixes) plus our own patch to `mDNSWindows/DLL/dllmain.c` (`IsSystemServiceDisabled` always returns FALSE, so the daemon can run as a per-user process). See `scripts/build-bonjour-sdk.ps1` for the build procedure.

## LGPL v2.1+

- **[GStreamer](https://gstreamer.freedesktop.org/) 1.24+** — Media framework.
  Copyright © 2003–2026 GStreamer Developers and contributors.
  License: LGPL v2.1+
  Plugins: `gst-plugins-base`, `gst-plugins-good`, `gst-plugins-bad`, `gst-plugins-ugly`, `gst-libav`.

  Some plugins implement patented codecs (H.264/H.265, AAC). Distribution as part of free non-commercial software is generally accepted; commercial users should review patent obligations independently.

## MIT

- **[WinUI 3 / Windows App SDK](https://github.com/microsoft/WindowsAppSDK)** — UI framework.
  Copyright © Microsoft Corporation.
  License: MIT

- **[.NET 8 Runtime](https://github.com/dotnet/runtime)** — Application runtime.
  Copyright © .NET Foundation.
  License: MIT
