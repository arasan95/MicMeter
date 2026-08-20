#!/bin/bash
set -euo pipefail

# Installs the MicMeter Mute LV2 plugin for the current user. The per-user
# LV2 directory is scanned by Carla/Element without root privileges.

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/artifacts/MicMeterMute.lv2"
DEST_DIR="$HOME/Library/Audio/Plug-Ins/LV2"
DEST="$DEST_DIR/MicMeterMute.lv2"

if [ ! -d "$SRC" ]; then
  echo "Plugin bundle not found. Run scripts/build-mute-plugin.sh first." >&2
  exit 1
fi

mkdir -p "$DEST_DIR"
rm -rf "$DEST"
cp -R "$SRC" "$DEST"
echo "Installed $DEST"
echo
echo "Restart Carla / Element to rescan LV2 plugins."
