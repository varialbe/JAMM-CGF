# Submarine v0.1 verification

## Scope

`examples/modkit/submarine` is an optional ModKit package, version 0.1.0. The build-time sprite recipes combine source assets from the user's legacy archive with vector details; the package does not distribute extracted game art. It adds a $125M boat shop entry, player-controlled underwater movement and ascent, a bow light, 110 seconds of oxygen with surface refill, depth readout, and short-line fishing from the bow. The starter hull is rated to 4,800 world units. Upgrades, hull pressure, and a shared dive query remain future work described in `docs/design/submarine.md`.

## Implementation review

Reviewed the interrupted WIP and retained its package, lifecycle hooks, boat shop registration, source-only art recipes, bench scene, and design findings. Merged local `main` before continuing. The merge keeps `InstanceGuard.Apply` as the final layer and preserves all ModMaker scenes.

The first solo game passes exercised purchase, dive, light, casting, and ascent, then exposed two vanilla HUD assumptions in `CatalogData_Draw_0`: a caught fish instance may not define `name` or `value`. `FishRuntime` now supplies the object's name when absent and gives a missing display value a safe zero fallback. A regression test covers both guards.

## Verification

- Release build: zero warnings and zero errors.
- Automated suite: 574 Core tests and 130 GUI tests passed; 16 Core and 1 GUI tests skipped because they require opt-in local files.
- The new caught-fish HUD regression test passed.
- An empty build round-tripped the configured legacy archive byte-for-byte (SHA-256 `dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1`).
- The solo Console + Submarine archive passed semantic reference verification and pack acceptance (Console 3/3, Submarine 6/6).
- The personal profile archive passed reload, reference, decompile, and compile verification with the existing pack set plus Submarine.
- `site/cgf-mods/make-packages.py` generated the source package ZIP; `submarine.zip` SHA-256: `d5814f3dbeba4eb86a6c65a3997ad3a9a922ad0f1a9d64cac0a3317ecf67ab1d`.

## In-game checks and art review

In-game solo and personal profile runs use `~/jamm-night/bin/run.sh` and the shared game lock. The final runs are in progress behind other queued sessions; their outcomes, depth telemetry, and capture paths will be added when complete. Two art review rounds are stored outside the repository at `~/jamm-night/submarine/art-review/round1-world.png`, `round1-cards.png`, `round2-world.png`, and `round2-cards.png`. The second round improves the hull silhouette and card readability while keeping the flat vanilla palette and outline.

Screenshots and video are stored outside the repository at `~/jamm-night/submarine/`; no game archives or extracted game assets are committed.
