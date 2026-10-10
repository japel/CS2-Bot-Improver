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

- Single `Directory.Build.props`: `net8.0` (what CSS runtime ships) and one pinned CounterStrikeSharp.API version for all plugins we build.
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
