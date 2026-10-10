#!/usr/bin/env bash
# Disable the mod (restore clean gameinfo.gi). Also remove -insecure from launch options.
set -euo pipefail
G="${1:-$HOME/.local/share/Steam/steamapps/common/Counter-Strike Global Offensive/game/csgo}"
STATE="${XDG_STATE_HOME:-$HOME/.local/state}/csbot"
cp "$STATE/gameinfo.gi.online" "$G/gameinfo.gi"
echo "Online mode: mod disabled. Remove -insecure before matchmaking."
