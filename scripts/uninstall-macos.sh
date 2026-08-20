#!/bin/bash
set -euo pipefail

# Removes MicMeter from macOS:
#   - /Applications/MicMeter.app
#   - ~/Library/Audio/Plug-Ins/LV2/MicMeterMute.lv2
#   - /Library/Audio/Plug-Ins/HAL/MicMeter.driver (+ legacy "MicMeter Loopback" name)
#   - ~/Library/Application Support/MicMeter (settings)
#   - /tmp/micmeter_debug.log
#   - the shared-memory objects (/micmeter_shared*, clear on reboot anyway)
#
# The HAL driver removal requires admin rights; the script performs the
# user-level clean-up first and then re-runs itself with sudo for the
# root-only part.

ROOT_ONLY=0
if [ "${1:-}" = "--root" ]; then
  ROOT_ONLY=1
fi

if [ "$ROOT_ONLY" -eq 1 ]; then
  echo "Removing the MicMeter Loopback HAL driver (admin)..."
  rm -rf /Library/Audio/Plug-Ins/HAL/MicMeter.driver \
         "/Library/Audio/Plug-Ins/HAL/MicMeter Loopback.driver"
  echo "Restarting coreaudiod so the driver is unloaded..."
  pkill -9 coreaudiod || true
  echo "coreaudiod restarted."
  exit 0
fi

echo "== MicMeter uninstall =="

# Quit the app if it is running.
pkill -x MicMeter 2>/dev/null || true
sleep 1

# App
rm -rf /Applications/MicMeter.app
echo "Removed app -> /Applications/MicMeter.app"

# LV2 plugin (user-level)
rm -rf "$HOME/Library/Audio/Plug-Ins/LV2/MicMeterMute.lv2"
echo "Removed plugin -> $HOME/Library/Audio/Plug-Ins/LV2/MicMeterMute.lv2"

# Settings and debug log
rm -rf "$HOME/Library/Application Support/MicMeter"
rm -f /tmp/micmeter_debug.log
echo "Removed settings and debug log."

# Shared memory objects (best effort; they are cleared on reboot anyway).
if command -v python3 >/dev/null 2>&1; then
  python3 - <<'PY' || true
import ctypes
try:
    lib = ctypes.CDLL(None)
    for name in (b"/micmeter_shared", b"/micmeter_shared_ring"):
        fn = getattr(lib, "shm_unlink", None)
        if fn:
            fn(name)
except Exception:
    pass
PY
fi

# HAL driver needs admin.
echo
if [ "$(id -u)" -eq 0 ]; then
  bash "$0" --root
else
  echo "Removing the loopback HAL driver requires admin rights."
  echo "Re-running with sudo (prompts for your password)..."
  sudo bash "$0" --root
fi

echo
echo "MicMeter has been uninstalled."