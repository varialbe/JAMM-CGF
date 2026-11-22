# The Silo 2.3.1 — Visual Polish

Addressed two visual issues reported against The Silo 2.3.0 (`examples/modkit/silo`) and bumped the package version to `2.3.1`.

## 1. Ferrex silhouette and sprite recipe

**Problem:** In 2.3.0, `ferrex` sampled `spr_fish_l9` (Bagu) as its body layer, keeping Bagu's rounded forehead, puckered lips, and squinted eye, so it still read as a recoloured Bagu in-game, on sonar, and in the Fishalog.

**Fix:** Replaced the Bagu-derived `ferrex` recipe with three layered self-contained SVG parts (`352 × 188`, origin `[44, 118]` at the mouth) in vanilla Cat Goes Fishing flat-vector style:
- `parts/ferrex-fins.svg`: swept spiny primary dorsal fin, rear dorsal finlet, deeply forked crescent tail with dark iron rays, plus swept anal and pelvic fins in old-iron slate (`#485d62`, `#314145`).
- `parts/ferrex-body.svg`: sloped armored brow, open-notch jaw with underslung chin, dark copper dorsal carapace (`#7c4227`), warm copper flank (`#b86c40`), and pale ochre belly (`#d99f68`).
- `parts/ferrex-details.svg`: curved armored gill plate, three tapered iron-slate flank bands, swept pectoral fin (`#667d82`, `#314145`), and a flat vanilla-style eye with an angled brow ridge.

Removed the obsolete Bagu/Noxius/Underfin mask parts (`parts/ferrex-body-mask.svg`, `parts/ferrex-dorsal-mask.svg`, `parts/ferrex-fin-mask.svg`, `parts/gills.svg`).

## 2. Shaft edge grey block and retracted door fit

**Problem:** After the opening animation completed (`dx = -300`), three edge artifacts were visible around the retracted `silo-door` and left shaft edge:
1. In `parts/hatch-frame.svg`, the left recess fill (`#606662`) ended in a straight vertical cut at `x = 319` (`y = 26..115`) while the dark shaft (`#333b39`) cut diagonally from `(310, 81)` to `(333, 118)` and `silo-door` curved back on its lower-right corner. This left an exposed grey rectangular corner of `#606662` at the bottom-left shaft edge.
2. `silo-door` (`y_frame = 12..112`) sat taller than the curved left end of `hatch-frame.svg`, overhanging the top-left outer bezel when retracted.
3. `silo-door` sampled `spr_silo` at `(204, 57, 312, 108)`, which overlapped the dark outer rock rim (`#3d3f3a`) at its bottom-left corner.

**Fix:**
- Updated `parts/hatch-frame.svg` so the `#606662` recess extends cleanly behind the `#333b39` shaft lip (`x = 335..332`) with no square corner exposed past the shaft edge.
- Updated `parts/door-detail.svg` (replacing `parts/door-mask.svg` + the overlapping rock crop) to a clean `#7a7a77` plate with `#656b67` lower bevel and recessed handle (`y = 13..103`, rounded left corners) that fits inside the left bay with zero top-left overhang and zero dark rock triangle.
- Adjusted `hooks/hatch_draw.gml` plate travel from `-300` to `-270` (`dx = -270 * travel * hatch.image_xscale`) so the retracted door's right edge aligns flush with the left shaft edge (`#333b39`) while leaving the full `shaft-mask` opening clear and leaving vanilla rock terrain untouched.

## Verification

| Check | Result | Notes |
| --- | --- | --- |
| `dotnet build -c Release -m:2` | Passed (0 warnings, 0 errors) | Clean Release build |
| `dotnet test -c Release --no-build` | Passed | 643 Core passed (17 opt-in skipped), 130 GUI passed (1 opt-in skipped) |
| `SiloAreaRealTests` (opt-in 10-profile matrix) | Passed (1/1) | Solo, each of 7 inputs, `public5`, `all7` (including Cave Expansion's rotated hatch and `silo_rim` parity) |
| `REPO=$PWD TAG=silo-polish bash ~/jamm-night/n21/build-personal.sh` | Passed | Merged personal package (`own-mods-silo-polish.win`, 18 decisions) |
| In-game `silo-polish-solo` (`--scene silo`) | `RECORDED` | `blocked_samples = 0`, closed/mid `passable = 0`, opened/glow `passable = 1`, 4/4 fish caught and deposited |
| In-game `silo-polish-personal` (`--scene silo`) | `RECORDED` | `blocked_samples = 0`, closed/mid `passable = 0`, opened/glow `passable = 1`, 4/4 fish caught and deposited |

Note: Both in-game runs hit an initial `EXITED` before `~/jamm-night/bin/steam-restart.sh`, then completed as `RECORDED` on retry. Both temporary `~/jamm-night/runs/silo-polish-*-silo/game` copies were removed afterwards.

## Screenshots and crops (`~/jamm-night/silo-polish/`)

- `ferrex-before-after.png` (`ferrex-before.png`, `ferrex-after.png`): 1× in-game comparison captures of Ferrex before (2.3.0), after (2.3.1), and vanilla Bagu.
- `shaft-edge-before-after.png` (`shaft-edge-before.png`, `shaft-edge-after.png`): opened hatch comparison and 2× zoom of the left shaft edge showing the removed grey block and clean bay fit.
- Full captures and CSVs: `solo-closed.png`, `solo-opened.png`, `solo-chamber.png`, `solo-boat.png`, `solo-t60.png`, `solo-scene.csv`, `personal-opened.png`, `personal-chamber.png`, `personal-t60.png`, `personal-scene.csv`.
