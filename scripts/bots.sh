#!/usr/bin/env bash
# Re-enable the mod. Requires -insecure in launch options.
set -euo pipefail
G="${1:-$HOME/.local/share/Steam/steamapps/common/Counter-Strike Global Offensive/game/csgo}"
STATE="${XDG_STATE_HOME:-$HOME/.local/state}/csbot"
# A CS2 update rewrites gameinfo.gi; then our patched copy is stale.
if ! grep -q 'addons/metamod' "$G/gameinfo.gi" && ! cmp -s "$G/gameinfo.gi" "$STATE/gameinfo.gi.online"; then
  echo "gameinfo.gi changed (CS2 update?) - re-run scripts/install-linux.sh"; exit 1
fi
cp "$STATE/gameinfo.gi.bots" "$G/gameinfo.gi"
echo "Bot mode ON."
