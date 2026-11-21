# Task: fix Endless Ocean sonar crash (branch `fix/ocean-sonar-crash`)

Found by autoplay v2 (Gemini branch `exp/autoplay-v2`) on the personal package
`~/jamm-night/personal-tge/own-mods-2026-11-22.win` (perf, hd-lite, console, smooth, silo, weather-pack,
endless-ocean), seed 11002, about 10 min in, far east in Endless Ocean (x ≈ 152,600, 5 chunks loaded, Boat7,
diving and casting):

```
FATAL ERROR in action number 1 of Draw Event for object SonarPanel:
Variable Dijon.s5(101204, -2147483648) not set before reading it.
at gml_Object_SonarPanel_Draw_0
```

Evidence: `~/jamm-night/autoplay/apv2b-own-11002/` (game.log, error.txt, error.png, actions.tsv,
jamm_autoplay.csv, shots/).

## Do
1. Find in the legacy base (`~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win`) where vanilla
   fish get `s5` (and siblings the sonar reads) and why Endless Ocean generated fish (`jmk_endless_ocean_fish`,
   `jamm_chunk_stream`, `src/Jamm.Core/Builtins/EndlessOceanMod.cs`) can reach the SonarPanel draw without it
   (creation order, deactivation, variable copy, Perf mod interaction, ...). Fix the root cause in the Endless Ocean
   generator; only if that is impossible add a narrow guard. Do not modify vanilla behaviour for vanilla fish.
2. Add a regression test (synthetic archive) and, if feasible, a ModMaker worldgen/sonar scene step that
   streams generated fish while the sonar panel is drawn.
3. Verify in-game: rebuild the personal package
   (`REPO=$HOME/jamm-wt/ocean-sonar TAG=sonar-fix bash ~/jamm-night/n21/build-personal.sh`), then run
   `~/jamm-night/bin/run.sh <win> sonarfix worldgen 900` and the autoplay v2 bot from
   `~/jamm-wt/autoplay-v2/tools/vps/autoplay.sh` with
   `JAMM_MODMAKER_DLL=$HOME/jamm-wt/autoplay-v2/tools/Jamm.ModMaker/bin/Release/net10.0/jamm-modmaker.dll`,
   15 min, seed 11002 (and one more seed). Shared game lock; Steam restart via ~/jamm-night/bin/steam-restart.sh.
4. `dotnet build -c Release` zero warnings, `dotnet test` green, short report `docs/reports/ocean-sonar-crash.md`.

AGENTS.md applies. Commit on this branch; do not push or merge into main.

## Round 2 (Gemini; orchestrator review of 2c49631)
Codex's fix works in-game (seeds 11002 and 11004, 15 min each, no error). Review points to fix on this branch:
1. `jmk_endless_ocean_sonar.gml` reads `SonarPanelAdvanced.advancedDrawEnabled` and `SonarPanel.cave_sonar`
   without checking the instances exist. Verify in the legacy base whether both always exist in every room
   where Endless Ocean streams fish; guard with `instance_exists` (fall back to the basic branch) either way.
2. It hardcodes asset indices (`object_index == 132`, `s1 = 238`, `424`, ...). Mods can add assets, so replace
   them with asset names resolved at build time or at runtime (`asset_get_index`) – they must match the vanilla
   `SonarPanel_Step_1` semantics exactly. Add a test that the generated code contains no bare vanilla indices.
3. Follow-ups from your audit `docs/reports/ocean-fish-parity.md` (now on main), inside the Endless Ocean
   generator only (never change vanilla fish behaviour):
   - set `base_lunge_mod` on generated fish at creation (same value `Rod_Other_10` would give),
   - do not stream `Torby` (and singleton/quest fish such as `Ancient_Kingfish`) from chunks, or make them safe,
   - clear `deactivated = 0` when `jmk_endless_ocean_dispose` keeps a reactivated fish.
4. `dotnet build -c Release` zero warnings, `dotnet test` green, update `docs/reports/ocean-sonar-crash.md`.
The orchestrator runs the game. Every command in the foreground; simple single commands (no `&&`, `;`, pipes,
redirections, no `rm`); if denied, write a small script file and run it. Do not commit.
