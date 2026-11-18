#!/usr/bin/env bash
# Builds dist/InkDot-<version>.dmg: a branded drag-to-Applications installer.
# Usage: scripts/make-dmg.sh            (packages the app first)
#        scripts/make-dmg.sh --no-build (uses the existing dist/InkDot.app)
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="$(sed -n 's/^VERSION="\(.*\)"/\1/p' scripts/package-mac.sh)"
APP="dist/InkDot.app"
DMG="dist/InkDot-${VERSION}.dmg"
WORK="dist/.dmg"

[[ "${1:-}" == "--no-build" ]] || ./scripts/package-mac.sh
[[ -d "$APP" ]] || { echo "No $APP. Run scripts/package-mac.sh first."; exit 1; }

# dmgbuild lays out the window without driving Finder.
if [[ ! -x .venv-dmg/bin/dmgbuild ]]; then
  python3 -m venv .venv-dmg
  .venv-dmg/bin/pip install -q dmgbuild
fi

# Background at 1x and 2x, packed into one TIFF so it's sharp on Retina.
mkdir -p "$WORK"
export FONTCONFIG_FILE="$PWD/scripts/dmg/fonts.conf" PANGOCAIRO_BACKEND=fc
sed "s/v0\.1\.0/v${VERSION}/" scripts/dmg/background.svg > "$WORK/background.svg"
rsvg-convert -w 640 -h 420 "$WORK/background.svg" -o "$WORK/bg.png"
rsvg-convert -w 1280 -h 840 "$WORK/background.svg" -o "$WORK/bg@2x.png"
tiffutil -cathidpicheck "$WORK/bg.png" "$WORK/bg@2x.png" -out "$WORK/background.tiff" >/dev/null

rm -f "$DMG"
.venv-dmg/bin/dmgbuild -s scripts/dmg/settings.py \
  -D app="$APP" -D background="$WORK/background.tiff" \
  "InkDot" "$DMG"

# Give the .dmg file itself the InkDot icon.
if command -v osascript >/dev/null; then
  osascript -l JavaScript -e "ObjC.import('AppKit'); \$.NSWorkspace.sharedWorkspace.setIconForFileOptions(\$.NSImage.alloc.initWithContentsOfFile('$PWD/src/Lveno.App/Assets/lveno.icns'), '$PWD/$DMG', 0)" >/dev/null || true
fi

IDENTITY="${LVENO_SIGN_IDENTITY:-Lveno Dev}"
if security find-identity -v -p codesigning | grep -q "\"$IDENTITY\""; then
  codesign --force --sign "$IDENTITY" "$DMG"
fi
rm -rf "$WORK"
echo "Done: $DMG ($(du -h "$DMG" | cut -f1))"
