# Task A: Submarine 0.2 (branch `exp/submarine-v2`, Gemini Hydrogen)

Base: Submarine 0.1 on main (`examples/modkit/submarine`, design `docs/design/submarine.md`, report
`docs/reports/submarine.md`, ModMaker scene `--scene submarine`). Feedback from Vel (Discord) and the user:

1. **Hull**: large, wide yellow oval in vanilla style with jet thrusters like the ones at the bottom left of the
   vanilla Tech boat sprite, several per side. Overall size roughly like the fish Thistle. Build it from local
   game pixels via composed sprite recipes / SVG parts like the other ModKit mods – never copy TGE/PWB or any
   extracted art into the repo.
2. **Camera**: under water the view zooms out so the submarine fits, with more room ahead in the facing direction.
   Smooth transitions when diving/surfacing; restore the vanilla view exactly when leaving the submarine.
3. **Rotating headlights**: the lights can be rotated (input + gamepad); the camera can briefly "peek" in the
   light direction.
4. **Casting from the submarine** only after an expensive upgrade (e.g. "Bow Winch", money sink >= 250M);
   before that the submarine is exploration only. Integrate with the existing shop/upgrade UI.
5. **Save compatibility** with 0.1 saves (old keys keep working, new keys default safely).
6. Extend ModMaker `--scene submarine` (and the autoplay bot `tools/vps/autoplay.sh` if needed) to cover zoom,
   light rotation, peek, winch purchase and casting. Bump the package version to 0.2.0.
7. Verify: `dotnet build -c Release` zero warnings, `dotnet test -c Release` green; in-game on the VPS
   (`~/jamm-night/bin/run.sh <win> <label> submarine 900` with `JAMM_MODMAKER_DLL=<this worktree's
   tools/Jamm.ModMaker/bin/Release/net10.0/jamm-modmaker.dll>`; build a solo archive with
   `dotnet src/Jamm.Cli/bin/Release/net10.0/cgfmm.dll modkit build examples/modkit/submarine --base
   ~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win --out <path>`; personal package via
   `REPO=$PWD TAG=sub2 bash ~/jamm-night/n21/build-personal.sh` plus adding the submarine package).
   The game runner holds a shared lock (it may wait). On EXITED (Steam init) run `~/jamm-night/bin/steam-restart.sh`
   and retry. Delete `~/jamm-night/runs/<label>-*/game` copies afterwards (disk is tight).
8. Report `docs/reports/submarine-v2.md` with build/test results and in-game screenshots (hull above and below
   water, zoomed camera, lights rotated, peek, casting after the winch). Copy the chosen PNGs to
   `~/jamm-night/submarine-v2/`.
AGENTS.md applies (never modify input game files, never commit *.win or game data, zero warnings, XML docs).

## Round 2 (orchestrator review of f44e0a7, 2026-11-22 06:20)
Hull, thrusters, zoom, light rotation, peek and the Bow Winch look right – good work. One real bug in the
screenshots `~/jamm-night/submarine-v2/jamm_submarine_depth_2.png` and `jamm_submarine_peek.png`:
1. **Vanilla HUD is misplaced while zoomed**: the "Lvl. 30" XP bar is drawn in the middle of the screen over the
   submarine instead of at the bottom; the "Sell" button floats middle-left; "New Quest"/"Wait" are cut off at the
   right edge. Everything drawn in GUI/view space must stay exactly where vanilla draws it at 800x480 (or scale
   consistently with the window), independent of the underwater zoom. Find how vanilla positions these (view-relative
   Draw vs Draw GUI) and fix it without changing vanilla behaviour when the submarine is not used.
2. Check the other screenshots (`jamm_submarine_*.png`) for any other HUD/menu element affected by the zoom (shops,
   catch popup, Fishalog, pause/save menu) and fix those too.
3. Re-run `--scene submarine` solo + personal, update the report and screenshots (before/after crop of the HUD).
`dotnet build -c Release` zero warnings, `dotnet test -c Release` green. Commit on this branch; do not push.

## Round 3 (orchestrator review of 6ff5290, 2026-11-22 12:15)
The HUD fix works (see `hud_before_after_depth_2.png`) – thanks. One blocking issue:
`ZoomUiRuntime.Apply` runs for every archive that has `Camera` and `Girl`, i.e. **every** CGF build, and patches ~20
vanilla HUD/menu objects even when Submarine is not part of the build. Builds without the feature must stay
byte-identical to before.
1. Install `ZoomUiRuntime` only when a mod in the build actually requests view zoom (Submarine declares it, e.g. via
   a ModKit flag/capability), never by default.
2. Add tests: (a) vanilla build and (b) a build with another ModKit mod (e.g. weather-pack) produce byte-identical
   code entries for all patched objects compared to before this branch; (c) Submarine build still gets the patches.
3. `dotnet build -c Release` zero warnings, `dotnet test -c Release` green; re-run `--scene submarine` solo once.
Commit on this branch; do not push.
