# The Silo

Lower a lure to the Missile Silo in the deep trench to open its hatch. Bring a rod and boat that can reach the trench, and use a light to find the entrance. Veyl, Radik, Ferrex and Garrusk appear in the Fishalog and use the normal lure, bait and catch rules.

## Build the mod

In a JAMM library configured with the August 2023 legacy archive, run `cgfmm add examples/modkit/silo` and enable **The Silo** in a profile. For a standalone check or build, run these commands from the repo root.

```sh
cgfmm modkit check examples/modkit/silo
cgfmm modkit build examples/modkit/silo --base /path/to/legacy/data.win --out silo-source.win
cgfmm check silo-source.win --base /path/to/legacy/data.win
cgfmm build --base /path/to/legacy/data.win --order silo-source.win --out silo-merged.win
```

## How the shaft was built

1. **Pick a landmark.** In the legacy NewWorld room, `MissileSilo` sits at (33984, 11570). The `launch-shaft` area's bounds start a little to the left and above it. The `follow` anchor translates the layout if another mod moves the silo. Cave Expansion is an example.
2. **Animate an instance.** Place a custom controller at the anchor's coordinates. It starts with `spr_silo` and the closed `spr_silomask`. An approaching lure triggers two shutters cut from `spr_silo`. The controller slides them aside along the hatch's local axes and swaps to the `open-silo` sprite's passable mask at the end. The open flag lives in the active game's save slot under `JAMM_Areas`. MissileSilo's object events stay intact.
3. **Build around the water.** Place vanilla cave rocks and Geiger lights using coordinates relative to the area. Flip and rotate the rocks to close the walls and let the lure enter from above. Each Geiger light starts dark and stores its intended brightness in `silo_glow_alpha`. The controller brings those lights up as the shutters open.
4. **Assemble each fish.** Preserve the donor's natural fin and tail joins. Use `tones` to give the entire fish one compact palette. Veyl combines a plum body and a taller Garder dorsal sail. Radik keeps its green plates and softens the Sizzer fin edges through `edgeSoftness`. Ferrex uses Bagu's native proportions and a copper palette. Garrusk keeps the continuous Shark tail and adds three tapered bands in its petrol-green body palette.
5. **Place each species.** Each fish's `spawn` points to `launch-shaft`. Its `within` rectangle narrows the range. Veyl stays near the upper shaft, while the single Garrusk starts deeper. JAMM chooses free-water points after the profile's edits and the area's solids have been applied. The Create hook opts into deep-water behavior. The Step hook uses the vanilla wall recovery routine and guides roaming fish back toward their home water when they stray.

Use the [Area API](../../../docs/modding/areas.md) to adapt the layout, or the [sprite recipe reference](../../../docs/modding/modkit-reference.md#sprite-recipes) to build a different species. The package ships the manifest, original SVG details and GML hooks. JAMM samples the base pixels from your own archive during the build.
