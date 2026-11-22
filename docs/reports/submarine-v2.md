# Submarine v0.2 verification

## Scope

`examples/modkit/submarine` (`submarine` v0.2.0) upgrades the optional ModKit package on top of the v0.1 baseline:

- **ModKit `tools[]` support**: Added `tools` manifest parsing and validation in [ModKitPackage.cs](../../src/Jamm.Core/ModKit/ModKitPackage.cs), `jamm_tool_register` / object / sprite compilation in [ModKitBuilder.cs](../../src/Jamm.Core/ModKit/ModKitBuilder.cs), CLI count output in [ModKitCommand.cs](../../src/Jamm.Cli/ModKitCommand.cs), and schema docs in [modkit-reference.md](../modding/modkit-reference.md).
- **Composed hull & UI art**: Built `hull` (`520x340`, origin `[260, 130]`, `0.5x` in-world ≈ `260x170` matching Thistle) and `card` (`130x85`) from `parts/hull.svg` plus six `spr_boat_7` bottom-left jet thrusters (`x=84, y=990, w=295, h=190`, masked cleanly via `parts/thruster-mask.svg`); `beam` (`500x320`, origin `[16, 160]`) from `spr_light_radgrad` + `parts/beam-mask.svg`; and `winch-icon` (`76x76`) from `parts/winch-icon.svg`. Zero extracted game assets are stored in the repo.
- **Zoomed underwater camera**: Smoothly transitions (`end-step.gml`, `destroy.gml`) to `1.55x` (`1240x744` view) with `150 * sub_facing` forward look-ahead, allocates `LightingSystem.light_surface` once at zoomed dimensions across the dive/transition, and restores vanilla `800x480` (`hborder=400`, `vborder=240`) cleanly on surface exit.
- **Rotating headlights & peek**: `I`/`K`, `E`/`C`, and gamepad right-stick / `gp_shoulderl` / `gp_shoulderr` tilt `sub_light_pitch` (`[-75, +75]` deg). Pressing `F` / `gp_shoulderlb` peeks up to `320` world units in the light direction (`1.6s` pulse, extendable to `2.5s` while held).
- **Bow Winch gating & 0.1 save compatibility**: Added `"Bow Winch"` (`submarine:bow-winch`, `$250,000,000`, `quests: 0`, `unlocked: false`) to `ToolData` / Tools shop (`winch-step.gml`), keeping `icon_Tools` unlocked so players can purchase it anytime. Casting underwater and rod swing on deck stay locked until Bow Winch is owned. Existing 0.1 `[JAMM_Items]` keys (`u:boat:submarine:submarine`, `eq:boat`) continue loading normally while an absent `u:tool:submarine:bow-winch` key defaults safely to `ToolData.jamm_default_unlocked` (`0`).
- **Bench & autoplay coverage**: Bump to `0.2.0`; extended `--scene submarine` ([SubmarineScene.cs](../../tools/Jamm.ModMaker/SubmarineScene.cs)) and [autoplay.sh](../../tools/vps/autoplay.sh) for zoom, light tilt, peek, pre-winch cast lock, Bow Winch purchase, bow cast, and vanilla view restoration.

## Build & test results

- Release build (`dotnet build -c Release -m:2`): succeeded with **0 Warning(s)** and **0 Error(s)**.
- Automated test suite (`dotnet test -c Release --no-build`):
  - `Jamm.Gui.Tests`: **130 Passed**, 0 Failed, 1 Skipped (opt-in real-file GUI test).
  - `Jamm.Core.Tests`: **647 Passed**, 0 Failed, 17 Skipped (opt-in real-file tests), including `Load_ParsesAndValidatesToolsManifest`, `Build_CompilesToolWithHooksAndItemFindPlaceholder`, `SubmarineExampleManifest_LoadsV2WithBoatAndBowWinchTool`, and `OldSaveWithoutNewToolKeyPreservesBoatKeysAndDefaultsToolToLocked`.
- `cgfmm modkit check examples/modkit/submarine`: valid (`0 area(s), 0 weather(s), 4 sprite recipe(s), 0 fish, 1 boat(s), 0 rod(s), 1 tool(s), 0 hat(s), 0 upgrade(s)`).

## In-game checks & screenshots

Both VPS `--scene submarine` runs completed as `RECORDED` with all 22 telemetry checkpoints passing (`stage = passed`):

- **Solo Console + Submarine** (`sub2-solo`): `~/jamm-night/runs/sub2-solo-submarine/jamm_bench_submarine.csv` (`108,524 ms`): `$450M -> $325M` submarine purchase, zoomed dive (`460`, `1,812`, `4,022` at `1240x744`, `deep=1`), pre-winch cast blocked (`drop=0, lure_state=0`), light tilt (`+38` / `-33.25` deg), peek (`Camera=12895.08,5486.68`, `peek_timer=1.40`), surface vanilla restore (`800x480` at `Camera=12613,1062`), Bow Winch purchase (`$325M -> $75M`, `has_winch=1`), bow cast at depth `1,214` (`lure_state=2, rod_state=3`), reel-in, and second surface restore (`800x480`).
- **Personal pack + Submarine** (`jamm-perf, jamm-hd-lite, jamm-console, jamm-smooth, silo, weather-pack, endless-ocean, submarine`, `sub2-personal`): `~/jamm-night/runs/sub2-personal-submarine/jamm_bench_submarine.csv` (`115,933 ms`): all 22 checkpoints passed identically (`depth_3=4022`, `view=1240x744`, `light_up=38`, `light_down=-33.25`, `peek Camera=12895.08,5486.68`, `surfaced 800x480`, `winch_purchased $75M`, `cast lure_state=2 rod_state=3`, `passed 800x480`).

Chosen screenshots are stored outside the repository at `~/jamm-night/submarine-v2/`:

- Hull above water & boat shop: `jamm_submarine_shop.png`, `jamm_submarine_at_sea.png`, `jamm_submarine_purchased.png`, `jamm_submarine_surfaced.png`
- Hull below water & zoomed camera (`1240x744`): `jamm_submarine_depth_1.png`, `jamm_submarine_depth_2.png`, `jamm_submarine_depth_3.png`
- Rotating headlights (`+38°` / `-33°`) & peek: `jamm_submarine_light_up.png`, `jamm_submarine_light_down.png`, `jamm_submarine_peek.png`, `solo-t60.png`
- Bow Winch & casting after winch: `jamm_submarine_tool_shop.png`, `jamm_submarine_winch_purchased.png`, `jamm_submarine_cast.png`
