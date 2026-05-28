#!/usr/bin/env bash
# Baut UxPlay aus Quelle im MSYS2/MINGW64-Environment.
# Aufruf aus Projekt-Root: bash scripts/build-uxplay.sh [git-ref]

set -euo pipefail

UXPLAY_REF="${1:-master}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SRC_DIR="$REPO_ROOT/build/uxplay-src"
OUT_DIR="$REPO_ROOT/src/AirPlayReceiver.App/uxplay"

mkdir -p "$SRC_DIR" "$OUT_DIR"

cd "$SRC_DIR"

if [ ! -d UxPlay/.git ]; then
  echo ">>> UxPlay-Repo klonen..."
  git clone https://github.com/FDH2/UxPlay.git
fi

cd UxPlay
echo ">>> Fetch & checkout $UXPLAY_REF"
git fetch --all --tags --quiet
git checkout "$UXPLAY_REF"
git pull --ff-only || true

echo ">>> CMake configure"
rm -rf build
mkdir build
cd build

# BONJOUR_SDK_HOME wird ggf. vom PS-Wrapper gesetzt; CMakeLists prüft ENV.
if [ -n "${BONJOUR_SDK_HOME:-}" ]; then
  echo "    BONJOUR_SDK_HOME=$BONJOUR_SDK_HOME"
fi

# -DNO_MARCH_NATIVE=ON: KRITISCH — sonst kompiliert GCC mit -march=native auf
# Build-Host-CPU-Instruktionen (AVX2 etc.), die aelter CPUs (z. B. Ivy Bridge 2012)
# nicht koennen. Fuehrt zu STATUS_ILLEGAL_INSTRUCTION (0xC000001D) Crashes auf
# Win10 22H2 mit Hardware vor ~2013.
cmake -G "MSYS Makefiles" -DCMAKE_BUILD_TYPE=Release -DNO_MARCH_NATIVE=ON ..

echo ">>> Build (-j$(nproc))"
make -j"$(nproc)"

echo ">>> Copy uxplay.exe -> $OUT_DIR"
cp uxplay.exe "$OUT_DIR/"

echo ""
echo "OK: $OUT_DIR/uxplay.exe"
