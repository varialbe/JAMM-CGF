# Task: base-mod mode (large overhaul mods + their addons)

Future large overhauls (e.g. "CR2") will have addons made against the overhaul, not against vanilla. Diffing such an addon against
vanilla attributes the whole overhaul to the addon and produces huge false conflicts. Branch `exp/base-mod` in `~/jamm-wt/base-mod`,
from main. Report `docs/reports/base-mod-mode.md`.

## Model
* A mod can declare itself a **base** (manifest/metadata field, e.g. `jamm.json` / ModKit manifest `"role": "base"`, plus a detection
  heuristic suggestion only, never automatic: e.g. > N % of vanilla code changed). Addons declare `"base": "<base mod id>"` (optional
  version/hash constraint). Legacy `data.win` mods without metadata: the user can set role/base in the profile (CLI + GUI).
* Build: the base is applied first against vanilla (as today). Each addon is diffed against the **base's original data.win** (the
  file the addon was made from), not against vanilla, and its patch is applied on top of the merged base result. Conflicts between
  addons are computed relative to the base. Vanilla-targeting mods combined with a base: allowed but `check` warns
  `[CGF-BASE-001] <mod> targets vanilla but profile uses base <base>; expect conflicts` with a short explanation.
* An addon whose base is missing/disabled → error with a clear message; addon with a base hash mismatch → warning + continue (or
  error if strict). Two bases in one profile → error.
* Patch storage: patches of an addon are keyed to the base hash so changing the base invalidates them (like base changes today).

## GUI / CLI
* Profile view groups base + addons (base on top, addons indented, can't reorder an addon above its base). Mod library shows role.
* CLI: `cgfmm profile base <profile> <mod>`, `cgfmm profile addon <profile> <mod> <base>`, `cgfmm profile role <profile> <mod> none`;
  `check` and `build` print the grouping.

## Tests (synthetic fixtures, CR2 does not exist yet)
Build fixtures on the legacy base with the existing test helpers: a "base" that changes many scripts/objects/rooms and adds assets; two addons
made from the base data.win (one changes a base-added object, one adds a fish); one vanilla mod. Check: addon patches are small (only their real
changes), build of base+addons equals applying them sequentially by hand, conflicts between the two addons are detected correctly, warnings
for vanilla mod + base, missing base error, profile persistence, GUI grouping (headless GUI tests like the existing ones). One real-file smoke
test: treat Cave Expansion as a "base" and a tiny synthetic addon built from Cave's data.win; build + `~/jamm-night/bin/run.sh <win> base dock 600`
RECORDED (one game at a time; other agents share the VPS: check `pgrep -f "Cat Goes Fishing.exe"` and wait).

## Rules
AGENTS.md. Zero warnings, all tests green, docs (docs/modding + user docs) updated. Commit on the branch, no push, no merge to main.
