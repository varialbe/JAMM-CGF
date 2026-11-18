# The Silo Mod & `varialbe.xyz/cgf-mods` Staging

## 1. The Silo Mod (`Jamm.ModMaker.SiloMod`)

`SiloMod` is an optional, isolated, non-default mod (`jamm-silo`) targeting the untouched legacy August 2023 `data.win`.

- **Existing door asset**: Activates `MissileSilo` at `(33984, 11570)` in `NewWorld`, using the game's own `spr_silo` (closed dome) and 5-frame `spr_silomask` (hatch opening mask/frames) alongside an original radial beacon light (`obj_jamm_silo_beacon` descending from `AbstractLight`) so the hatch is visible in the deep trench.
- **Visible opening animation**: Approaching `MissileSilo` with `CatSwim` or an active `Lure` smoothly animates `spr_silomask` frames `0..4`, lifts and fades the dome overlay, reveals rising bubbles and a sanctuary glow, and prompts the player (`"Silo Open - Swim in or press W / E to descend"`). Moving away gently closes it.
- **Pleasant new area (`room_jamm_silo`)**: Descending transitions to a dedicated `1600x1100` underwater sanctuary room (`obj_jamm_silo_area`) with procedural water gradients, sunlit shafts from the entry hatch, stepped cavern ledges, swaying seagrass, glowing crystal clusters, drifting motes, friendly sanctuary fish (`spr_jamm_silo_fish`), and a cozy diving cat (`spr_jamm_silo_diver`) controlled via WASD / Arrow keys / mouse.
- **Safe exit & state preservation**: Sets `NewWorld` `room_persistent = true` before entering and pauses `SaveManager` and `NavBrain` during the visit so `NewWorld` instances, fish, money, quests, and save state remain untouched. Swimming back up into the top hatch or pressing `Esc` / `Q` / `Backspace` returns safely above the hatch `(33960, 11470)` with replenished breath and a return cooldown.
- **Original assets**: Only original procedural art (`spr_jamm_silo_glow`, `spr_jamm_silo_diver`, `spr_jamm_silo_fish`) and GML are included. Zero game archive bytes, copied sprites, or extracted GML are shipped.

### Build standalone via ModMaker

```sh
dotnet run --project tools/Jamm.ModMaker -c Release -- silo /path/to/original/data.win /path/to/silo.win
```

## 2. Staged Static Downloads Site (`site/cgf-mods/`)

`site/cgf-mods/` is a buildless static directory staged privately for orchestrator deployment to `https://varialbe.xyz/cgf-mods/`.

- `site/cgf-mods/index.html`: Static HTML listing separate downloadable packages for all four first-party defaults (`jamm-perf.zip`, `jamm-smooth.zip`, `jamm-hd-lite.zip`, `jamm-console.zip`), the test ModKit mod (`example-fish.zip`), and the non-default Silo mod (`jamm-silo.zip`).
- `site/cgf-mods/catalog.json`: UTF-8 JSON array matching `../inputs/catalog-contract.md` and `../inputs/package-contract.md`.
- `site/cgf-mods/packages/*.zip`: Deterministic pure-source ZIPs built from `site/cgf-mods/sources/` and `examples/modkit/example-fish/` via `python3 site/cgf-mods/make-packages.py`.
- `Jamm.Core.ModKit.RecipePackage`, `ModDownloadService.Validate`, and `ModLibrary.Add` support importing both `jamm-recipe.json` ZIPs and `jamm-mod.json` ModKit ZIPs locally against the configured `base` archive.
