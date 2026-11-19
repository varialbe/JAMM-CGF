# Overnight report 2026-11-19 (VPS, Claude as orchestrator)

Start: `main` = `adf1b04`. All in-game tests ran on the VPS (Wine + real Steam client, Xvfb, llvmpipe — no GPU,
so no FPS numbers here; anything frame-rate related is marked "on the PC to verify").

## 1. Crash in the public 5-pack (Magmer) — root cause found and fixed

`FATAL ERROR Draw Event Magmer: Unable to find any instance for object index … at gml_Object_Magmer_Draw_0`

* Reproduced on the VPS with `Antiquity,PWB,Abyss,Colour,Verdant` and with **`Abyss,Verdant` alone**.
* Root cause (verified by room instance counts and the decompiled code):
  vanilla places **2** `Magmer` in `NewWorld`, Abyss adds 2 (→4), Verdant adds 1 (→3); the additive room merge gives **6**.
  Vanilla `FishThinner_Step_0` fires the cull event whenever FPS stays below 60; `FishThinner_Other_10` culls fish whose
  `instance_number > 5`, **destroys their `light`** and deactivates them. Zoners reactivate them later and
  `Magmer_Draw_0` writes `light.image_alpha` → crash. With ≤5 Magmer (vanilla or a single mod) the bug is latent.
  The earlier "live > pop / kill_extras" hypothesis was wrong: `kill_extras` is never set.
* Fix **CGF-INSTANCE-003** (`InstanceCompatibility`): in every build with mods, the cull condition gets `&& !(light > 0)`,
  so fish that own a light are never culled (loop termination preserved). Fails closed with a warning if a mod rewrote the event.
* Verified in game (dock scene, full benchmark run, no error): `Abyss,Verdant` ✅ (before: crash), 5-pack ✅ (before: crash).
* Side note: Verdant's "Reech" catalog entry is incomplete in Verdant itself (`object[0,62]` never set) — not a JAMM loss.

## 2. ID collisions — relocation instead of picking a winner

* **Boats (CGF-RELOCATE-001)**: distinct boats (identity = boat object) on the same `BoatData` slot → the later claimant moves to
  the next free slot; typed references in its own code are rewritten; disjoint slot writes are unioned (also when both mods bump
  a shared line such as `nItems`).
  Real packs: heavy `TGE,Antiquity,PWB` → `Boat "Dark Boat" (Antiquity) moved from slot 8 to 9`, Echo-9 keeps 8 and TGE's
  Louis dialogue / `BoatData_Step_0` still point at Echo-9. Public 5-pack → `Boat "Hyper Boat" (Abyss) moved from slot 8 to 9`.
* **Upgrades (CGF-RELOCATE-003)**: heavy pack → `Upgrade "Bulbguard" (PWB) moved from slot 18 to 19; rewrote 6 typed references in
  3 code entries; icon frame 19->20`; "Zoom" (TGE) keeps 18; both names, costs and icons present. Byte-identical hunks (TGE inherits
  PWB code) are shared lineage and keep the old id. Orchestrator correction: `Rod.upgrades[]` is an effect-slot array (vanilla maps
  id 14 → `upgrades[18]`), not an id — the first agent version broke vanilla upgrade 14; fixed and covered by tests.
* **Effect slots (CGF-RELOCATE-004)**: TGE's Zoom and PWB's Bulbguard both used `Rod.upgrades[22]`; Bulbguard's effect moves to 23.
* **Shared unlocks (CGF-RELOCATE-005)**: the inherited `Rod_Step_0` trigger `UnlockUpgrade(18)` now also unlocks Bulbguard (19),
  guarded by `unlocks[19]`, so both upgrades stay obtainable.
* **Asset names are IDs too** (new finding): TGE and Antiquity both add an object `Boat8` (different objects) — today Antiquity's
  wins and Echo-9 runs with the wrong boat object. Antiquity and cave-expansion both add `Dollfin` (small vs medium fish) —
  the build aborted (`Catalog fish 'Dollfin' changes size category`).
  **CGF-RELOCATE-002** now renames the later claimant's asset (`Dollfin_jamm_cave`, `Boat8_jamm_antiquity`, …) and rewrites its
  structured, GML and asset-index references; identical/clearly modified copies keep the old merge. Object lineage uses the
  snapshot's event code names (TGE names events freely — the first version mistook TGE's Boat8 for codeless). Applied in `check`
  too (INFO findings). All-6/all-7 packs: 10–13 assets renamed, boats on slots 8/9/10/11.
* **Independent insertions**: two mods appending different blocks at one position (TGE + Colour in `ParticleSystems_Create_0`)
  used to drop one → all-7 crashed (`water_drip not set`). Pure insertions with disjoint assigned variables and no cross reads are
  now unioned automatically; explicit resolutions still win.
* **Lures/rods/tools**: still last-mod-wins (TGE "Lantern" vs Cave "Giga Hook" on lure slot 6) — agent task in progress.

## In-game results (VPS, auto scene, full benchmark capture = no crash)

| Build (main after merges) | Dock | Deep sea | Sonar |
|---|---|---|---|
| `Abyss,Verdant` before fix | CODE ERROR (Magmer) | – | – |
| `Abyss,Verdant` with CGF-INSTANCE-003 | ✅ | – | – |
| Public 5-pack before fix | CODE ERROR (Magmer) | – | – |
| Public 5-pack (`Antiquity,PWB,Abyss,Colour,Verdant`) | ✅ | ✅ | not run* |
| Heavy pack (`TGE,Antiquity,PWB`) | ✅ | ✅ | not run* |
| Heavy pack with boat + upgrade relocation | ✅ | – | – |
| **All 6 public mods** (`Antiquity,PWB,Abyss,Colour,Verdant,Cave`) — previously aborted (Dollfin) | ✅ | ✅ | not run* |
| **All 7 mods** (+ TGE) before insertion union | CODE ERROR (`ParticleSystems.water_drip`) | – | – |
| **All 7 mods** (`TGE,Antiquity,PWB,Abyss,Colour,Verdant,Cave`) | ✅ | ✅ | not run* |

*The sonar runs were stopped by Claude Code because the VPS ran low on memory while three agents were building/testing in
parallel; per policy they were not restarted automatically. To repeat: `~/jamm-night/bin/run.sh <build.win> <label> sonar 400`.

## 3. Pack acceptance report (new)

Every `build`/`apply` now prints per mod what arrived (assets, fish, table items, room placements, lost conflict hunks) and
runtime risks; `cgfmm build --acceptance-json <file>` writes the details. Example (heavy pack, main after merges):
`TGE 449/467 (lost: object fpsController, upgrade Zoom, …; modified: object Boat8 …), Antiquity 289/291, PWB 129/131`.

## 4. Gamma+ vs Perf/Smooth

`docs/reports/gamma-plus-vs-perf.md`. Summary: no safe code to copy. Gamma+'s even frame times come from ~30 FPS pacing, and it
makes `FishThinner` cull much more aggressively (+100 per frame at 45–60 FPS) — population reduction, not a neutral optimization.
Collision/arithmetic wins are already in Perf; draw/light changes need GPU profiling (on the PC to verify). Note: at the heavy
pack's ~25 FPS, vanilla thinning culls 10 % of the fish about every second until half of them are gone.

## 5. Where the heavy pack's tick goes (relative CPU profile, VPS)

New tool on branch `exp/gml-profiler` (on top of `exp/jamm-gamma-bench`, not merged into main because the bench branch itself is
not yet on main): `jamm-modmaker bench … --profile [--profile-scripts]` wraps event code with `get_timer` and writes
`jamm_profile_<scene>.csv`. Numbers are inclusive, relative and CPU-only (draw entries are render-affected on llvmpipe).

| Dock scene | Fish instances | `Fish_Step_2` (fish End Step, incl. state events) per tick | per fish |
|---|---:|---:|---:|
| Vanilla | ~1090 | 56 ms (39 % of loop) | 51 µs |
| Heavy pack (TGE, Antiquity, PWB) | ~2006 | 143 ms (41 % of loop) | 71 µs |
| Heavy pack + Perf builtin, perf mode ON (F8 setting seeded) | ~2050 | 155 ms (40 %) | 74 µs |

* The heavy pack costs ~2.5× more fish-update CPU: ~1.8× more fish **and** ~1.4× more per fish, which fits the `with (Fish)`
  neighbour loops (`fish_avoid_fish`, flocking) growing with the fish count.
* Script breakdown (heavy pack): `fish_lunge` ~10 % of the loop (called for every fish every tick), `Fish_Other_10` (idle) 6 %,
  `fish_avoid_fish` 4 %, `fish_avoid_wall` 3 %. `fish_lunge` → `check_lunge` already puts the cheap distance check first; this build
  appears to short-circuit, and for such small scripts the profiler overhead is significant, so no "blind" optimization was added.
* **Perf mode is OFF by default** (`jamm_perf.ini` `[Perf] Mode=0`, F8 toggles). The PC benchmark "Pack+Perf" therefore probably
  measured Perf without its distant-fish scheduling. On the VPS, turning it on did not reduce fish End Step CPU either (within noise).
  On the PC to verify: Pack+Perf with F8 enabled.
* Most promising next step: reduce the fish count's quadratic part (neighbour queries in `fish_avoid_fish`/flocking, e.g. a spatial
  bucket per tick) — needs careful equivalence work; and cull invisible fish updates more aggressively in Perf for large packs.

## Infrastructure notes

* Steam Cloud: the first VPS test run downloaded the account's CGF saves (slots 7–9) and one 30 s vanilla run re-saved slot 9
  and uploaded it. Copy of the post-run state: `~/jamm-night/save-backup-*`. Cloud is now disabled **only in the VPS client's
  localconfig.vdf** (not roaming); tests use an empty save dir.
* Killing CGF with a signal leaves the Steam client unable to serve the next launch (`SteamAPI_InitSafe() failed`).
  `~/jamm-night/bin/run.sh` closes the window gracefully and restarts Steam (`steam-restart.sh`) when needed.
* Codex needs `-s danger-full-access` on the VPS (AppArmor blocks bwrap); worktrees get a disabled push URL.
