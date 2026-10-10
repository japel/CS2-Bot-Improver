#!/usr/bin/env python3
"""Per-round view of CT bots' hostage goals (css_hd_dump samples) next to round outcomes
(cs2live recording).

  analyze-goals.py <samples.txt> <events.jsonl> [--since UNIX_TS]

samples.txt lines: "<unix_ts> [hd] CounterTerrorist <name> pos=... goalEnt=<x> ..."
"""
import argparse, json, re
from collections import defaultdict

LINE = re.compile(r"^(\S+) \[hd\] CounterTerrorist\s+(\S+)\s+pos=.* goalEnt=(\S+)")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("samples")
    ap.add_argument("events")
    ap.add_argument("--since", type=float, default=0)
    a = ap.parse_args()

    events = [e for e in map(json.loads, open(a.events)) if e["ts"] >= a.since]
    bounds, start = [], None
    for e in events:
        if e["kind"] == "round_start":
            start = e["ts"]
        elif e["kind"] == "team_win" and start:
            bounds.append((start, e["ts"], e["reason"].replace("SFUI_Notice_", "")))
            start = None
    pickups = [e["ts"] for e in events if e["kind"] == "hostage_touch"]

    samples = defaultdict(list)          # ts -> [(name, goal)]
    for line in open(a.samples):
        if m := LINE.match(line):
            samples[float(m[1])].append((m[2], m[3]))   # one key per dump batch

    print(f"{'#':>2} {'outcome':24} {'pickup':6} {'max collectors':14} {'collector-secs':14} lost-goal")
    for i, (s, e, reason) in enumerate(bounds, 1):
        ticks = [(t, v) for t, v in samples.items() if s <= t <= e]
        collectors = [sum(1 for _, g in v if g == "hostage_entity") for _, v in ticks]
        had = defaultdict(int)
        lost = 0
        for t, v in sorted(ticks):
            for name, g in v:
                if g == "hostage_entity":
                    had[name] += 1
                elif had[name]:
                    lost += 1
                    had[name] = 0
        picked = any(s <= p <= e for p in pickups)
        print(f"{i:2} {reason:24} {'yes' if picked else 'no':6} {max(collectors, default=0):14} "
              f"{sum(collectors):14} {lost}")


if __name__ == "__main__":
    main()
