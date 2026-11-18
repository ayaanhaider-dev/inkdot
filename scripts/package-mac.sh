#!/usr/bin/env bash
# Builds "InkDot.app" (InkDot by Lveno: app + driver + plugin, .NET runtime included) into dist/.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

RID="${RID:-osx-arm64}"
APP="dist/InkDot.app"
OUT="$APP/Contents/MacOS"
VERSION="0.2.0"

rm -rf "$APP"
mkdir -p "$OUT" "$APP/Contents/Resources"

echo "Building the Lveno app…"
dotnet publish src/Lveno.App -c Release -r "$RID" --self-contained -o "$OUT" -nologo -clp:ErrorsOnly
echo "Building the driver…"
dotnet publish external/OpenTabletDriver/OpenTabletDriver.Daemon -c Release -r "$RID" --self-contained -o "$OUT" -nologo -clp:ErrorsOnly

cp src/Lveno.App/Assets/lveno.icns "$APP/Contents/Resources/lveno.icns"
cp external/OpenTabletDriver/LICENSE "$APP/Contents/Resources/OpenTabletDriver-LICENSE.txt"
cp THIRD-PARTY-NOTICES.md "$APP/Contents/Resources/" 2>/dev/null || true

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>InkDot</string>
  <key>CFBundleDisplayName</key><string>InkDot</string>
  <key>CFBundleIdentifier</key><string>agency.lveno.inkdot</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundleExecutable</key><string>Lveno</string>
  <key>CFBundleIconFile</key><string>lveno.icns</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.graphics-design</string>
</dict>
</plist>
PLIST

# Sign with a stable identity when there is one, so macOS keeps Input Monitoring and Accessibility
# across rebuilds. An ad-hoc signature changes every build, and macOS then asks for permissions again.
IDENTITY="${LVENO_SIGN_IDENTITY:-Lveno Dev}"
if security find-identity -v -p codesigning | grep -q "\"$IDENTITY\""; then
  codesign --force --deep --sign "$IDENTITY" "$APP" && echo "Signed with \"$IDENTITY\" (permissions survive rebuilds)."
else
  codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || true
  echo "Signed ad-hoc. Create a \"$IDENTITY\" code-signing certificate so permissions survive rebuilds."
fi
echo "Done: $APP"
