#!/usr/bin/env bash
# Builds dist/game/csgo: pinned upstream Linux release as base (native bits + plugins we
# don't build yet), overlaid with the managed plugins we build from source.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

BASE_URL="https://github.com/ed0ard/CS2-Bot-Improver/releases/download/v1.4.5/CS2BotImprover_for_Linux.zip"
BASE_SHA256="7aa916e328209b4ac65dc2647336c7aa301cb34d6ec07b53eacf1d350ef588e8"
# Managed plugins built from source (submodules under addons/counterstrikesharp/plugins).
OWN_PLUGINS=(BotAI BotState HostageDirector)

CACHE="$ROOT/.cache"; OUT="$ROOT/dist/game/csgo"
PLUGINS="$ROOT/addons/counterstrikesharp/plugins"
mkdir -p "$CACHE"

base="$CACHE/base-v1.4.5.zip"
if [ ! -f "$base" ]; then curl -sSL -o "$base.part" "$BASE_URL" && mv "$base.part" "$base"; fi
echo "$BASE_SHA256  $base" | sha256sum -c --quiet

rm -rf "$ROOT/dist"; mkdir -p "$OUT"
unzip -q "$base" -d "$OUT"
rm -rf "$OUT/backup" "$OUT/gameinfo.gi"   # install script patches the live gameinfo.gi instead

# BotState references BotControllerApi.dll as a prebuilt; build it from the submodule.
"$DOTNET" build "$ROOT/addons/BotController/csharp/BotControllerApi/BotControllerApi.csproj" \
  -c Release -o "$CACHE/BotControllerApi" -nologo -v q
mkdir -p "$PLUGINS/BotState/libs"
cp "$CACHE/BotControllerApi/BotControllerApi.dll" "$PLUGINS/BotState/libs/"

for p in "${OWN_PLUGINS[@]}"; do
  "$DOTNET" build "$PLUGINS/$p/$p.csproj" -c Release -o "$CACHE/build/$p" -nologo -v q
  dest="$OUT/addons/counterstrikesharp/plugins/$p"
  mkdir -p "$dest"
  cp "$CACHE/build/$p/$p".{dll,deps.json,pdb} "$dest/"
done

# Our behavior trees (select with: mp_bot_ai_bt "scripts/ai/csbot/<name>.kv3").
mkdir -p "$OUT/scripts/ai/csbot"
cp "$ROOT"/bt/*.kv3 "$OUT/scripts/ai/csbot/"

# BotHider's MaintainBotQuota hook breaks bot_quota on current CS2; ship it disabled.
mkdir -p "$ROOT/dist/disabled"
mv "$OUT/addons/metamod/BotHider.vdf" "$ROOT/dist/disabled/"

echo "Built $OUT"
