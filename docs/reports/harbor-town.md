# Harbor Town v0.1.0 report

## Scope

Harbor Town is a separate optional ModKit package. It adds a dock sign/menu, an upgradeable lighthouse and aquarium,
and a cosmetic golden cat statue. Progress uses `[JAMM_Harbor]` in the active save INI. Art recipes use local vanilla
sprites at build time; the package contains only SVG details and recipe instructions, never extracted game pixels.

The WIP review found that two palette-map keys did not match the source colors in the vanilla landscape sprites. Those
keys now match the actual `spr_landscape3` / `spr_landscape4` colors. The statue was darkened and given a thin outline
derived from the vanilla cat sprite. The command's level and money changes now call the native save event immediately.
The Harbor bench scene exercises the same `harbor` command path used by the console.

## Art review

Private concepts and previews are in `~/jamm-night/harbor-town/concept/`; game captures belong in
`~/jamm-night/harbor-town/art-review/`. No vanilla image files are in the repository.

1. The first sprite preview showed that the landscape palette remap did not match the source pixels, and the statue
   read too pale. The palette source keys and gold tones were corrected.
2. A second preview showed the gold statue needed a clearer silhouette against the dock. A slightly enlarged, darkened
   vanilla cat layer now provides a one-pixel outline beneath the gold cat.
3. The revised lighthouse, aquarium, statue and sign previews were checked again at the five recipe stages (levels
   0, 1, 5, 20 and 100). The recipes retain the vanilla palette and perspective cues and use no outside art.

The Harbor bench scene ran all 15 building/level combinations in both solo and personal profiles and wrote a CSV
with every command result. Both runs recorded without a Harbor Code Error. GameMaker's `screen_save` did not leave its
requested PNGs on disk. An external screen capture during the personal run showed Steam's "logged in on another
computer" sign-in overlay over the game, so it could not serve as an honest dock-art review. The private concept
passes above are complete; an in-game stage-by-stage visual review remains outstanding because concurrent runs on
the shared Steam account displaced the game window. The bench scene now removes the legacy `GameModeMenu` overlay
in its temporary instrumented archive; its updated GML compiles successfully. A final clean solo capture is queued.

## Verification

- `dotnet build -c Release`: passed with 0 warnings and 0 errors.
- `dotnet test -c Release`: 710 passed, 17 skipped, 0 failed (the skipped tests require optional real-file test
  environment variables).
- Harbor ModKit package check: passed; 1 area, 4 sprite recipes, 1 object.
- Harbor-only source build and merged archive verification: passed against the legacy August 16, 2023 base.
- Strict `harbor-personal` profile check: 8 mods, 0 conflicts, 0 warnings. The verified personal archive contains
  Harbor Town, Silo, Weather Pack, Endless Ocean, JAMM Perf, HD Lite, Console and Smooth.
- Solo in-game bench: `RECORDED`; all 15 command rows are present in `jamm_bench_harbor.csv`, with no Code Error.
- Personal in-game bench: first attempt exited during Steam initialization; the runner retry recorded all 15 commands
  with no Code Error. The shared Steam account overlay prevented visual evaluation of the dock screenshots.
- `site/cgf-mods/packages/harbor-town.zip` was generated without changing the site catalog. SHA-256:
  `450b1e7a0308af3e58d9ed5268ca63b508ad221aae4a22285cebeec942ecbcc1`.

## Merge and branch

Local `main` was merged into `exp/harbor-town` in commit `11a9e16`. The `ArchiveBuilder` merge keeps
`InstanceGuard.Apply` last in the final-layer block, and `Jamm.ModMaker` keeps all scene routes. This branch is not
pushed or merged back to `main`.
