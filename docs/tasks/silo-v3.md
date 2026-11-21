# Task: Silo v3 – only the door animates, depth effect through the rock (Vel's feedback)

Branch `exp/silo-v3`, worktree `~/jamm-wt/silo-v3`. Package `examples/modkit/silo`, bump to **2.3.0**.
Read AGENTS.md, `docs/tasks/silo-v2-area-api.md`, `docs/modding/areas.md`, the silo README and hooks first.

## Feedback (Vel, Discord)
"the rock breaks in half xD … if you added a **depth effect**, where we passed through the rock with the rock
behind **in front of our lure**, and the silo's **door is the only thing that had animation**, it would be even cooler."

## Required
1. **No more splitting rock.** Today the opening cuts two shutters out of `spr_silo` and slides them apart, which
   reads as the rock breaking in half. Replace this: the rock/silo body stays completely static. Only the
   silo **door** (hatch / blast door, a part that visually belongs to the silo, composed at build time from the
   player's own vanilla pixels – e.g. the existing hatch region of `spr_silo` or a recipe sprite – no shipped
   vanilla pixels) animates: slides/rotates open in its own frame, with the existing bubbles + green light fade.
2. **Depth effect.** Add a foreground rock layer that is drawn **in front of** the lure, line, hooked fish and
   fish (lower `depth` than Lure/line/Fish; check the vanilla depths of Lure, the fishing line draw, Fish and
   CaveRock), so the lure visibly passes *through* the rock into the shaft, while the rock behind the shaft stays
   behind the lure. The foreground piece must line up pixel-exactly with the static rock (no seams, no double
   outlines), may use a little alpha/darkening for depth, and must not block the view of the lure inside the
   shaft completely (the lure must remain findable – e.g. foreground only on the rim/overhang).
   Collision/passage unchanged: blocked until the door is fully open, then passable; opened state per save slot.
3. Works solo and in all-7 including Cave Expansion's relocated hatch (`follow` anchor), and in the user's personal
   build (perf, hd-lite, console, smooth, silo, weather-pack, endless-ocean).
4. Fix the two open points from v2 review:
   * **Ferrex still looks like a brown Bagu** – give it a clearly own identity (silhouette tweak from vanilla parts,
     distinct copper/teal palette, pattern via the existing parts SVGs), still vanilla-style.
   * **Small grey block at the shaft rim after opening** – find which instance/sprite/mask draws it and remove it.
5. If the Area API needs a feature for this (e.g. a foreground piece with depth relative to the lure, or a
   `door` animation target), add it cleanly to the API + docs (`docs/modding/areas.md`, modkit reference) so other
   mods can use it, with unit tests.
6. Art review rounds like v2: make a side-by-side image (vanilla rocks/silo next to the new door + foreground)
   `~/jamm-night/silo-v3/art-review.png`, critique it yourself honestly (seams, palette, outline width, AI look),
   fix, repeat at least twice. Opening sequence frames (≥ 8 frames) to `~/jamm-night/silo-v3/opening/` and an
   MP4 + GIF of the opening (solo run only for anything public; all-7 captures are for internal testing only).
7. Rebuild the package with `site/cgf-mods/make-packages.py` (the site package for The Silo) but do not deploy.
8. Tests: unit tests, vanilla byte-identical, full suite green, zero warnings. In-game: silo scene
   (`~/jamm-night/bin/run.sh <win> <label> silo`, shared game lock; `JAMM_SILO_MODE=cycle|save|reload`) solo + all-7.
9. Report `docs/reports/silo-v3.md`. Commit on `exp/silo-v3`; do not merge into main or push main.

## Orchestrator notes (where the "rock breaks in half" comes from)
* `examples/modkit/silo/hooks/hatch_draw.gml` draws two frames of the `silo-doors` sprite recipe
  (`jmk_jamm_silo_recipe_silo_doors`, cropped from `spr_silo` around x≈545, 309×647 px) and slides them
  ±160 px along the hatch axis with a small rotation. Because those crops contain rock/silo body pixels,
  the whole top of the silo appears to split. v3: crop only the actual door/hatch plate (check `spr_silo`
  frames and `spr_silomask` to find the door region), animate only that plate, keep the rest of `spr_silo`
  static under it.
* `hatch_step.gml` swaps the mask to the `open-silo` sprite's passable mask at the end; keep that timing.
* The foreground layer can be an Area API piece with a custom object (`objects[]` with `depth`) drawing a
  cropped recipe sprite of the rock rim at a depth lower than `Lure` (`Lure_Create_0`: `natural_depth = -1.55`,
  hooked: `fish.depth + 0.002`) – e.g. depth −3 – while the existing rocks stay at their vanilla depth.
