# Endless Ocean v1

Take the final vanilla boat east to find sandy shelves, rocky slopes and lit trenches. The generated seabed follows the last placed rock mask, with solid rock all the way down to the room bottom. Pockets under rock outcrops open back into the main water column. Vanilla fish inhabit three depth bands, and occasional wrecks attract a rarer fish.

## Build

Use JAMM with Endless Ocean generator version 2 and the August 2023 legacy VM archive.

```sh
cgfmm modkit check examples/modkit/endless-ocean
cgfmm modkit build examples/modkit/endless-ocean --base /path/to/legacy/data.win --out /path/to/endless-ocean.win
```

You can also add the recipe ZIP in JAMM and enable it in a profile. The final build checks the merged placements for the seam and keeps generated fish, plants and wrecks east of the combined map. To add the recipe to an existing archive, pass that archive as `--base`. Use a separate game copy to try the output.

Each world has a transition of at least three 2,048 px chunks and 488 chunks beyond the reserved edge. Boat7 stops 400 px short of the new eastern limit. Earlier boats retain their original range.

## Choose a seed

Set `seed` in [jamm-recipe.json](jamm-recipe.json) to a whole number from 0 to 2,147,483,646. The default is 271828. The first visit writes `jamm_endless_ocean.ini` beside the usual saves. Change `[World] seed` while the game is closed, or remove that file to use the recipe default again. All slots in that save directory use the same seed.

The seed reproduces each chunk's initial terrain and species. Revisiting an unloaded chunk can bring back fish you caught. Catches, quests and Fishalog progress use the game's save system.

## Adapt the example

The [chunk API](../../../docs/modding/chunks.md) handles loading and release in your controller's context. The [generator source](../../../src/Jamm.Core/Builtins/Worldgen/) shows how shared floor samples, the live collision veto, and depth-specific species selection work. Run `node --test tests/worldgen/chunks.test.mjs` for the synthetic checks. Run `jamm-modmaker bench endless-ocean.win worldgen-bench.win --scene worldgen` to build the in-game validation route or add `--showcase` to capture the panorama frames with a disposable save.
