# Harbor Town (v0.2.0)

Harbor Town is an optional endgame money sink. The package adds a lighthouse, a small aquarium, a golden cat
statue, a research station, and a sign near the dock. Donations unlock titles and recipe-built dock flags and
lanterns. It does not change vanilla save keys or add collisions to the walking path.

Build it with the legacy August 16, 2023 archive:

```sh
cgfmm modkit check examples/modkit/harbor-town
cgfmm modkit build examples/modkit/harbor-town --base /path/to/legacy/data.win --out harbor-town-source.win
cgfmm check harbor-town-source.win --base /path/to/legacy/data.win
cgfmm build --base /path/to/legacy/data.win --order harbor-town-source.win --out harbor-town.win
```

The source archive contains namespaced GML and sprite recipes. The build samples the player's own `spr_landscape3`,
`spr_landscape4`, and `spr_cat` pixels locally; the package ships only the SVG details and recipe descriptions.
The area follows the vanilla `Cliff` landmark, and its decorative instances are non-solid.

Walk to the sign and press **E** (or the gamepad's X button) to open the menu. Up/down selects a building, Space
buys or upgrades it, and Escape closes the menu. Mouse clicks work on the cards and close button. Upgrade cost is
`base × 1.6^level` through level 40, then grows linearly; the displayed amount uses JAMM's compact money formatter
when available. Lighthouse light gain is logarithmic and capped at 40%. The aquarium displays up to six caught
species as small animated drawings; they are not game instances and cannot be hooked or caught.

The level and money test command is available when JAMM Console is also enabled:

```text
harbor
harbor lighthouse 20
harbor aquarium 5
harbor statue 100
harbor research 100
harbor money 1000000000
harbor donate 1000000
```

The levels and cumulative donations are read from and written to `[JAMM_Harbor]` in the active vanilla save INI.
Missing keys in v0.1 saves default to zero. The same values are written to the main and backup slots through
the native save events. Removing the mod leaves the section untouched. The Research Station grants
`min(cap, a × ln(1 + level))` bonuses: sell price (a=2%, cap 10%), bite rate (a=1.5%, cap 8%), and lure luck
(a=1%, cap 5%). Sell price applies after the vanilla/JAMM $100M fish value cap and is clamped to $110M.
Donations accrue toward $1M, $1B, $1T, and $1Qa and unlock Harbor Patron, Harbor Baron, Harbor Tycoon, and
Harbor Legend in order. Each completed tier adds dock decorations.

## Art review

The private design, extracted previews, and game screenshots are kept under `~/jamm-night/harbor-town/`; none of
the vanilla image data is part of this package. `jamm-modmaker bench harbor-town.win harbor-scene.win --scene harbor`
captures all four buildings at levels 0, 1, 5, 20, and 100 as `harbor_art_<building>_<level>.png` in the game
working directory and writes `jamm_bench_harbor.csv` alongside them. The scene funds and completes all donation
tiers before capture so the title and decorations are visible in the bench run.
