# Phase 6a — Performance mod + Smooth mod to real 120 FPS (implementer: Codex)

Read AGENTS.md first. Work only in `tools/Jamm.ModMaker/` (plus this spec's report). Do NOT touch
`HdMod.cs` (Phase 6b runs in parallel on another branch). Do not commit, push or change git config;
the orchestrator commits after review.

## Background (measured by the orchestrator, do not re-derive)
- CGF is GameMaker 1.4 (bytecode 16). Logic is frame-based: `Girl_Step_0` forces `room_speed = 30`
  every step (60 while Tab is held = fast-forward). Mods ship as full `data.win` files; JAMM merges them.
- `tools/Jamm.ModMaker` builds example mods from the ORIGINAL data.win:
  `dotnet run --project tools/Jamm.ModMaker -c Release -- smooth <original.win> <out.win> [--debug]`.
  Code is written in GML and compiled with UndertaleModLib's `CompileGroup` (works for this game).
  Not every GMS function exists for bytecode 16 (e.g. `display_get_frequency` fails to compile).
- `SmoothMod.cs` (read it fully) renders extra frames between logic ticks: everything frozen with
  `instance_deactivate_all`, exact wake of the frozen ids in End Step ((de)activation is applied only
  between events), interpolation of positions/angles/views by draw time, culling of off-screen
  scenery / plain fish / default-drawn objects, fish draw side effects restored on render-only frames.
  Hard-won constraints, keep them:
  - Never wake instances the game deactivated itself (Fish with `deactivated`, zoning). Draw code of
    such instances crashes (`Magmer_Draw_0` reads `light`).
  - Draw code freely reads other instances (`ParallaxBackground_Draw_0` reads `Arcade.x`): never keep
    an arbitrary subset frozen during draw.
  - Fish draw has side effects (`fish_predraw_switchcase`: `frames_since_last_flip++`, flip, angle;
    `fish_basicdraw`: image_index/alpha). One draw per logic tick must stay the observable behaviour.
- Debug build (`--debug`) appends one line per second to `jamm_smooth_log.txt` in the game save
  folder (frames, ticks, frame-time stats, per-phase ms). `profile <in.win> <out.win>` instruments
  every object event and script (time since previous event start, attributed to it).
- **The bottleneck is the game's own tick, not the smooth mod.** Real merged profile (Abyss, Cave,
  Colour, HD, Smooth; NewWorld has ~6,700 instances, ~3,000 fish, 1,866 CaveRockBig):
  one tick takes ~34 ms under Wine, so even 30 ticks/s are not reached (26-29). Base game alone:
  ~17 ms logic + ~5 ms draw per tick. A render-only frame costs ~4 ms.
  For a true 120 FPS without hitches a tick frame must fit in ~8 ms.
- Profile (merged, % of frame time, time attributed "since previous instrumented start", so it
  includes engine work after the call): fish_check_trawler 11 (really: rest of Fish_Step_2 + engine
  per-instance work), fish_disabled 9 (zoned fish, state 12), Fish_Step_2 6, fish_predraw 6,
  fish_basicdraw 6, fish_avoid_fish 5.5, SceneItem_Step_2 4.6, fish_standard_step12 4, extended_x 4,
  Fish_Draw_0 4, SceneItem_Draw_0 3.4, check_lunge_catswim 3, is_it_night 1.3, ...
- In the merged build `FishThinner_Step_0` has `do_zoning = 1` (from the Colour mod): far fish are put
  in state 12 (`fish_disabled`: x = xprevious) but still run the whole Fish_Step_2 every tick.

## Goal
1. A new **Performance mod** (`perf` command, new `PerfMod.cs`) that makes the game's own tick much
   cheaper. Ideas from the user (evaluate, implement what is safe, measure):
   - compute expensive checks once per tick instead of once per fish (e.g. `is_it_night()`: note it
     has a side effect on `AmbianceController.lockDay` and loops `with (Gear)`; a per-tick cache must
     give identical results within the tick and keep the side effect),
   - defuse the all-against-all loop in `fish_avoid_fish` (find the callers and argument types; the
     sum of forces over neighbours within the radius must stay the same),
   - throttle far, already-paused standard fish (e.g. zoned state 12 and/or off-screen `isVisible == 0`
     far from Lure/CatSwim/camera) with explicit exceptions for special creatures (Dragon, Noctis,
     Torby, Ghost, anything with `always_zoned`, hooked/netted fish, ...). Whatever you throttle must
     not change what the player can observe: catalog/live counts, night behaviour when the fish comes
     back into view, respawn timers (`current_time` based), etc. Document every behavioural difference.
   - anything else the profile justifies (event_perform dispatch, SceneItem parallax for off-screen
     items, ...). Prefer few, well-argued changes over many risky ones.
   Patch existing game code as narrowly as possible (line-level edits through decompile→edit→compile),
   because JAMM three-way-merges against other mods: small hunks merge, whole rewrites conflict.
   The mod must build from the ORIGINAL data.win and must not depend on the Smooth mod.
2. **Smooth mod**: keep its overhead minimal (it is still ~2 ms per tick in the merged build);
   when render-only frames are impossible (tick frame longer than the tick interval) it must cost
   ~nothing. Keep all constraints above. Make 120 the default target (F7 still cycles 60/120/off).
3. Report expected gains with numbers you can obtain offline (instruction counts, number of
   instances affected per tick, complexity before/after). The orchestrator runs the in-game
   measurements (`--debug` logs, `profile`) on the real files; design the debug output so that a
   one-line-per-second log shows tick-frame ms and render-frame ms separately.

## Rules
- Never write next to or over the original game files. Write test outputs to `out/phase6a/` in the
  repository (gitignored). Original: `/run/media/varialbe/Festplatte 2/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600/data.win` (read-only).
- `dotnet build -c Release` of the tool with 0 warnings; every new command must run end-to-end on the
  real original file and produce a data.win that `cgfmm verify` accepts.
- Small readable classes, XML doc comments on public types, comments explaining WHY for every game
  quirk you rely on.
- Finish with a short report in `docs/reports/phase6a.md`: what changed, measured/derived gains,
  behavioural differences, open risks.
