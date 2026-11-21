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
