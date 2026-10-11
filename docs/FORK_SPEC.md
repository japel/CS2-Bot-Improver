# CS2-Bot-Improver fork — improvement spec

Fork of [ed0ard/CS2-Bot-Improver](https://github.com/ed0ard/CS2-Bot-Improver) at `72adfc2` (v1.4.5).
Target: offline / private-server play on **Linux** (CS2 under Hyprland, `-insecure`).
License: AGPL-3.0 (inherited) — the fork stays public and AGPL.

## 1. Why fork

Findings from running v1.4.5 and reading the source:

| Problem | Evidence |
|---|---|
| Hostage maps are unsupported | Zero hostage code in any plugin. BotAI's global-hearing / always-investigate patches and BotState's per-tick "always alert, never ignore enemies" overrides pull CT bots off the objective. Upstream issue #121. |
| Behaviour is forced, not decided | `BotState.UpdateBotBehavior` rewrites ~10 native timers every tick (alert 600 s, ignore-enemies 0, panic 0, surprise 0, sniper-dodge always). No context, no config. |
| Update fragility is invisible | 40 byte-signature patches against one `libserver.so` hash. Failures only show in a log file (`TBot_BombsiteSearch_UseKnownPlantedSite: FAILED` on our build). BotHider's MaintainBotQuota hook silently broke auto-fill (`bot_quota` did nothing). |
| No tests, no CI | 1 test file across ~32k lines. Release zips assembled by hand. |
| Inconsistent builds | 4 CounterStrikeSharp API versions (1.0.367–1.0.376), net8.0 + net10.0 mixed. |
| Map-type assumptions | Bomb logic spread through general behaviour; hardcoded map checks (`isInferno`). |

## 0. Status (2026-10-10)

| Phase | State |
|---|---|
| 0 – Baseline | **Done.** Forks of CS2-Bot-Improver, CS2-BotAI, CS2-Smarter-Bot; `scripts/build.sh`, `install-linux.sh`, `online.sh`, `bots.sh`; CI green; fork build verified in-game. |
| A – Scoped patches | **Done, default reverted.** `MapProfile`, `PatchScopes`, `css_botmod_status`, `css_botmod_scope auto\|all`, `css_botmod_patch <name> on\|off\|auto`, hostage round stats with team sizes. Benchmark showed scoping hurts, so every patch defaults to Always; hearing/investigate stay runtime-toggleable. |
| Spike | **Done.** See §0a. Native bots *do* rescue hostages; the gap is CT commitment, not escort. |
| B – HostageDirector | Next: CT rescuer assignment right after freeze end. Open question: native move-to entry point. |
| C – Behaviour policy | Not started. Candidate: BotState smoke reveal (many `throughsmoke` kills). |

Dev loop: CS2 runs with `-netconport 2121 -netconpassword …`; `scripts/cs2con '<cmd>'` runs console commands and prints output (password in `~/.config/csbot/netcon`). `scripts/cs2restart --install` stops CS2 cleanly, installs, relaunches and tiles it. `scripts/cs2live` records server-log events to JSONL; `scripts/analyze-hostage.py` summarises them. Spectating needs `mp_forcecamera 0` (competitive cfg sets 1).

## 0a. Hostage benchmark results (cs_office, exact 5v5, host_timescale 4)

Patch A/B, 20 rounds each:

| hearing | investigate | CT pickup rounds | rescues | CT wins |
|---|---|---|---|---|
| off | off | 7/20 | 4 | 9 |
| on | off | 13/20 | 7 | 11 |
| off | on | 12/20 | 2 | 6 |
| on | on (upstream) | 15/20 | 8 | 16 |

(An earlier run showing 17/20 for off/off was at 6v4 — `bot_quota fill` drifted; bench now adds 5+5 explicitly.)

Baseline, all patches on, 40 rounds, with `cs2live` recording:

| round outcome | with pickup | without |
|---|---|---|
| All hostages rescued | 11 | – |
| CTs win (elimination) | 8 | 7 |
| Ts win (elimination) | 3 | 6 |
| Hostages not rescued (time) | – | 6 |

- 27 pickups: 11 rescued, 11 still carried at round end, only **5 carriers killed** → escort is not the main problem.
- **18/40 rounds with no pickup at all**; 6 of those are lost purely on time.
- First pickup 34–102 game-seconds after round start (median ~50 s): CT bots are slow to commit.
- Kills CT 158 / T 129, T win rate 15/40 → T side is competitive; T-guard logic is lower priority than CT commitment.

## 0b. Measurement lessons and later results (2026-10-11)

Earlier numbers in §0a are suspect; the runner now guards against all of these:

- **maxplayers 1**: maps started with `map` from the console run with maxplayers 1, so
  CounterStrikeSharp's `GetPlayers()` sees no bots. BotState's FOV/flashbang/weapon logic,
  BotAI's team counts and HostageDirector were blind in those runs. All our plugins now
  scan `cs_player_controller` entities.
- **Warmup / team drift**: a run sat in warmup after `mp_maxrounds`; others drifted to 4v6.
  `scripts/run-rounds.sh` ends warmup, raises maxrounds, re-adds bots and aborts unless 5v5.
- **netcon replay**: every connection replays history and receives all clients' output;
  `scripts/cs2cmd` uses `css_hd_mark` markers and privately tagged dumps.
- **Random bot profiles** changed skill between runs. Runs now use fixed lineups
  (CT Vitality, T NaVi) and alternate variants (A/B/A/B).

Findings:
- Legacy CT bots *do* commit: 2–4 CTs target a hostage every round (no "decision" to patch).
- Carriers died a median ~1 s after pickup to T bots camping the hostages → **covered
  pickup** (HostageDirector suppresses +use near a free hostage while an enemy is seen):
  42 rounds each, alternating: rounds with pickup 15 vs 10, rescue wins 4 vs 2, CT wins
  20 vs 19. Small positive, not significant; kept on.
- BotState on/off (alternating, ~42 rounds each, covered pickup on): rounds with pickup
  17 vs 12, rescue wins 4 vs 6, T elimination wins 13 vs 2, time-outs 10 vs 19. BotState
  does *not* hurt hostage play; without it bots go passive. The earlier 76%-vs-30% gap came
  from random bot profiles, not BotState.
- **cs_italy**, covered pickup on/off (alternating, 42 rounds each, BotState on): rounds
  with pickup 37 vs 26 (each run pair 18/19 vs 12/14), rescue wins 16 vs 9, total CT
  wins 27 vs 29. Covered pickup clearly makes CT play the objective without losing
  strength; on cs_office the same change was only a small positive.
- **Escort via radio** (carrier sends `playerradio followme`): no bot ever entered follow
  mode, neither for bot- nor human-sent radio. Shipped disabled (`css_hd_escort`).
- **Native move-to found**: `CCSBot::MoveTo(goal, route)` (VA 0xc1c150, unique signature,
  see HostageDirector/Native.cs). `nav_mark`/`bot_goto_mark` do nothing in release CS2.
  Bots honor the order until they see an enemy (combat preempts) or their own idle logic
  overwrites it (~12 s), so directors re-issue orders. This unlocks escorts, positioning,
  and voice/text commands (e.g. via a local tool-calling model such as cactus-compute/needle).
- **de_mirage baseline** (20 rounds, fixed lineups): CT 10 : T 10, bomb planted 80% of
  rounds, 13.6% of kills through smoke (upstream #146), T bots have >=4 teammates within
  600u 51% of the time vs CT 32% (bunching, upstream #121).
- **Escort via MoveTo** (cs_italy, 40 rounds each, alternating, covered pickup on): CT wins
  30 vs 21 (per run 16/14 vs 8/13), rescue wins 17 vs 8, carriers killed 10/31 vs 20/38.
  Biggest effect so far; on by default (2 escorts per carrier).
- **Split** (de_mirage, 40 rounds each, alternating): at freeze end 2 random T bots are
  sent to one bomb site via MoveTo. T wins 32 vs 24 (per run 14/18 vs 12/12), CT defuses
  5 vs 14; bunching only 44% vs 49%. Makes the T side clearly stronger; on by default.
- **Team orders** (`css_bots follow [n] | hold <A|B> [n] | hostage [n] | free`, client
  command): humans order bot teammates via MoveTo, re-issued every 0.5 s while the bot
  isn't fighting. Verified: followers head to the caller. Intended as the surface for a
  voice front end (local STT + tool-calling model sending `css_bots` over netcon).
- Variance: identical 20-round runs gave 12 and 5 pickup rounds. Only differences seen over
  ~40+ rounds per variant mean anything.
- Behavior trees (`mp_bot_ai_bt`, see `bt/`) work for scripted pickup/rescue but BT
  CT bots lose every round on cs_office; parked as the long-term "own brain" track.

## 1a. Roadmap (approach)

1. **Now – Director** (this spec): keep Valve's bot AI for movement/shooting; our code scopes patches per map and assigns roles/targets on top. C# on CounterStrikeSharp.
2. **Later – Own brain**: our code decides every action; Valve bots are only bodies driven via BotController usercmd/movement injection. Needs our own nav-mesh pathfinding. Design so Phase A/B pieces (MapProfile, role assignment, status) carry over.
3. **Maybe – Learned policy**: an ML agent (trained on demos) driving bots through the same injection layer. Research track; only after 2 gives us a clean control interface.

## 2. Goals

1. **G1 – Hostage maps work**: CT bots rescue hostages, T bots guard them, with the mod's aim/nade/movement improvements still active.
2. **G2 – Map/mode-aware behaviour**: every behaviour override and memory patch declares which map types/modes it applies to.
3. **G3 – Visible health**: one console command shows which patches/hooks/features are active, so a CS2 update's damage is obvious in-game.
4. **G4 – Reproducible Linux builds**: one command builds the full `game/csgo` drop-in from source; CI builds it on every push.
5. **G5 – Tests for the logic we own**: pure logic is unit-tested without a running game.

### Non-goals
- Valve matchmaking, FACEIT, public servers (never; VAC risk).
- The Windows Panel (keep upstream's, don't extend).
- Rewriting native-patching infrastructure (it's the best-written part; reuse it).
- Cosmetics (BotRandomizer skins, BotHider identities) beyond keeping them from breaking core features.

## 3. Repository strategy

- Main fork `japel/CS2-Bot-Improver` holds configs, packaging, **new** plugins, tests, CI.
- Fork a submodule only when we must change it. Expected: **BotAI** and **BotState** (both ed0ard). Point `.gitmodules` at the forks on a `fork/main` branch.
- Leave XBribo's native plugins (BotController, BotHider, BotVision) on upstream tags; consume via their APIs.
- Track upstream: `upstream` remote; rebase monthly or after each CS2 update fix upstream ships.

## 4. Design

### 4.1 `MapProfile` (shared library, new)

Classifies the current map on `OnMapStart`:

```
enum MapKind { Bomb, Hostage, Other }
record MapProfile(string Name, MapKind Kind, Vector[] HostageSpawns, Vector[] RescueZones, Vector[] BombSites);
```

Detection is entity-based, not name-based: `hostage_entity`/`info_hostage_spawn` ⇒ Hostage; `func_bomb_target` ⇒ Bomb. Name prefix is only a fallback. Exposed to other plugins as a CSS shared capability.

### 4.2 Scoped patches & overrides (BotAI + BotState fork)

- Each BotAI patch definition gains `Scope = Always | BombOnly | NotHostage`.
- On map start BotAI applies/restores patches to match the `MapProfile` (it already supports clean restore on unload — reuse `RestorePatch`).
- Initial scoping:
  - `NotHostage`: `OnAudibleEvent_GlobalHearRange`, `InvestigateNoise_SkipSelfDefenseCheck`
  - `BombOnly`: all `Bomb*`, `Defuse*`, `OnBombPlanted_*`, `TBot_BombsiteSearch_*`, `GameState_Reset`
- BotState's per-tick overrides move behind a `BehaviorPolicy` that receives `(bot, MapProfile, role)`; on hostage maps, rescuers/escorts skip the "always alert / never ignore enemies" forcing.

### 4.3 `HostageDirector` plugin (new)

Phase B only if needed (see §5). Responsibilities:

- **CT**: each round, assign 1–2 *rescuers* (closest healthy bots with rifles) and the rest as *cover* along the path. Rescuer reaches hostage, holds `+use` via BotController `InjectUsercmd(IN_USE)` until carried, then routes to nearest rescue zone.
- **T**: assign 2 *guards* per hostage cluster to hold angles near `HostageSpawns`; others play normally. On "hostage picked up" event, nearby Ts converge on the carrier.
- **Movement**: preferred = native bot navigation (`CCSBot::MoveTo`-equivalent found by signature, same mechanism as BotAI). Fallback = none; if no native move-to exists we stop and re-scope (own nav-mesh pathing is out of scope for v1).

### 4.4 `css_botmod_status` command (new, in BotAI fork or shared lib)

Prints: libserver.so SHA256 vs the hash patches were verified against; per-patch applied/failed/skipped-by-scope; BotHider quota hook OK/FAIL; MapProfile kind; plugin versions. Also printed once to console on map start if anything failed.

### 4.5 Build & CI

- Single `Directory.Build.props`: `net10.0` (CSS 1.0.376 ships .NET runtime 10.0.3) and one pinned CounterStrikeSharp.API version (1.0.376) for all plugins we build.
- `build.sh`: builds managed plugins, downloads pinned native release artifacts (Metamod, CSS, XBribo plugins) by tag + checksum, assembles `dist/game/csgo/` matching today's release layout. Our `~/cs2-botmod/install.sh` logic (patch current `gameinfo.gi`, don't copy upstream's) moves into the repo as `scripts/install-linux.sh` with `online.sh`/`bots.sh`.
- GitHub Actions: build + unit tests on push; release workflow uploads the Linux zip on tag.

### 4.6 Tests

xUnit project `tests/` covering: hex/signature parsing and `LinuxDisplacementFixups` (BotAI), patch-scope selection, MapProfile classification from fake entity lists, HostageDirector role assignment (pure function over bot snapshots), buy-plan logic (BotBuy, if touched).

## 5. Phases

| Phase | Deliverable | Done when |
|---|---|---|
| **0 – Baseline** | Fork BotAI/BotState, unify build, `build.sh`, CI green, install scripts in repo | `dist/` from CI installs and behaves identically to upstream v1.4.5 on de_mirage |
| **A – Scoped patches** | MapProfile, patch/override scoping, `css_botmod_status` | On cs_office, status shows hearing/investigate patches skipped; on de_mirage all applied |
| **Spike** | Watch vanilla + Phase-A bots on cs_office (spectator, 10 rounds); search libserver.so for native hostage task / move-to entry points | Written answer: do native bots rescue hostages once un-patched? Is there a callable move-to? |
| **B – HostageDirector** | Only if spike says native rescue is insufficient and a move-to exists | Over 20 spectated rounds on cs_office: CT bots touch a hostage in ≥70 % of rounds where ≥1 CT survives 40 s; T guards present at hostage area at round start ≥80 % |
| **C – Behaviour policy** | Replace blunt per-tick forcing with context-aware policy (bomb maps too) | No regression in bot win-rate vs. human-free baseline; configurable via `configs/plugins/BotState/BotState.json` |

## 6. Open decisions

1. **BotHider**: keep disabled (bots show BOT tag, native quota works) or fix its quota hook upstream? *Recommendation: keep disabled in our default build; revisit when XBribo ships a fix.*
2. **Upstream contributions**: send scoped patches/MapProfile back as PRs to ed0ard? *Recommendation: yes after Phase A works — keeps our fork small.*
3. **Windows**: build/test only Linux? *Recommendation: Linux only; keep code portable, don't test Windows.*

## 7. Risks

- **CS2 updates** break signatures for both upstream and us. Mitigation: status command + CI can't catch it (needs game binary) — document a manual "verify after update" checklist.
- **No native move-to** in CS2's server binary → Phase B blocked; fallback is accepting Phase A quality.
- **Upstream churn**: dependabot bumps submodules often; our forks of BotAI/BotState must be rebased. Keep our diff small and isolated in new files where possible.
