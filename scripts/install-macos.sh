#!/bin/bash
set -euo pipefail

# Installs MicMeter on macOS in one step:
#   - the MicMeter.app bundle      -> /Applications
#   - the MicMeter Mute LV2 plugin -> ~/Library/Audio/Plug-Ins/LV2  (no admin)
#   - the MicMeter Loopback driver -> /Library/Audio/Plug-Ins/HAL   (admin)
#
# Expectations: scripts/build-loopback-driver.sh, scripts/build-mute-plugin.sh
# and scripts/package-macos.sh have been run (or a release archive was unpacked
# containing artifacts/MicMeter.app, artifacts/MicMeterMute.lv2 and
# artifacts/MicMeter.driver).

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP_SRC="$ROOT/artifacts/MicMeter.app"
PLUGIN_BUNDLE="$ROOT/artifacts/MicMeterMute.lv2"
DRIVER_BUNDLE="$ROOT/artifacts/MicMeter.driver"

APP_DEST="/Applications/MicMeter.app"
PLUGIN_DEST_DIR="$HOME/Library/Audio/Plug-Ins/LV2"
PLUGIN_DEST="$PLUGIN_DEST_DIR/MicMeterMute.lv2"
DRIVER_DEST="/Library/Audio/Plug-Ins/HAL/MicMeter.driver"

echo "== MicMeter install =="

# --- App ---
if [ ! -d "$APP_SRC" ]; then
  echo "App bundle not found at $APP_SRC. Run scripts/package-macos.sh first." >&2
  exit 1
fi

rm -rf "$APP_DEST"
cp -R "$APP_SRC" "$APP_DEST"
echo "Installed app -> $APP_DEST"

# --- LV2 mute plugin (user-level, no admin needed) ---
if [ -d "$PLUGIN_BUNDLE" ]; then
  mkdir -p "$PLUGIN_DEST_DIR"
  rm -rf "$PLUGIN_DEST"
  cp -R "$PLUGIN_BUNDLE" "$PLUGIN_DEST"
  echo "Installed plugin -> $PLUGIN_DEST"
else
  echo "Plugin bundle not found ($PLUGIN_BUNDLE); skipping." >&2
fi

# --- Loopback HAL driver (needs admin) ---
if [ -d "$DRIVER_BUNDLE" ]; then
  echo
  echo "Installing the MicMeter Loopback driver requires admin rights."
  if [ "$(id -u)" -eq 0 ]; then
    mkdir -p /Library/Audio/Plug-Ins/HAL
    rm -rf "$DRIVER_DEST" "/Library/Audio/Plug-Ins/HAL/MicMeter Loopback.driver"
    cp -R "$DRIVER_BUNDLE" "$DRIVER_DEST"
    echo "Installed driver -> $DRIVER_DEST"
    echo "Restarting coreaudiod for the driver to load..."
    pkill -9 coreaudiod || true
    echo "coreaudiod restarted."
  else
    echo "Re-running the driver installer with sudo (prompts for your password)."
    sudo "$ROOT/scripts/install-loopback-driver.sh"
  fi
  echo "Installed driver."
else
  echo "Driver bundle not found ($DRIVER_BUNDLE); skipping (not required for Plugin mode)." >&2
fi

echo
echo "Done. Launch /Applications/MicMeter.app"
echo "In Plugin mode, load the \"MicMeter Mute\" LV2 plugin in Carla/Element (see README)."