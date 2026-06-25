#!/usr/bin/env bash
# Run the published Baballonia.Desktop under Steam Linux Runtime 4 (from the Steam
# folder) — i.e. the real container the app ships into. Build first with
# scripts/steamrt4-build.sh.
#
# Usage: scripts/steamrt4-run.sh [linux-x64|linux-arm64]   (default linux-x64)
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

ARCH="${1:-linux-x64}"
RT="${STEAMRT4:-$HOME/.local/share/Steam/steamapps/common/SteamLinuxRuntime_4}"
APP="$PWD/src/Baballonia.Desktop/bin/Release/net10.0/$ARCH/publish"

if [ ! -x "$RT/_v2-entry-point" ]; then
  echo "Steam Linux Runtime 4 not found at: $RT" >&2
  echo "Install it with: steam steam://install/4183110   (or set \$STEAMRT4 to its path)" >&2
  exit 1
fi
if [ ! -x "$APP/Baballonia.Desktop" ]; then
  echo "No build at: $APP" >&2
  echo "Run the 'Build (steamrt4 SDK)' button / scripts/steamrt4-build.sh $ARCH first." >&2
  exit 1
fi

echo ">> launching Baballonia.Desktop under Steam Runtime 4 ($RT)"
cd "$APP"   # content root must be the publish dir so models/Calibration resolve
exec "$RT/_v2-entry-point" --verb=run -- ./Baballonia.Desktop
