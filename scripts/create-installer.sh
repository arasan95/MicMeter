#!/bin/bash
set -euo pipefail

# create-installer.sh — creates a macOS .pkg installer from pre-built artifacts.
#
# The .pkg installs:
#   - /Applications/MicMeter.app
#   - /Library/Audio/Plug-Ins/HAL/MicMeter.driver  (restarts coreaudiod)
#
# The LV2 plugin is auto-installed to ~/Library/Audio/Plug-Ins/LV2 by the app
# on first launch, so it is NOT included in the .pkg.
#
# Usage:
#   bash scripts/create-installer.sh              # build + package
#   bash scripts/create-installer.sh --skip-build # use existing artifacts/
#
# Output: dist/MicMeter-<version>.pkg

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SKIP_BUILD=0
VERSION="0.3.0"

for arg in "$@"; do
  case "$arg" in
    --skip-build) SKIP_BUILD=1 ;;
    --version=*) VERSION="${arg#*=}" ;;
    --help|-h)
      echo "Usage: $0 [--skip-build] [--version=X.Y.Z]"
      exit 0 ;;
  esac
done

# ── Build ──────────────────────────────────────────────────────────────────

if [ "$SKIP_BUILD" -eq 0 ]; then
  echo "== Building =="
  bash "$ROOT/scripts/build-loopback-driver.sh"
  bash "$ROOT/scripts/build-mute-plugin.sh"
  dotnet publish \
    -c Release -f net10.0 -r osx-arm64 --self-contained true \
    "$ROOT/src/MicMeter/MicMeter.csproj" \
    -o "$ROOT/artifacts/MicMeter-osx-arm64" \
    2>&1 | grep -iE "error|warning" || true
  bash "$ROOT/scripts/package-macos.sh" "$ROOT/artifacts/MicMeter-osx-arm64" "$ROOT/artifacts"
fi

# ── Verify artifacts ───────────────────────────────────────────────────────

APP="$ROOT/artifacts/MicMeter.app"
DRIVER="$ROOT/artifacts/MicMeter.driver"

if [ ! -d "$APP" ]; then
  echo "ERROR: $APP not found. Run without --skip-build first." >&2
  exit 1
fi

echo
echo "== Creating installer =="
echo "  Version: $VERSION"

STAGING="$ROOT/dist/pkg-staging"
rm -rf "$STAGING"
mkdir -p "$STAGING/root/Applications" \
         "$STAGING/root/Library/Audio/Plug-Ins/HAL" \
         "$STAGING/scripts"

# ── Prepare payload ────────────────────────────────────────────────────────

cp -R "$APP" "$STAGING/root/Applications/"

if [ -d "$DRIVER" ]; then
  cp -R "$DRIVER" "$STAGING/root/Library/Audio/Plug-Ins/HAL/"
fi

# ── Postinstall script (restart coreaudiod) ────────────────────────────────

cat > "$STAGING/scripts/postinstall" <<'POSTINST'
#!/bin/bash
set -euo pipefail
echo "Restarting coreaudiod for the MicMeter Loopback driver..."
pkill -9 coreaudiod || true
sleep 1
echo "Done."
exit 0
POSTINST
chmod +x "$STAGING/scripts/postinstall"

# ── Component: App ─────────────────────────────────────────────────────────

echo "  [1/3] Packaging app component..."
pkgbuild \
  --root "$STAGING/root" \
  --identifier com.arasan95.MicMeter \
  --version "$VERSION" \
  --install-location "/" \
  "$STAGING/MicMeter-app.pkg"

# ── Component: Driver (with postinstall) ───────────────────────────────────

echo "  [2/3] Packaging driver component..."
pkgbuild \
  --root "$STAGING/root/Library/Audio/Plug-Ins/HAL/MicMeter.driver" \
  --identifier com.arasan95.MicMeterLoopback \
  --version "$VERSION" \
  --install-location "/Library/Audio/Plug-Ins/HAL" \
  --scripts "$STAGING/scripts" \
  "$STAGING/MicMeter-driver.pkg"

# ── Distribution XML ───────────────────────────────────────────────────────

cat > "$STAGING/Distribution" <<DISTXML
<?xml version="1.0" encoding="utf-8"?>
<installer-gui-script minSpecVersion="2">
    <title>MicMeter ${VERSION}</title>
    <options customize="never" require-scripts="false" hostArchitectures="arm64"/>
    <domains enable_localSystem="true"/>
    <choices-outline>
        <line choice="app">
            <line choice="app.pkg"/>
        </line>
        <line choice="driver">
            <line choice="driver.pkg"/>
        </line>
    </choices-outline>
    <choice id="app" title="MicMeter Application" enabled="true" selected="true">
        <description>The audio level meter overlay.</description>
    </choice>
    <choice id="app.pkg" visible="false">
        <pkg-ref id="com.arasan95.MicMeter"/>
    </choice>
    <choice id="driver" title="MicMeter Loopback Driver" enabled="true" selected="true">
        <description>Virtual audio device for capturing system audio. Requires admin rights. Restarts coreaudiod.</description>
    </choice>
    <choice id="driver.pkg" visible="false">
        <pkg-ref id="com.arasan95.MicMeterLoopback"/>
    </choice>
    <pkg-ref id="com.arasan95.MicMeter" version="${VERSION}">MicMeter-app.pkg</pkg-ref>
    <pkg-ref id="com.arasan95.MicMeterLoopback" version="${VERSION}">MicMeter-driver.pkg</pkg-ref>
</installer-gui-script>
DISTXML

# ── Product archive ────────────────────────────────────────────────────────

echo "  [3/3] Building product archive..."
productbuild \
  --distribution "$STAGING/Distribution" \
  --package-path "$STAGING" \
  --version "$VERSION" \
  "$ROOT/dist/MicMeter-${VERSION}.pkg"

# ── Cleanup ────────────────────────────────────────────────────────────────

rm -rf "$STAGING"

echo
echo "== Done =="
echo "  Installer: $ROOT/dist/MicMeter-${VERSION}.pkg"
echo "  Size: $(du -h "$ROOT/dist/MicMeter-${VERSION}.pkg" | cut -f1)"
