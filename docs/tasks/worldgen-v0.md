# Task: "Endless Ocean" v0 – seeded world generation prototype mod (overnight)

Long-term dream of the user: a CGF ocean you can drive forever, with pseudo-randomly generated ocean-floor chunks and randomized fish from a seed.
This task is ONLY the simple first step: generated floor + a few random fish. Keep it small, working and well documented.

## Scope v0
* East of the vanilla world (NewWorld is 140000x26000; water surface y = 1244) the ocean continues with generated chunks.
* Chunk = fixed width (e.g. 2048 px). Deterministic from `seed` + chunk index: floor height profile (smooth 1D value/Perlin noise, depth range similar
  to the vanilla deep-ocean floor), built from VANILLA rock objects (CaveRock*/CaveRockBig etc. – pick the ones vanilla uses for the open-ocean floor),
  so it looks like the game. No custom art.
* Fish: per chunk a few spawns of EXISTING vanilla fish species (so fishalog/catalog stay vanilla), chosen by depth band and seed, spawned only in free
  water (never inside rocks), using the vanilla Fish behaviour.
* Streaming: only chunks near the camera exist (e.g. current ±2). Create on approach, destroy (or deactivate) when far. Rocks must have collision so the
  lure/fish interact as with vanilla terrain.
* "Infinite": v0 may extend NewWorld's width at build time by a large amount (e.g. +1,000,000 px) instead of a floating origin; document the limit and
  what a floating origin (shifting the world back around the player) would need later. Check what currently stops the boat at the east edge
  (room bounds / blocker objects / camera clamp / NavBrain) and handle it without editing vanilla events if possible (own controller + per-instance changes);
  if a vanilla edit is unavoidable, keep it minimal and report it.
* Save: store only the seed (+ nothing else in v0). Same seed → same world. Seed configurable (mod config / ModKit field), default fixed.
* Investigate and write down (no implementation needed): what is tied to fixed x ranges (ocean spawn rangeMin/Max, quests, sonar, NavBrain edges, day/night,
  boat fuel/speed?), and what a v1 would need.

## How
* Build it as a JAMM mod using public APIs where they exist (ModKit / GML runtime API, fish/catalog API). If the Area API from `exp/silo-v2` is merged to main
  when you start, reuse it; otherwise implement a small, clean runtime chunk helper as a documented public GML API (`jamm_chunk_*` or similar) so other
  modders can use it. Example mod folder `examples/modkit/endless-ocean/` with a README written for modders.
* Must not collide with other mods: generated content lives only east of the vanilla world edge; if other mods extend NewWorld eastward (check the 7 real
  mods' room dimensions), start generation after the widest mod's content and report it.

## Tests
* Synthetic: determinism (same seed → same chunk layout, different seed → different), fish never spawned inside rocks, chunk create/destroy bookkeeping.
* Real/VPS (one in-game run at a time; check `pgrep -f "Cat Goes Fishing.exe"` and wait if another agent is running the game; big builds need 600 s):
  vanilla + Endless Ocean: a bench scene that drives/teleports the boat east past the vanilla edge into generated chunks; screenshots at 3 points
  (`~/jamm-night/worldgen/screens/`), instance count over distance (must stay bounded), no CODE ERROR. If time permits also with the public 5-pack.

## Rules
AGENTS.md. Branch `exp/worldgen-v0` in `~/jamm-wt/worldgen-v0` from main. Zero warnings, tests green. Commit on branch OK, no push/merge.
Report `docs/reports/worldgen-v0.md` (what works, screenshots, limits, v1 plan).
