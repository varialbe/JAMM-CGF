# Task: resource optimisation audit (VRAM, RAM, multithreading) + first wins

The user wants a systematic, measured look at what JAMM builds cost in VRAM, RAM and CPU threads, then the most worthwhile fixes
as separate branches. Context: texture repack (exp/texture-repack, 417→119 pages, 3401→2171 MiB on all-7) and the fish grid
(exp/fish-perf) are handled elsewhere; reference their numbers, do not redo them.

Branch `exp/resource-audit` in `~/jamm-wt/resource-audit`, from main (merge exp/texture-repack if it is not on main yet, so you
measure the repacked builds). Report `docs/reports/resource-optimization-audit.md` with measured numbers for **vanilla vs heavy
(TGE, Antiquity, PWB) vs all-7 (TGE, Antiquity, PWB, Abyss, Colour, Verdant, Cave)**. Real files: `~/cgf-files/mods/pack-inputs`,
`~/cgf-files/private-vel` (never copy TGE/PWB off the VPS).

## Part A – measure and explain (no code changes needed to finish Part A)
1. **VRAM**: pages and MiB per build (before/after repack); does the GM 1.4 runner upload all pages at start or lazily (check
   runner behaviour: texture flush functions, `texture_prefetch`/`draw_texture_flush` availability in this bytecode version,
   observe via Wine d3d/OpenGL debug channels or memory of the process after load vs after visiting rooms)? Surfaces: list
   every `surface_create` in vanilla and mods with sizes (lighting/shader surfaces, HD mod scale); total MiB. Duplicate sprites
   between mods that survive repack (should be zero), and the largest images (top 30 by MiB, with owner mod).
2. **RAM**: process RSS (`/proc/<pid>/status` VmRSS/VmHWM of the Wine process) at menu, dock after 60 s, deepwater after 60 s for the
   three builds. Audio: per build, count/size of embedded sounds by format (WAV/uncompressed vs OGG), `AUDO`/`SOND` flags
   (streamed vs loaded into memory, audio groups), duplicates between mods by content hash. Strings/code size per build.
   Unreferenced assets after the merge (sprites/sounds/objects/backgrounds no code, room or table references) with sizes —
   be conservative: `asset_get_index`/string-built names count as possible references.
3. **Multithreading**: honest assessment.
   (a) JAMM build itself: per-phase wall times for the all-7 build (`AnalysisPhase` timings exist), which phases are CPU-bound and
       independent per mod (decompile, analysis, patch generation, texture decode/encode in the repacker) — estimate gains of
       parallelising them, considering RAM (11 GB VPS, users' PCs).
   (b) In game: GML is single-threaded in GM 1.4. Only native extension DLLs could offload work (e.g. fish neighbour search).
       Feasibility, data-transfer cost per tick (buffers vs per-call), Wine/Proton/Linux-native/macOS implications, stability.
       Prototype only if the numbers clearly justify it.
   (c) GPU side: draw calls/batching (per-fish light draws, surface switches). Not measurable on the VPS (llvmpipe): write a
       concrete measurement plan for the user's PC (tools, scenes, what to compare).

## Part B – implement the 1–3 best wins
Rank candidates by (benefit for a weak PC) / (risk). Likely candidates, verify first:
* Audio: convert uncompressed embedded WAV from mods to OGG (Vorbis q≈5) and/or mark large music as streamed; dedupe identical
  sounds. Must be opt-out per profile, audibly equivalent (document method), and not change sound asset IDs/names.
* Unreferenced-asset stripping (opt-in at first unless proven safe).
* Parallel build phases (deterministic output required: identical data.win bytes with 1 vs N workers).
* Faster PNG handling in the repacker (decode/encode in parallel, compression level) if not already done there.
Each implemented win: own commit (or own branch `exp/<name>` if large), tests, before/after numbers in the report, in-game RECORDED
dock + deepwater on all-7 via `~/jamm-night/bin/run.sh` (600 s budget), one game at a time (other agents share the VPS: check
`pgrep -f "Cat Goes Fishing.exe"` and wait). Behaviour/pixel-identical wherever claimed.

## Rules
AGENTS.md. Zero warnings, all tests green, commit on the branch, no push, no merge to main.
