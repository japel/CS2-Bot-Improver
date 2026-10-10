#!/usr/bin/env python3
"""Analyse a cs2live JSONL recording from a hostage map.

Per hostage pickup: carrier, outcome (rescued / carrier killed / round over), seconds
held. Plus round results and where carriers and T bots died.

  analyze-hostage.py events.jsonl [--since UNIX_TS]
"""
import argparse, json, statistics
from collections import Counter


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("file")
    ap.add_argument("--since", type=float, default=0)
    args = ap.parse_args()

    events = [e for e in map(json.loads, open(args.file)) if e["ts"] >= args.since]

    rounds, carries = [], []
    carrying = {}                 # carrier name -> pickup event
    t_deaths, ct_deaths, kills_by_team = [], [], Counter()

    def close_all(outcome, t):
        for name, ev in carrying.items():
            carries.append({"carrier": name, "outcome": outcome, "held": t - ev["ts"], "round": len(rounds) + 1})
        carrying.clear()

    for e in events:
        k = e["kind"]
        if k == "round_start":
            carrying.clear()
        elif k == "hostage_touch":
            carrying[e["a"]] = e
        elif k == "hostage_rescue" and e["a"] in carrying:
            ev = carrying.pop(e["a"])
            carries.append({"carrier": e["a"], "outcome": "rescued", "held": e["ts"] - ev["ts"], "round": len(rounds) + 1})
        elif k == "kill" and e.get("v_team"):
            kills_by_team[e["a_team"]] += 1
            (ct_deaths if e["v_team"] == "CT" else t_deaths).append(e["vpos"])
            if e["v"] in carrying:
                ev = carrying.pop(e["v"])
                carries.append({"carrier": e["v"], "outcome": "carrier killed", "held": e["ts"] - ev["ts"],
                                "round": len(rounds) + 1, "pos": e["vpos"], "by": e["a"], "weapon": e["weapon"]})
        elif k == "team_win":
            close_all("round over", e["ts"])
            rounds.append(e)

    print(f"rounds: {len(rounds)}   " + "  ".join(f"{r}: {n}" for r, n in Counter(
        r["reason"].replace("SFUI_Notice_", "") for r in rounds).most_common()))
    print(f"kills by team: {dict(kills_by_team)}")
    print(f"\nhostage pickups: {len(carries)}")
    for outcome, n in Counter(c["outcome"] for c in carries).most_common():
        held = [c["held"] for c in carries if c["outcome"] == outcome]
        print(f"  {outcome:15} {n:3}   held median {statistics.median(held):5.1f}s")
    killed = [c for c in carries if c["outcome"] == "carrier killed"]
    if killed:
        print("\ncarriers killed at (x y z, by, weapon):")
        for c in killed:
            print(f"  r{c['round']:<3} [{c['pos']}]  {c['by']} {c['weapon']} after {c['held']:.0f}s")
    if t_deaths:
        print(f"\nT deaths: {len(t_deaths)}   CT deaths: {len(ct_deaths)}")


if __name__ == "__main__":
    main()
