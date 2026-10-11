#!/usr/bin/env python3
"""Map-agnostic analysis of a run-rounds.sh directory (events.jsonl + samples.txt).

Reports round outcomes, bomb plants/defuses, kill flags (through smoke, wallbang,
headshot) and team "bunching" from the 1 Hz bot position samples:
  - nearest-teammate distance (median) per team
  - share of samples where >= 4 alive teammates are inside one 600-unit circle

  analyze-round.py <run dir> [<run dir> ...]
"""
import json, math, re, statistics, sys
from collections import Counter, defaultdict

SAMPLE = re.compile(r"^(\S+) \[hd\] (CounterTerrorist|Terrorist)\s+(\S+)\s+pos=(-?\d+) (-?\d+) (-?\d+)")
CLUSTER_R, CLUSTER_N = 600, 4


def analyze(d):
    ev = [json.loads(l) for l in open(f"{d}/events.jsonl")]
    out = Counter()
    for e in ev:
        if e["kind"] == "team_win":
            out["rounds"] += 1
            out["win_" + e["team"]] += 1
            out["reason_" + e["reason"].replace("SFUI_Notice_", "")] += 1
        elif e["kind"] == "triggered" and e["event"] in ("Planted_The_Bomb", "Defused_The_Bomb"):
            out[e["event"]] += 1
        elif e["kind"] == "kill" and e.get("v_team"):
            out["kills"] += 1
            for f in ("throughsmoke", "penetrated", "headshot", "attackerblind"):
                if f in e.get("flags", ""):
                    out["k_" + f] += 1

    batches = defaultdict(lambda: defaultdict(list))   # ts -> team -> [(x,y)]
    try:
        for line in open(f"{d}/samples.txt"):
            if m := SAMPLE.match(line):
                batches[m[1]][m[2]].append((int(m[4]), int(m[5])))
    except FileNotFoundError:
        pass
    nn, clustered, n = defaultdict(list), Counter(), Counter()
    for teams in batches.values():
        for team, pts in teams.items():
            if len(pts) < 2:
                continue
            n[team] += 1
            for i, p in enumerate(pts):
                nn[team].append(min(math.dist(p, q) for j, q in enumerate(pts) if j != i))
            if len(pts) >= CLUSTER_N and any(
                    sum(math.dist(c, q) <= CLUSTER_R for q in pts) >= CLUSTER_N for c in pts):
                clustered[team] += 1
    bunch = {t: (statistics.median(nn[t]), clustered[t] / n[t]) for t in n}
    return out, bunch


def main():
    total, bunches = Counter(), defaultdict(list)
    for d in sys.argv[1:]:
        o, b = analyze(d)
        total.update(o)
        for t, v in b.items():
            bunches[t].append(v)
    r = total["rounds"] or 1
    k = total["kills"] or 1
    print(f"rounds {total['rounds']}: CT {total['win_CT']}  T {total['win_TERRORIST']}")
    print("  reasons: " + ", ".join(f"{key[7:]} {v}" for key, v in total.most_common() if key.startswith("reason_")))
    print(f"  plants {total['Planted_The_Bomb']} ({100 * total['Planted_The_Bomb'] / r:.0f}% of rounds), "
          f"defuses {total['Defused_The_Bomb']}")
    print(f"kills {total['kills']}: through smoke {100 * total['k_throughsmoke'] / k:.1f}%, "
          f"wallbang {100 * total['k_penetrated'] / k:.1f}%, headshot {100 * total['k_headshot'] / k:.1f}%")
    for t, vals in bunches.items():
        print(f"{t:17} nearest teammate median {statistics.mean(v[0] for v in vals):5.0f}u   "
              f">={CLUSTER_N} in {CLUSTER_R}u circle {100 * statistics.mean(v[1] for v in vals):4.0f}% of time")


if __name__ == "__main__":
    main()
