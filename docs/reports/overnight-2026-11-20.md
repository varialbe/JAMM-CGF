# Overnight report 2026-11-20 (VPS, Claude as orchestrator, Codex implementing)

## Kurzfassung (deutsch)

* **Gemergt nach main:** nur `exp/texture-repack` (wie vereinbart). All-7: 417 → 119 Texturseiten, geschätzter VRAM 3,4 → 2,2 GB,
  Datei 217 → 120 MB, alle Texturen pixelgleich. Läuft jetzt für **jeden** Build mit Mods (auch Einzelmods, Builtins, ModKit); Vanilla bleibt
  Byte für Byte gleich. Neue Datei: `~/jamm-night/builds/all7-repack.win`.
* **Zum Anschauen (nicht gemergt):** Silo v2 mit Öffnungsanimation, Endless Ocean (Worldgen v0), Wetter-Mod. Bilder unter
  `~/jamm-night/review-2026-11-20/`. Meine ehrliche Einschätzung steht bei jedem Punkt – vor allem zu den Fischen.
* **Weitere Branches:** siehe Abschnitt 6 (Fisch-Grid, Ressourcen-Audit, …) – Stand am Morgen.
* **Vorfälle:** Platte war um ~02:15 voll (behoben, Wächter läuft), RAM ist der Engpass auf dem VPS (all-7-Spiel ≈ 6 GB unter Wine).

Start: `main` = `9e95bb2`. In-game tests ran on the VPS (Wine + real Steam client, Xvfb, llvmpipe – no GPU, so no FPS numbers).

## 1. Texture repack – merged (`94c6f6a`)

* Codex (`exp/texture-repack`, 709fb37 + af57e03): after all merges, rebuild the legacy atlas from the texture items that are actually
  referenced (sprites, backgrounds, fonts, embedded images), dedupe identical images (pixel hash), drop unreferenced regions, skyline-pack
  into 2048² pages with the 2-texel gutters the legacy atlas uses; malformed/out-of-bounds rectangles keep their original page.
* User follow-up (night): repack for **every** build with at least one mod – single mods, built-ins only, ModKit/JSON – only pure vanilla
  untouched (byte-identical). Lossless PNG re-encoding shrank the archive (all-7: 282 → 115 MiB in Codex's run).
* Orchestrator fix (`64e8611`): on the tightly packed vanilla atlas a built-in-only build *grew* (34 → 37 pages, 536 → 568 MiB). The repack
  still runs but keeps the source atlas unless it saves memory or pages (`[CGF-TEX-001] Texture repack kept the source atlas …`);
  unreferenced texture items are still removed. New test `TightlyPackedAtlasIsKeptWhenARepackWouldGrow`.

| Build | Pages | Est. RGBA8 memory |
|---|---:|---:|
| All-7 (TGE, Antiquity, PWB, Abyss, Colour, Verdant, Cave) | 417 → 119 | 3,401 → 2,171 MiB |
| Abyss | 196 → 73 | 1,490 → 1,311 MiB |
| Antiquity | 154 → 47 | 1,293 → 740 MiB |
| TGE | 91 → 53 | 1,039 → 836 MiB |
| Perf only | 34 → 34 (kept) | 536 MiB |

Verification (own, on the final code): all-7 built through the normal profile path in 5:41 min, archive **217 → 120 MB**; opt-in
pixel comparison against the unpacked build passed for every live texture item (pixels, fields, collision masks); full suite
446 core + 127 GUI green, zero warnings; **dock RECORDED** with the final build. Deepwater was RECORDED by Codex on 709fb37 (same atlas:
identical `417 -> 119` decision; later changes were lossless PNG encoding, gating and the keep-guard). My own deepwater run with the final
build was stopped by Claude Code's low-memory reaper and not restarted – **to repeat:** `~/jamm-night/bin/run.sh ~/jamm-night/builds/all7-repack.win tex dock|deepwater 600`.
On the PC to verify: VRAM/FPS on Vel's iGPU laptop with `all7-repack.win`.

## 2. Silo v2 + Area API + opening animation – for review (`exp/silo-v2`, 673a0ee)

* Real chamber in NewWorld from vanilla cave rocks and Geiger lights, new Area API, ModKit source package (`examples/modkit/silo/`);
  zero vanilla code entries/objects/rooms modified. Four fish composed at build time from the player's own vanilla sprites (no vanilla pixels shipped).
* Opening animation (required): hatch opens over 1.55 s as the lure approaches – shutters slide/turn apart, bubbles, green light on the rocks;
  passage blocked until the doors clear; opened state survives reload; other save slots stay closed. Solo and all-7 verified (incl. Cave's relocated hatch).
* Review rounds (orchestrator → Codex): round 1 fish were stitched together (hard seams, foreign colours) → rules: one palette per fish,
  no seams, CGF flat style. Round 2 clean but derivative (Veyl ≈ Cuddlefish, Ferrex ≈ stretched Magmer, Garrusk ≈ recoloured Shark) and the
  opening screenshots were black → round 3: distinct palettes/features, lit bench-only captures, MP4/GIF.
* **My assessment for you:** opening sequence and chamber read well. Veyl (plum, dorsal sail), Radik and Garrusk (petrol, flank bands) are
  their own fish. **Open:** Ferrex is still essentially a brown Bagu; a small grey block stays at the right shaft edge after opening.
* Images: `~/jamm-night/review-2026-11-20/silo-v2/` (art-review, opening sequences solo/all7, opening.gif/mp4, chamber).
* Tests: 581 default tests green, ten-profile archive matrix and site-package test passed (Codex).

## 3. Endless Ocean v0 (worldgen) – for review (`exp/worldgen-v0`, cd7d45a)

* Seeded ocean east of the existing world (+999,424 px), chunks of 2,048 px streamed around the camera (≤ 20 vanilla `CaveRockBig` + ≤ 15 fish
  per window), collidable floor from smooth value noise, three fish depth bands with six vanilla species. Only the seed is saved; streamed fish
  are excluded from save files (guarded `[Fishes]` writes, exact pattern or abort).
* Vanilla and public-5 runs: no CODE ERROR, collision at every checkpoint, instance counts return to zero heading west.
* Showcase (orchestrator asked for it): panorama, wide floor views, depth bands, seed comparison under `~/jamm-night/review-2026-11-20/worldgen/showcase/`.
* **My assessment:** a good v0. For v1: more variety (one rock type, few fish), a softer transition (abrupt cliff where the old world ends),
  gaps under the floor (rocks look floating at the bottom edge). The report contains a v1 plan (floating origin, sonar, quests, nav, boat travel).

## 4. Weather mod + Weather API – for review (`exp/weather`)

* Weather API (packs, per-slot forecasts, `weather` console command) and a Weather Pack: Super Sunny, Rainy, Windy, Fog; eight new surface fish;
  original synthesized music and ambience. 598 tests green; solo/public-5/all-7 dock without code errors (Codex).
* **My assessment:** the scenes look good (sun, rain streaks, wind clouds, fog). Round 1 issues sent back: visible vertical tiling seams in the
  fog band, and most fish were recolours of their donors (Drizzit ≈ Roundfin, Bask ≈ Skitter, Tack ≈ Airy, Mote ≈ Cuddlefish).
  Round 2 (`d82dad8`): fog tiles seamlessly (wide fog screenshot), all eight fish got own palettes and silhouette features from vanilla parts
  (Bask fan fins, Drizzit elongated, Kest sail, Tack new donor Sibilfin, Mote pink with barbels, Veilmouth grey with club tail). Some donors are
  still recognisable; good enough for your decision. 599 tests green, solo dock without code errors.
* Images: `~/jamm-night/review-2026-11-20/weather/` (art-review, art-review-gameplay, fog-wide).
* Note: Codex sent you Telegram images of round 1 itself; I sent the assessment afterwards.

## 5. Infrastructure and incidents

* **Disk full (~02:15):** `/` reached 100 % (369 MB free) from per-run game copies and build outputs. Cleaned to 58 GB free without losing
  evidence (CSVs, logs, screenshots kept). `tmux jamm-diskguard` now removes old run game copies when < 15 GB are free.
* **RAM:** an all-7 game uses **~5.8 GB RSS** under Wine/llvmpipe (texture pages live in system RAM without a GPU), Steam's CEF helpers ~2.5 GB.
  With two Codex jobs this triggered Claude Code's low-memory reaper twice (my background watchers / one verification run). Queue now starts
  jobs only with > 2.5 GB available RAM.
* `run.sh` used to `pkill` any running game for non-silo scenes – with several agents that killed other agents' runs. It now waits (≤ 20 min).
* `tmux has-session -t name` prefix-matches (`jamm-silo2` matched `jamm-silo2-next`, the follow-up waited for itself) → exact `-t =name`.
* Night queue: max 2 concurrent `codex exec` processes (counted directly), stops on a quota message. No Codex quota limit was hit tonight.

## 6. Other branches (state at 07:45; jobs keep running)

### Fish neighbour grid (`exp/fish-perf`, 7a437bd, CPU) – finished: behaviour-identical, target missed, do not merge as a speed-up

* Step 0 done: `exp/gml-profiler` (+ `exp/jamm-gamma-bench`) merged onto current main in that branch; new bench option `--trace-fish`.
* Step 1 found that most fish neighbour code is **not** a local radius loop: global `instance_nearest` selectors with deactivate/retry,
  random-index selection, native collision checks. Only the avoidance loop (`with (FishLarge)` in fish End Step) could be rewritten provably.
* Grid (`jamm_perf.ini [Perf] FishGrid=1`): lazy build per tick, original visiting order preserved via traversal ordinals, invalidates on any
  create/destroy/activation and falls back to the original loop.
* **Equivalence:** fish-state traces byte-identical over 1,800 ticks for vanilla dock, vanilla deepwater and heavy deepwater; heavy dock, all-7 dock and
  all-7 deepwater match over their shared prefixes (609–1,603 ticks; the 600 s runner cap stopped the longer side).
* **Speed:** no reduction for vanilla/heavy (controlled heavy dock pair 171.6 → 179.3 ms/tick fish End Step, slightly worse); all-7 dock pair
  452 → 400 ms/tick, but a single pair on a shared VPS. Conclusion so far: behaviour-identical, but the quadratic cost is not where the
  grid helps; the remaining big cost is per-fish work and the global selectors. Do not merge as Perf default. 587 tests green. The profiler/bench tooling (own merge commit in that branch) is useful on its own and could go to main separately – your call.

### Resource audit (`exp/resource-audit`) – Part A done, Part B (parallel PNG rendering in the repacker) in progress

Measured on the VPS (repacked builds, llvmpipe; RSS includes Wine's CPU-side texture copies):

| Build | Texture pages / MiB (after repack) | Game RSS dock +60 s | Embedded audio |
|---|---:|---:|---|
| Vanilla | 34 / 536 | 1,183 MiB | 37 PCM WAV (13.2 MiB) |
| Heavy | 67 / 1,048 | 1,960 MiB | + 11 Ogg (2.1 MiB) |
| All-7 | 119 / 2,171 | 4,459 MiB | same as heavy |

* Audio is **not** a big RAM factor: all mods together add ~2 MiB of Ogg; the 8 startup-decoded Oggs ≈ 41 MiB PCM-equivalent (39 MiB is
  one 213 s cave ambience → candidate for streaming). No byte-identical duplicates after merge.
* GM 1.4 runner: only per-sprite/background prefetch/flush, no whole-page flush; no mod uses them. Page uploads stay flat from title to
  deepwater (all pages resident) – so the repack's page reduction is the main VRAM lever.
* Build time all-7: 347 s, of which **texture repack 158 s** (PNG decode 412× + re-decode 723× during rendering) → Part B parallelises/caches that.
* In-game multithreading: GML is single-threaded; a native DLL for neighbour search is not justified by the grid result above.
* GPU plan for the PC (draw calls, per-fish lights) is in the audit report.

### Queued, not started yet
Vel's rod/boat points (ModKit JSON rods + unlock checker), base-mod mode, weak-PC preset (specs on main, queue `jamm-queue`).

## What I need from you

1. Silo v2: are the fish OK (especially Ferrex)? Opening animation OK? → then merge `exp/silo-v2`.
2. Worldgen v0 and Weather: look at the images; decide what goes to main.
3. PC: test `all7-repack.win` (VRAM, FPS), ideally on Vel's laptop.
