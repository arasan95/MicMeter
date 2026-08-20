#!/bin/bash
set -euo pipefail

# Packages a macOS .app bundle from a framework-dependent publish directory.
# Usage: package-macos.sh <publish-dir> <output-dir>

PUBLISH_DIR="${1:?usage: package-macos.sh <publish-dir> <output-dir>}"
OUT_DIR="${2:-dist}"
APP_NAME="MicMeter"
ICON_SRC="$(dirname "$0")/../src/MicMeter/Assets/app-icon.png"

APP="$OUT_DIR/$APP_NAME.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>MicMeter</string>
  <key>CFBundleDisplayName</key>
  <string>MicMeter</string>
  <key>CFBundleIdentifier</key>
  <string>com.arasan95.MicMeter</string>
  <key>CFBundleVersion</key>
  <string>0.3.0</string>
  <key>CFBundleShortVersionString</key>
  <string>0.3.0</string>
  <key>CFBundleExecutable</key>
  <string>MicMeter</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleIconFile</key>
  <string>MicMeter</string>
  <key>LSUIElement</key>
  <true/>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>NSMicrophoneUsageDescription</key>
  <string>MicMeter reads microphone levels to display live meters.</string>
</dict>
</plist>
PLIST

cp -R "$PUBLISH_DIR"/* "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/MicMeter"

# Embed the MicMeter Mute LV2 plugin so the app can self-install it without
# admin rights (~/Library/Audio/Plug-Ins/LV2 is scanned by LV2 hosts).
PLUGIN_SRC="$(dirname "$0")/../artifacts/MicMeterMute.lv2"
if [ -d "$PLUGIN_SRC" ]; then
  mkdir -p "$APP/Contents/Resources/LV2"
  rm -rf "$APP/Contents/Resources/LV2/MicMeterMute.lv2"
  cp -R "$PLUGIN_SRC" "$APP/Contents/Resources/LV2/MicMeterMute.lv2"
fi

TMP_DIR="$(mktemp -d)"
ICONSET="$TMP_DIR/MicMeter.iconset"
mkdir "$ICONSET"
sips -z 16 16   "$ICON_SRC" --out "$ICONSET/icon_16x16.png" >/dev/null
sips -z 32 32   "$ICON_SRC" --out "$ICONSET/icon_16x16@2x.png" >/dev/null
sips -z 32 32   "$ICON_SRC" --out "$ICONSET/icon_32x32.png" >/dev/null
sips -z 64 64   "$ICON_SRC" --out "$ICONSET/icon_32x32@2x.png" >/dev/null
sips -z 128 128 "$ICON_SRC" --out "$ICONSET/icon_128x128.png" >/dev/null
sips -z 256 256 "$ICON_SRC" --out "$ICONSET/icon_128x128@2x.png" >/dev/null
sips -z 256 256 "$ICON_SRC" --out "$ICONSET/icon_256x256.png" >/dev/null
sips -z 512 512 "$ICON_SRC" --out "$ICONSET/icon_256x256@2x.png" >/dev/null
sips -z 512 512 "$ICON_SRC" --out "$ICONSET/icon_512x512.png" >/dev/null
sips -z 1024 1024 "$ICON_SRC" --out "$ICONSET/icon_512x512@2x.png" >/dev/null
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/MicMeter.icns"
rm -rf "$TMP_DIR"

echo "Packaged $APP"
