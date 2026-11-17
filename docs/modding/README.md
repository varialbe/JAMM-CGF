# Cat Goes Fishing Modding with JAMM

Welcome to JAMM's modding guide for the legacy VM build of *Cat Goes Fishing*.

JAMM lets multiple mods run side-by-side without conflicts by registering new fish, boats, hats, and rod upgrades through `jamm_init_*` scripts instead of editing shared game objects or rooms.

## Where to start

1. **[Getting Started (Your First Fish in 10 Minutes)](getting-started.md)** — scaffold a ModKit package, draw a sprite, validate, build, and test it with `cgfmm`.
2. **[ModKit Reference](modkit-reference.md)** — complete `jamm-mod.json` schema, sprite/origin conventions, GML hook entry points, and `cgfmm modkit` CLI reference.
3. **[Catalog API](../catalog-api.md)** — how JAMM assigns conflict-free fish catalog slots and name-keyed `[JAMM_Catalog]` saves.

## Examples in this repository

* [`examples/modkit/example-fish/`](../../examples/modkit/example-fish/) — adds **Goldie** (small shallow fish) and **Glowfin** (night-only medium fish with a `step` hook that pulses `image_blend` and darts from the lure).
* [`examples/modkit/showcase-upgrades/`](../../examples/modkit/showcase-upgrades/) — adds five idempotent rod upgrades (**Turbo Reel**, **Deep Diver**, **Iron Line**, **Fish Magnet**, **Golden Touch**) demonstrating `apply` and `step` hooks.
* [`examples/modkit/make-art.py`](../../examples/modkit/make-art.py) — Python stdlib script used to generate the example pixel art PNGs.

## ModKit workflow at a glance

```text
my-fish-mod/
  jamm-mod.json       # mod metadata + fish/boats/hats/upgrades definitions
  art/*.png           # PNG sprites (or horizontal sprite strips)
  hooks/*.gml         # optional GM:S 1.4 GML hooks
```

* `cgfmm modkit new <dir>` — create a starter package.
* `cgfmm modkit check <dir>` — validate `jamm-mod.json`, PNG headers/frames, and hook paths.
* `cgfmm modkit build <dir>` — compile the package onto the legacy base `data.win` into a standalone mod `.win`.
* `cgfmm add <dir>` — build and import a ModKit package folder directly into your JAMM mod library.
