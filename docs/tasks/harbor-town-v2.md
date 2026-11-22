# Task D: Harbor Town 0.2 (branch `exp/harbor-town-v2`, Codex Luna)

Implement phase v0.2 from `docs/design/harbor-town.md` in `examples/modkit/harbor-town` (v0.1 on main):
1. **Research station** building with the capped logarithmic bonuses from the "Research bonuses (v0.2)" table
   (sell price, bite rate, lure luck). Sell price bonus applies after the 100M fish value cap (max $110M per fish).
   Five visual stages like the other buildings, composed from local vanilla props via recipes/SVG parts (no
   extracted or TGE/PWB art in the repo).
2. **Donation milestones** $1M/$1B/$1T/$1Qa: money disappears; unlock titles under the wallet ("Harbor Patron",
   "Harbor Baron", "Harbor Tycoon", "Harbor Legend"), a hat recolour and dock decorations (flags, lanterns).
3. Menu, console `harbor` command, and save format extended; v0.1 saves keep working (missing keys = 0).
4. Balancing check with $1B and $1T saves (console `harbor money <n>`); bonuses must never exceed their caps.
5. Extend the ModMaker harbor bench scene for research levels, a donation, and title display. Version 0.2.0.
6. `dotnet build -c Release` zero warnings, `dotnet test -c Release` green; in-game solo and personal
   (`REPO=$PWD TAG=harbor2 bash ~/jamm-night/n21/build-personal.sh` + harbor-town) via `~/jamm-night/bin/run.sh`
   with this worktree's ModMaker dll. Shared game lock (may wait); on EXITED `~/jamm-night/bin/steam-restart.sh`
   and retry; delete `~/jamm-night/runs/<label>-*/game` copies.
7. Codex is hunting Harbor Town crashes in parallel on `fix/feature-crash-hunt`. Before you finish, merge that
   branch into yours if it has commits (`git merge fix/feature-crash-hunt`) and resolve conflicts, then rebuild/test.
8. Report `docs/reports/harbor-town-v2.md` with screenshots (research station stages, titles, decorations);
   copy PNGs to `~/jamm-night/harbor-town-v2/`.
AGENTS.md applies. Commit on this branch; do not push.

## Round 2 (orchestrator review, 2026-11-22 16:10)
Implementation (Research Station, donations, caps, saves) looks complete and tests are green. Two blockers before merge:
1. **Unreported Code Error** in `~/jamm-night/runs/harbor-town-v2-personal-retry-harbor/game.log`:
   `trying to index a variable which is not an array at gml_Object_SaveManager_Other_14`, called from
   `gml_Object_SaveManager_Other_11`. Find the cause offline now (decompile the personal Harbor build's SaveManager
   events, compare with the legacy base and with Harbor's save hooks / `[JAMM_Harbor]` keys / v0.1 save loading),
   fix the root cause, add a regression test (synthetic archive + an old v0.1 save and a fresh save).
2. **Harbor scene never reached the feature**: the CSVs are header-only (`harbor_ready=0`), so `RECORDED` proves nothing.
   Fix the ModMaker harbor scene so it actually loads Harbor, walks through the stages and writes rows; make the scene
   fail (CODE ERROR or non-zero exit) when it records no rows.
Game runs are currently blocked by `~/jamm-night/PAUSE-GAMES` (user tests on the PC) – never delete it or bypass the
lock; do all offline work now and queue the in-game runs (solo + personal, plus autoplay 20 min with Harbor in the
personal package), they run when the pause ends. Then screenshots of Research Station stages, titles, decorations.
`dotnet build -c Release` zero warnings, `dotnet test -c Release` green. Commit on this branch; do not push.
