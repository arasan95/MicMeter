#!/bin/bash
set -euo pipefail

# Installs the MicMeter Loopback driver system-wide and restarts coreaudiod so
# the new CoreAudio HAL plugin is picked up. macOS only loads HAL plugins from
# /Library/Audio/Plug-Ins/HAL (the per-user ~/Library/Audio/Plug-Ins/HAL path is
# not scanned by coreaudiod), so this must run with sudo.

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BUNDLE="$ROOT/artifacts/MicMeter.driver"
DEST_DIR="/Library/Audio/Plug-Ins/HAL"
DEST="$DEST_DIR/MicMeter.driver"

if [ ! -d "$BUNDLE" ]; then
  echo "Driver bundle not found. Run scripts/build-loopback-driver.sh first." >&2
  exit 1
fi

if [ "$(id -u)" -ne 0 ]; then
  echo "Installing to $DEST_DIR requires root. Re-run with sudo." >&2
  echo "  sudo \"$0\"" >&2
  exit 1
fi

mkdir -p "$DEST_DIR"
# Remove any previous installs (current name and the older "MicMeter Loopback" name).
rm -rf "$DEST" "$DEST_DIR/MicMeter Loopback.driver"
cp -R "$BUNDLE" "$DEST"
echo "Installed $DEST"

echo
echo "Restarting coreaudiod is required for macOS to load the new plugin."
read -r -p "Restart coreaudiod now? This may briefly interrupt audio. [y/N] " answer
if [[ "$answer" =~ ^[Yy]$ ]]; then
  pkill -9 coreaudiod
  echo "coreaudiod restarted."
else
  echo "Skipped. Restart manually with: sudo pkill -9 coreaudiod"
fi
