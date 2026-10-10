#!/usr/bin/env bash
# Installs dist/game/csgo (from build.sh) into the local CS2 and switches to bot mode.
# Re-run after every build; it updates in place. Usage: install-linux.sh [csgo_dir]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
G="${1:-$HOME/.local/share/Steam/steamapps/common/Counter-Strike Global Offensive/game/csgo}"
DIST="$ROOT/dist/game/csgo"
STATE="${XDG_STATE_HOME:-$HOME/.local/state}/csbot"

[ -f "$G/gameinfo.gi" ] || { echo "CS2 not found at $G"; exit 1; }
[ -d "$DIST" ] || { echo "No build at $DIST - run scripts/build.sh first"; exit 1; }
mkdir -p "$STATE"

# Clean (online) gameinfo.gi: take the live one unless it is already patched.
if grep -q 'addons/metamod' "$G/gameinfo.gi"; then
  [ -f "$STATE/gameinfo.gi.online" ] || { echo "gameinfo.gi is patched but no clean backup in $STATE"; exit 1; }
else
  cp "$G/gameinfo.gi" "$STATE/gameinfo.gi.online"
fi
[ -d "$STATE/cfg.orig" ] || cp -a "$G/cfg" "$STATE/cfg.orig"

# --delete only inside mod-owned trees; keep CSS logs and plugin configs users may edit.
rsync -a --delete --exclude 'logs/' --exclude 'counterstrikesharp/configs/' "$DIST/addons/" "$G/addons/"
rsync -a --delete "$DIST/overrides/" "$G/overrides/"
rsync -a "$DIST/cfg/" "$G/cfg/"

python3 -I - "$STATE/gameinfo.gi.online" "$STATE/gameinfo.gi.bots" <<'EOF'
import sys
src, out = sys.argv[1], sys.argv[2]
t = open(src, newline='').read()
anchor = "Game_LowViolence\tcsgo_lv // Perfect World content override"
i = t.index(anchor); j = t.index("\n", i) + 1
nl = "\r\n" if "\r\n" in t else "\n"
ins = f"{nl}\t\t\tGame\tcsgo/overrides/botprofile.vpk{nl}\t\t\tGame\tcsgo/addons/metamod{nl}"
open(out, "w", newline='').write(t[:j] + ins + t[j:])
EOF
cp "$STATE/gameinfo.gi.bots" "$G/gameinfo.gi"

echo "Installed; bot mode ON. Launch options: SDL_VIDEO_DRIVER=wayland %command% -insecure"
echo "Before online play: scripts/online.sh and remove -insecure."
