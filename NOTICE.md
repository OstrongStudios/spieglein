# Third-Party Notices

Spieglein bundles or links to the following third-party software. The full text of each license is referenced.

## GPL v3

- **[UxPlay](https://github.com/FDH2/UxPlay) 1.73.6+** — AirPlay protocol implementation.
  Copyright © 2019–2026 The UxPlay authors.
  Source: https://github.com/FDH2/UxPlay
  License: GPL v3 — see [LICENSE](LICENSE)

  **Build flags:** Compiled with `-DNO_MARCH_NATIVE=ON` so the binary uses only the
  baseline x86-64 instruction set instead of the build-host's specific CPU
  extensions. Without this, the resulting `uxplay.exe` would crash with
  `STATUS_ILLEGAL_INSTRUCTION` (`0xC000001D`) on CPUs older than the build host
  (e.g. Intel Ivy Bridge). See `scripts/build-uxplay.sh`.

Because Spieglein dynamically links UxPlay, **the entire Spieglein binary distribution is licensed under GPL v3**.

## Apache 2.0

- **[mDNSResponder](https://github.com/apple-oss-distributions/mDNSResponder)** — Open-source mDNS daemon by Apple.
  Copyright © Apple Inc.
  Source: https://github.com/apple-oss-distributions/mDNSResponder (branch `rel/mDNSResponder-2881`)
  License: Apache 2.0 — https://www.apache.org/licenses/LICENSE-2.0

  **Modifications applied at build time:**
  1. A patch from [leapbtw/uxplay-windows](https://github.com/leapbtw/uxplay-windows/blob/main/mdnsresponder-patches/2881.patch) — Windows build fixes for vcxproj and source files.
  2. Our own patch to `mDNSWindows/DLL/dllmain.c`: `IsSystemServiceDisabled` always returns `FALSE`. Without this, the client (dnssd.dll) and server (mDNSResponder.exe) negotiate different TCP ports (53545 vs. 5354) when no Windows service is registered, causing `kDNSServiceErr_ServiceNotRunning`. With the patch, both speak port 5354.
  3. `<UACExecutionLevel>` in `mDNSResponder.vcxproj` changed from `RequireAdministrator` to `AsInvoker` so the daemon can start as an unprivileged child process from an MSIX-packaged app.

  See `scripts/build-bonjour-sdk.ps1` for the automated build procedure.

## LGPL v2.1+

- **[GStreamer](https://gstreamer.freedesktop.org/) 1.24+** — Media framework.
  Copyright © 2003–2026 GStreamer Developers and contributors.
  License: LGPL v2.1+
  Plugins bundled: `gst-plugins-base`, `gst-plugins-good`, `gst-plugins-bad`, `gst-plugins-ugly`, `gst-libav`.

  Some plugins implement patented codecs (H.264/H.265, AAC). Distribution as part of free non-commercial software is generally accepted; commercial users should review patent obligations independently.

  **Plugin exclusions:** `libgstcodec2json.dll` is removed from the bundle (see `DENY_LIST` in `scripts/copy-runtime-dlls.sh`). It is a codec-analytics helper not needed for AirPlay reception and has a delay-loaded dependency on `libjson-glib-1.0-0.dll` which would otherwise need to be redistributed.

## MIT

- **[WinUI 3 / Windows App SDK](https://github.com/microsoft/WindowsAppSDK)** — UI framework.
  Copyright © Microsoft Corporation.
  License: MIT

- **[.NET 8 Runtime](https://github.com/dotnet/runtime)** — Application runtime.
  Copyright © .NET Foundation.
  License: MIT

## Disclaimer

AirPlay® is a registered trademark of Apple Inc. Spieglein is not affiliated with, endorsed by, or sponsored by Apple Inc. UxPlay's AirPlay protocol implementation is based on reverse engineering of the publicly observed protocol behaviour and does not include any Apple proprietary code or DRM-bypass mechanisms.
