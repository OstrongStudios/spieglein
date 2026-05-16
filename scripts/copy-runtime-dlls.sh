#!/usr/bin/env bash
# Kopiert alle Runtime-Dependencies von uxplay.exe in dasselbe Verzeichnis,
# so dass die EXE ohne MSYS2-PATH lauffaehig ist.
#
# Sammelt:
#   - Direkte + transitive DLL-Deps von uxplay.exe via `ldd`  (nur /mingw64/...)
#   - Alle GStreamer-Plugins aus /mingw64/lib/gstreamer-1.0/
#   - Plugin-Helper-EXEs aus /mingw64/libexec/gstreamer-1.0/
#   - dnssd.dll aus vendor/bonjour-sdk/Bin/x64/

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
UXPLAY_DIR="$REPO_ROOT/src/AirPlayReceiver.App/uxplay"
GST_PLUGIN_DIR="$UXPLAY_DIR/gstreamer-1.0"
BONJOUR_DLL="$REPO_ROOT/vendor/bonjour-sdk/Bin/x64/dnssd.dll"
MINGW_BIN="/mingw64/bin"
MINGW_GST_PLUGINS="/mingw64/lib/gstreamer-1.0"
MINGW_GST_LIBEXEC="/mingw64/libexec/gstreamer-1.0"

if [ ! -f "$UXPLAY_DIR/uxplay.exe" ]; then
  echo "FEHLER: uxplay.exe fehlt unter $UXPLAY_DIR" >&2
  exit 1
fi

mkdir -p "$GST_PLUGIN_DIR"

declare -A VISITED

collect_deps() {
  local file="$1"
  local base
  for dep in $(ldd "$file" 2>/dev/null | awk '/=> \/mingw64\// {print $3}'); do
    base=$(basename "$dep")
    if [ -n "${VISITED[$base]:-}" ]; then continue; fi
    VISITED[$base]=1
    cp -n "$dep" "$UXPLAY_DIR/"
    collect_deps "$dep"
  done
}

echo ">>> Sammele Deps von uxplay.exe..."
collect_deps "$UXPLAY_DIR/uxplay.exe"

echo ">>> Kopiere GStreamer-Plugins (nur .dll, keine Header/pkgconfig)..."
find "$MINGW_GST_PLUGINS" -maxdepth 1 -name '*.dll' -exec cp -n {} "$GST_PLUGIN_DIR/" \;

# Plugins haben selbst DLL-Deps. ldd auf jedes Plugin, transitive Deps in UXPLAY_DIR.
echo ">>> Sammele Plugin-Deps..."
for plugin in "$GST_PLUGIN_DIR"/*.dll; do
  collect_deps "$plugin"
done

echo ">>> Kopiere GStreamer-Helper-EXEs..."
if [ -d "$MINGW_GST_LIBEXEC" ]; then
  mkdir -p "$UXPLAY_DIR/gst-libexec"
  cp -r "$MINGW_GST_LIBEXEC/." "$UXPLAY_DIR/gst-libexec/"
  for helper in "$UXPLAY_DIR/gst-libexec"/*.exe; do
    [ -f "$helper" ] && collect_deps "$helper"
  done
fi

echo ">>> Kopiere dnssd.dll + mDNSResponder.exe (Bonjour)..."
BONJOUR_EXE="$REPO_ROOT/vendor/bonjour-sdk/Bin/x64/mDNSResponder.exe"
if [ -f "$BONJOUR_DLL" ]; then
  cp -n "$BONJOUR_DLL" "$UXPLAY_DIR/"
else
  echo "WARNUNG: $BONJOUR_DLL fehlt — erst build-bonjour-sdk.ps1 ausfuehren." >&2
fi
if [ -f "$BONJOUR_EXE" ]; then
  cp -n "$BONJOUR_EXE" "$UXPLAY_DIR/"
  # Deps von mDNSResponder.exe auch einsammeln
  collect_deps "$BONJOUR_EXE"
else
  echo "WARNUNG: $BONJOUR_EXE fehlt — erst build-bonjour-sdk.ps1 ausfuehren." >&2
fi

# Bei mDNSResponder + libstdc++ Sonderfall: explizit nachziehen
for must in libstdc++-6.dll libgcc_s_seh-1.dll libwinpthread-1.dll; do
  if [ -f "$MINGW_BIN/$must" ] && [ ! -f "$UXPLAY_DIR/$must" ]; then
    cp "$MINGW_BIN/$must" "$UXPLAY_DIR/"
  fi
done

dll_count=$(find "$UXPLAY_DIR" -maxdepth 1 -name '*.dll' | wc -l)
plugin_count=$(find "$GST_PLUGIN_DIR" -maxdepth 1 -name '*.dll' | wc -l)
total_size=$(du -sh "$UXPLAY_DIR" | cut -f1)

echo ""
echo "OK: $dll_count Top-Level-DLLs, $plugin_count GStreamer-Plugins, Gesamtgroesse $total_size"
echo "Ziel: $UXPLAY_DIR"
