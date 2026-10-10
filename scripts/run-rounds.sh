#!/usr/bin/env bash
# Run N rounds on the current map (no map reload, no team changes), recording
# cs2live events and css_hd_dump samples. Aborts if no round finishes for 3 minutes.
#   run-rounds.sh <rounds> <outdir> [timescale=4] [extra console cmds...]
# Writes <outdir>/events.jsonl, <outdir>/samples.txt, <outdir>/status.txt
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CON="$ROOT/scripts/cs2con"
ROUNDS="${1:?rounds}"; OUT="${2:?outdir}"; TS="${3:-4}"; shift $(( $# < 3 ? $# : 3 ))
mkdir -p "$OUT"
q() { WAIT=1 "$CON" "$@" >/dev/null 2>&1; }
rounds_done() { WAIT=2 "$CON" css_botmod_status 2>/dev/null | grep -oE 'hostage rounds=[0-9]+' | tail -1 | grep -oE '[0-9]+$' || echo 0; }

python3 -I "$ROOT/scripts/cs2live" --quiet --out "$OUT/events.jsonl" & LIVE=$!
( while :; do
    python3 -I "$ROOT/scripts/cs2cmd" css_hd_dump 2>/dev/null \
      | awk -v t="$(date +%s.%N)" '{print t" "$0}' >> "$OUT/samples.txt" || true
    sleep 1
  done ) & SAMP=$!
trap 'kill $LIVE $SAMP 2>/dev/null; q "host_timescale 1"' EXIT

# warmup / match-end guards: earlier runs silently sat in warmup after mp_maxrounds was hit
q 'mp_warmup_end' 'mp_maxrounds 200' 'mp_halftime 0' 'mp_autoteambalance 0' 'mp_limitteams 0' \
  'bot_join_after_player 0' 'jointeam 1' "$@"
sleep 2
# exact 5v5 every run: restarts / warmup end move bots between teams
q 'bot_kick all' 'bot_quota_mode normal' 'bot_quota 0'
sleep 1
q bot_add_ct bot_add_ct bot_add_ct bot_add_ct bot_add_ct bot_add_t bot_add_t bot_add_t bot_add_t bot_add_t
sleep 3
q 'css_botmod_stats_reset' 'mp_restartgame 1'
sleep 4
count_teams() {
  python3 -I "$ROOT/scripts/cs2cmd" css_hd_dump 2>/dev/null \
    | grep -E '^\[hd\] (CounterTerrorist|Terrorist) ' | awk '{print $2}' | sort | uniq -c \
    | tr -s ' ' | tr '\n' ' ' || true
}
teams=""
for _ in $(seq 1 10); do   # bots need a moment to spawn after mp_restartgame
  teams=$(count_teams)
  case "$teams" in *"5 CounterTerrorist"*"5 Terrorist"*) break;; esac
  sleep 2
done
echo "teams: ${teams:-none}"
case "$teams" in *"5 CounterTerrorist"*"5 Terrorist"*) ;; *) echo "ABORT: teams not 5v5"; exit 1;; esac
q "host_timescale $TS"

last=0; last_change=$(date +%s)
while :; do
  sleep 20
  n=$(rounds_done)
  if [ "$n" != "$last" ]; then last=$n; last_change=$(date +%s); echo "rounds=$n/$ROUNDS"; fi
  [ "$n" -ge "$ROUNDS" ] && break
  if [ $(( $(date +%s) - last_change )) -gt 180 ]; then
    echo "ABORT: no round finished in 3 minutes (warmup? paused?)"; exit 1
  fi
done
WAIT=3 "$CON" css_botmod_status 2>/dev/null | grep '\[botmod\]' | awk '!s[$0]++' > "$OUT/status.txt"
grep 'hostage rounds' "$OUT/status.txt"
