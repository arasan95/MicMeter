#!/bin/bash
set -euo pipefail

# Builds the MicMeter Loopback CoreAudio HAL driver as an ad-hoc signed .driver
# bundle. The driver is derived from BlackHole (GPL-3.0); see the bundled
# LICENSE file and THIRD-PARTY-NOTICES.md.

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC_DIR="$ROOT/drivers/MicMeterLoopback"
OUT_DIR="${OUT_DIR:-$ROOT/artifacts}"
BUNDLE="$OUT_DIR/MicMeter.driver"
ARCH="${ARCH:-arm64}"

SDKROOT="$(xcrun --sdk macosx --show-sdk-path)"

mkdir -p "$BUNDLE/Contents/MacOS" "$BUNDLE/Contents/Resources"

clang -bundle -Os \
  -arch "$ARCH" \
  -isysroot "$SDKROOT" \
  -mmacosx-version-min=12.0 \
  -DkDriver_Name=\"MicMeter\" \
  -DkHas_Driver_Name_Format=false \
  -DkDevice_Name=\"MicMeter\" \
  -DkManufacturer_Name=\"MicMeter\" \
  -framework CoreAudio \
  -framework CoreFoundation \
  -framework Accelerate \
  "$SRC_DIR/MicMeterLoopback.c" \
  -o "$BUNDLE/Contents/MacOS/MicMeterLoopback"

cp "$SRC_DIR/Info.plist" "$BUNDLE/Contents/Info.plist"

codesign --force --sign - "$BUNDLE"

echo "Built $BUNDLE"
