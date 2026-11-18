#!/usr/bin/env bash
# Builds InkDot for Windows (app + driver + plugin, .NET runtime included) into dist/InkDot-win-<arch>/
# and zips it. Runs on macOS, Linux or Windows (Git Bash). For a setup.exe, open installer/inkdot.iss
# in Inno Setup on Windows after running this.
# Usage: scripts/package-win.sh            (x64)
#        RID=win-arm64 scripts/package-win.sh
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

RID="${RID:-win-x64}"
VERSION="$(sed -n 's/^VERSION="\(.*\)"/\1/p' scripts/package-mac.sh)"
OUT="dist/InkDot-${RID}"

rm -rf "$OUT"
mkdir -p "$OUT"
echo "Building the InkDot app for $RID…"
dotnet publish src/Lveno.App -c Release -r "$RID" --self-contained -o "$OUT" -nologo -clp:ErrorsOnly
echo "Building the driver…"
dotnet publish external/OpenTabletDriver/OpenTabletDriver.Daemon -c Release -r "$RID" --self-contained -o "$OUT" -nologo -clp:ErrorsOnly

# The app's .exe is named after the product. (The apphost still loads Lveno.dll next to it.)
mv "$OUT/Lveno.exe" "$OUT/InkDot.exe"
rm -f "$OUT"/*.pdb
cp external/OpenTabletDriver/LICENSE "$OUT/OpenTabletDriver-LICENSE.txt"
cp LICENSE "$OUT/LICENSE.txt"
cp THIRD-PARTY-NOTICES.md "$OUT/" 2>/dev/null || true

ZIP="dist/InkDot-${VERSION}-${RID}.zip"
rm -f "$ZIP"
(cd dist && zip -qr "$(basename "$ZIP")" "$(basename "$OUT")")
echo "Done: $OUT and $ZIP ($(du -h "$ZIP" | cut -f1))"
