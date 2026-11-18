#!/usr/bin/env bash
# Live development: edit code, see it in the running app. No rebuilding or reinstalling.
#
#   ./scripts/dev.sh          start (or restart) the dev app with live reload
#   ./scripts/dev.sh plugin   rebuild the driver plugin and restart the driver
#
#   • XAML (.axaml) edits appear instantly (HotAvalonia).
#   • C# edits are applied while the app runs. If a change can't be applied live,
#     the app restarts by itself.
#
# The installed InkDot.app owns the driver because it holds the macOS permissions.
# This script makes sure that driver is running, then runs ONE dev copy of the app against it.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
# Same temp folder as apps opened from Finder: the driver's pipe and lock live there.
export TMPDIR="$(getconf DARWIN_USER_TEMP_DIR)"
PLUGINS="$HOME/Library/Application Support/InkDot/Plugins/Lveno"
INSTALLED_APP="InkDot"

stop_dev_copies() {
  # Old watchers (even from closed terminal tabs) and the apps they launched.
  pkill -f "dotnet watch --project src/Lveno.App" 2>/dev/null || true
  pkill -f "dotnet-watch.*Lveno.App" 2>/dev/null || true
  pkill -f "bin/Debug/net10.0/Lveno" 2>/dev/null || true
  sleep 1
}

ensure_driver() {
  if pgrep -x OpenTabletDriver.Daemon >/dev/null; then return; fi
  echo "Starting the driver from the installed app…"
  open -a "$INSTALLED_APP" --args --background
  for _ in $(seq 1 30); do pgrep -x OpenTabletDriver.Daemon >/dev/null && break; sleep 0.5; done
  sleep 2
  # Close the installed app's window; its driver keeps running.
  osascript -e "tell application \"$INSTALLED_APP\" to quit" >/dev/null 2>&1 || true
  pgrep -x OpenTabletDriver.Daemon >/dev/null && echo "Driver running." || echo "Driver didn't start. Open InkDot from Applications once."
}

stop_dev_copies

if [[ "${1:-}" == "plugin" ]]; then
  dotnet build src/Lveno.Plugins -c Debug -nologo -clp:ErrorsOnly
  mkdir -p "$PLUGINS"
  cp src/Lveno.Plugins/bin/Debug/net10.0/Lveno.Plugins.dll "$PLUGINS/"
  # The installed app copies its own plugin into the driver when it starts it, so keep that copy current too.
  INSTALLED="/Applications/$INSTALLED_APP.app/Contents/MacOS"
  [ -d "$INSTALLED" ] && cp src/Lveno.Plugins/bin/Debug/net10.0/Lveno.Plugins.dll "$INSTALLED/"
  pkill -x OpenTabletDriver.Daemon 2>/dev/null || true
  sleep 1
  ensure_driver
  echo "Plugin updated. Run ./scripts/dev.sh to start the app again."
  exit 0
fi

ensure_driver

# When this terminal closes, take the watcher and the app down with it.
trap 'pkill -f "dotnet-watch.*Lveno.App" 2>/dev/null; pkill -f "bin/Debug/net10.0/Lveno" 2>/dev/null; exit 0' HUP INT TERM EXIT

export DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1 LVENO_DEV=1
dotnet watch --project src/Lveno.App run -c Debug
