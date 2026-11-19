# The Silo Mod & `varialbe.xyz/cgf-mods` Staging

## 1. The Silo

The Silo is an optional exploration recipe for the legacy August 2023 game. The maintenance hatch is at (34136, 11496) in NewWorld, east of the original silo site. Put the rod away, use Q to send out your swimming cat, and press E near the hatch once the lights turn green. Diving uses the standard boat and level-25 swimming unlock. The cat needs free paws and enough breath to reach the hatch.

Swim through the flooded launch shaft with WASD or the arrow keys. Follow the rusted ladder, find the control room and use the air bells to refill. Press E near salvage to recover it. Four pickups pay a total of $12,000 per save slot when you return or choose Save and quit. The room's fish are background wildlife. Salvage uses the existing money balance and requires no edits to fishalog or catch logic.

Press E by the upper air bell to go back. Esc brings up the dive pause menu, with controls to resume, leave, or save and quit. Running out of air returns the cat to NewWorld and leaves the current dive's unbanked salvage available for the next visit.

The recipe adds one independent hatch instance to NewWorld and appends the flooded silo room. It preserves the original MissileSilo, including Cave Expansion's changes. The entrance stays at the original clearing when a mod moves MissileSilo. During a visit the live NewWorld is suspended. A return restores the previous room persistence setting. The vanilla save is checkpointed before entry, and reopening a checkpointed save puts the swimmer back at the hatch with the saved boat, rod, catch and quests. A namespaced section of the active save slot keeps the salvage claims and the checkpoint marker.

### Build standalone or with other mods

Import `site/cgf-mods/packages/jamm-silo.zip` in JAMM to build the recipe against the configured legacy base, then add The Silo to the profile. ModMaker also supports direct standalone generation.

```sh
dotnet run --project tools/Jamm.ModMaker -c Release -- silo /path/to/original/data.win /path/to/silo.win
```

The room and hatch art are authored in the recipe's drawing code. The recipe includes the original diver, fish and lamp sprites, and can play the underwater loop from the installed game.

## 2. Staged Static Downloads Site (`site/cgf-mods/`)

`site/cgf-mods/` is a buildless static directory staged privately for orchestrator deployment to `https://varialbe.xyz/cgf-mods/`.

- `site/cgf-mods/index.html`: Static HTML listing separate downloadable packages for all four first-party defaults (`jamm-perf.zip`, `jamm-smooth.zip`, `jamm-hd-lite.zip`, `jamm-console.zip`), the test ModKit mod (`example-fish.zip`), and the non-default Silo mod (`jamm-silo.zip`).
- `site/cgf-mods/catalog.json`: UTF-8 JSON array matching `../inputs/catalog-contract.md` and `../inputs/package-contract.md`.
- `site/cgf-mods/packages/*.zip`: Deterministic pure-source ZIPs built from `site/cgf-mods/sources/` and `examples/modkit/example-fish/` via `python3 site/cgf-mods/make-packages.py`.
- `Jamm.Core.ModKit.RecipePackage`, `ModDownloadService.Validate`, and `ModLibrary.Add` support importing both `jamm-recipe.json` ZIPs and `jamm-mod.json` ModKit ZIPs locally against the configured `base` archive.
