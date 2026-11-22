# Task B: Silo polish (branch `exp/silo-polish`, Gemini Hydrogen)

The Silo (`examples/modkit/silo`, current version on main, reports `docs/reports/silo*.md`) has two visual
leftovers reported by the user:
1. **Ferrex** still looks like a brown Bagu (recoloured vanilla fish). Give it its own recognisable look in vanilla
   style (shape, not only colour) via the ModKit sprite recipe/SVG parts used by the other Silo fish (`veyl`,
   `radik`, `garrusk`). It must read well on the sonar and in the Fishalog/catch screen.
2. **Small grey block at the shaft edge after the opening animation.** Find which sprite/tile/instance leaves it
   (compare frames before/after opening: `~/jamm-night/runs/*silo*/t60.png`, ModMaker `--scene silo`), fix the
   root cause without changing vanilla terrain elsewhere.
Verify: `dotnet build -c Release` zero warnings, `dotnet test -c Release` green; in-game
`~/jamm-night/bin/run.sh <win> <label> silo 900` (solo silo build and the personal package,
`REPO=$PWD TAG=silo-polish bash ~/jamm-night/n21/build-personal.sh`) with
`JAMM_MODMAKER_DLL=<this worktree>/tools/Jamm.ModMaker/bin/Release/net10.0/jamm-modmaker.dll`.
Shared game lock (may wait); on EXITED run `~/jamm-night/bin/steam-restart.sh` and retry; delete
`~/jamm-night/runs/<label>-*/game` afterwards. Bump the silo package patch version.
Report `docs/reports/silo-polish.md` with before/after crops (Ferrex close-up, shaft edge); copy the PNGs to
`~/jamm-night/silo-polish/`.
AGENTS.md applies (never commit *.win or game data, no TGE/PWB assets, zero warnings).
