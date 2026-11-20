# Endless Ocean v0

Endless Ocean adds a seeded deep-water floor and up to three vanilla fish per chunk. Take the final vanilla boat east to reach it. The floor uses the same collidable CaveRockBig asset as the vanilla deep ocean. The fish use the original species, paths, catching behavior and Fishalog entries.

## Build

Use `cgfmm` from this branch with the August 2023 legacy VM archive.

```sh
cgfmm modkit check examples/modkit/endless-ocean
cgfmm modkit build examples/modkit/endless-ocean --base /path/to/legacy/data.win --out /path/to/endless-ocean.win
```

For a mod pack, build the pack first and pass its verified output as `--base`. The recipe is the last build step. It needs the merged room dimensions and placements to reserve the territory already used by the pack. Use a separate game copy to try the output and keep the original archive and saves backed up. Repeat the recipe step when you rebuild the pack.

The first rock starts at or east of x = 170,000. The recipe also checks the input room's width and placed sprite and mask extents. From that point there are 488 chunks, each 2,048 px wide. The last boat can travel to 400 px before the new east end. Earlier boats keep their usual range, so there is a long stretch of open water before the first chunk in a vanilla build.

## Choose a seed

Set `seed` in [jamm-recipe.json](jamm-recipe.json) to a whole number from 0 to 2,147,483,646 and build the recipe. The default is 271828. The first visit writes `jamm_endless_ocean.ini` beside the usual CGF saves. Change its `[World]` section's `seed` while the game is closed to select a different seed for that save directory, or remove the INI to use the recipe default again.

The same seed reproduces each chunk's initial terrain and fish layout. This is a seed per save directory and applies to all slots there. Chunks regenerate after unloading, including fish that were caught. Normal catches, quests and Fishalog progress still use the game's own save system.

## Adapt the example

[JAMM's chunk scripts](../../../docs/modding/chunks.md) handle bounded loading and release in your controller's context. The [generation source](../../../src/Jamm.Core/Builtins/Worldgen/) contains the terrain profile, fish bands and callbacks. Run the synthetic checks with `node --test tests/worldgen/chunks.test.mjs`. For an automated in-game route, create a fresh bench archive with `jamm-modmaker bench endless-ocean.win worldgen-bench.win --scene worldgen` and run it from a separate game copy with a disposable save directory.
