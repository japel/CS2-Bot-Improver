#!/usr/bin/env bash
# Spectated 5v5 bot benchmark on a hostage map. Prints css_botmod_status after N rounds.
#   bench-hostage.sh <auto|all> [rounds=20] [map=cs_office] [timescale=4] [extra console cmds...]
#   e.g. bench-hostage.sh auto 20 cs_office 4 'css_botmod_patch OnAudibleEvent_GlobalHearRange on' 
set -euo pipefail
CON="$(dirname "$0")/cs2con"
SCOPE="${1:?auto|all}"; ROUNDS="${2:-20}"; MAP="${3:-cs_office}"; TS="${4:-4}"; shift $(( $# < 4 ? $# : 4 ))
q() { WAIT=1 "$CON" "$@" >/dev/null 2>&1; }

q 'game_type 0' 'game_mode 1' "map $MAP"
sleep 40
q 'sv_cheats 1' 'jointeam 1' 'bot_kick all' 'mp_autoteambalance 1' 'mp_limitteams 1' \
  'bot_join_after_player 0' 'bot_quota_mode normal' 'bot_quota 10' \
  'mp_maxrounds 200' 'mp_halftime 0' 'mp_warmup_end' "css_botmod_scope $SCOPE" "$@"
sleep 5
q 'css_botmod_stats_reset' 'mp_restartgame 1' "host_timescale $TS"

while :; do
  sleep 30
  n=$(WAIT=2 "$CON" 'css_botmod_status' 2>/dev/null | grep -oE 'hostage rounds=[0-9]+' | tail -1 | grep -oE '[0-9]+$' || echo 0)
  echo "rounds=$n/$ROUNDS"
  [ "${n:-0}" -ge "$ROUNDS" ] && break
done
q 'host_timescale 1'
WAIT=3 "$CON" 'css_botmod_status' 2>/dev/null | grep '\[botmod\]' | awk '!seen[$0]++' \
  | grep -E 'kind=|: (off|applied|FAILED|deferred)|hostage rounds' | grep -v '#'
