#!/bin/bash
set -euo pipefail

# Builds the MicMeter Mute LV2 plugin as a self-contained .lv2 bundle, and the
# shared-memory helper dylib used by the MicMeter app. No LV2 SDK is required.

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/plugins/MicMeterMute"
OUT="${OUT:-$ROOT/artifacts}/MicMeterMute.lv2"
HELPER_OUT="$ROOT/artifacts/libmicmeter_shm.dylib"
ARCH="${ARCH:-arm64}"

mkdir -p "$OUT"

clang -shared -fPIC -O2 \
  -arch "$ARCH" \
  -mmacosx-version-min=12.0 \
  -o "$OUT/micmeter_mute.so" \
  "$SRC/micmeter_mute.c"

clang -dynamiclib -O2 \
  -arch "$ARCH" \
  -mmacosx-version-min=12.0 \
  -install_name "@rpath/libmicmeter_shm.dylib" \
  -o "$HELPER_OUT" \
  "$SRC/micmeter_shm_helper.c"

cp "$SRC/manifest.ttl" "$SRC/dsp.ttl" "$OUT/"

echo "Built $OUT"
echo "Built $HELPER_OUT"
