# Phase 22c + 22d — Custom upgrades API, fish API, JAMM ModKit

Both build on `docs/tasks/phase22-modding-api.md` (read it first) and the audit
`docs/reports/phase22-modding-audit.md` (§5 upgrades, §6 fish).

## 22c — Custom upgrades (`exp/api-upgrades`, after 22b is merged)

`jamm_upgrade_register(key, name, desc, desc2, cost, icon_sprite, max_stacks, apply_script, step_script)`

* Appends at `UpgradeData` index `array_length_1d(UpgradeData.unlocks)` (mods such as Pondweed may already have
  appended legacy upgrade 18, so never assume 18), sets name/desc/desc2/cost, `unlocks = 1`, `jamm_key`,
  `disp = icon_sprite` (-1 = none), `jamm_max_stacks`, `jamm_apply`, `jamm_step` (script ids or -1), updates
  `UpgradeData.nItems`. Idempotent by key.
* Icons: patch upgrade card / slot / UpgradeData draw code so ids with `disp >= 0` draw that sprite (never index
  `spr_upgradeicons` beyond its subimage count).
* Limits: patch `UpgradeCard` Other_10 so ids registered through the API enforce `jamm_max_stacks` on the current rod
  (count `UpgradeData.upg[Rod.rod_id, 0..7] == id`) and show `Message_TooManyUpgrades` when exceeded. Base ids unchanged.
* Effects: patch the end of `Rod` Other_10 (the upgrade recompute, which resets `Rod.upgrades[]` first): build
  `Rod.jamm_upg[id] = stacks` for API ids, then call `script_execute(UpgradeData.jamm_apply[id], stacks)` for each
  installed API upgrade. Because Rod Other_10 resets its own state first, apply hooks that modify `Rod.*` or the per-fish
  values recomputed there are idempotent. A persistent `obj_jamm_api` controller (created by `jamm_api_init`, placed like
  the console) calls `step_script(stacks)` every Begin Step for installed API upgrades.
  Helper: `jamm_upgrade_stacks(key)` returns the stack count on the current rod.
* Saves: `slot:<rodkey>:<iy>` from 22b already store upgrade keys; ensure API ids round-trip by key and that upgrades
  of removed mods become empty slots. Unlock state `u:upgrade:<key>`.
* Girl/Gear loops over upgrades bounded by 17 (`gear_upgrade_scalar`): extend to `UpgradeData.nItems` (audit §7.1.5).
* Tests (synthetic) + real build with a test mod.

## 22d — Fish API + JAMM ModKit (`exp/modkit`)

### Fish API (runtime, add to the items runtime or a new `FishRuntime`)

`jamm_fish_register(key, object, size_tab, name, desc, x1, x2, y1, y2, count)`: if fewer than `count` instances of
`object` exist, create the missing ones at random positions inside the box (set `start_x/start_y/TRUE_START_X/Y`
as the base Create does), then `jamm_catalog_register(size_tab, object, name, desc)`. Safe to call again after a room
restart (counts existing instances). Spawning must happen before CatalogData counts populations (it counts with
`instance_number` and re-checks; verify order in the audit dumps and document it).

### ModKit package format

A folder with `jamm-mod.json` (+ PNGs and optional `.gml` hook files):

```json
{
  "id": "example-fish", "name": "Example Fish", "version": "1.0.0", "author": "JAMM", "description": "…",
  "fish":    [{ "key": "goldie", "name": "Goldie", "size": "small", "sprite": "art/goldie.png", "frames": 1,
                "value": 300, "xp": 60, "stamina": 40, "lungeSpeed": 8, "lungeRange": 60, "speed": 1.5,
                "spawn": { "x": [1200, 4000], "y": [300, 900], "count": 4 }, "night": false,
                "description": "…", "caughtDescription": "…",
                "hooks": { "create": "hooks/goldie_create.gml", "step": "hooks/goldie_step.gml", "caught": "hooks/goldie_caught.gml" } }],
  "boats":   [{ "key": "raft", "name": "Raft", "cost": 15000, "card": "art/raft_card.png", "sprite": "art/raft.png",
                "description": "…", "stats": { "maxSpeed": 4, "capacity": 4 } }],
  "hats":    [{ "key": "crown", "name": "Crown", "sprite": "art/crown.png", "value": 2500, "description": "…",
                "hooks": { "effect": "hooks/crown_effect.gml" } }],
  "upgrades":[{ "key": "turbo", "name": "Turbo Reel", "description": "…", "description2": "…", "cost": 4000,
                "icon": "art/turbo.png", "maxStacks": 3, "hooks": { "apply": "hooks/turbo_apply.gml", "step": null } }]
}
```

* Field names and defaults are documented in `docs/modding/modkit-reference.md`; validation errors name the file,
  the JSON path and what to do (non-developer friendly). Keys: `[a-z0-9_-]`, unique per type; asset names are
  namespaced `jmk_<id>_<key>…` so two packages never collide.
* Generator `src/Jamm.Core/ModKit/` (small classes: `ModKitPackage` (parse+validate), `ModKitBuilder`):
  loads the legacy base read-only, imports PNGs as sprites (reuse the texture-page helpers in `src/Jamm.Core/Builtins/`),
  creates objects: fish with parent `FishSmall|FishMedium|FishLarge|FishHuge` (Create = `event_inherited();` + variables
  from the JSON: BASE_VALUE, XP_VALUE, MAX_STAMINA, LUNGE_SPEED, LUNGE_RANGE_START/STOP, stayOutAtNight, random
  hspeed/vspeed, sprite; hooks appended / as extra events), boats with parent `Boat` (audit Boat1 Create for the stat
  variables), hats with parent `Gear` (effect hook in the event the base hats use for their effect, see e.g.
  Tophat/Sunhat events), and one script `jamm_init_<id>` with the register calls. Hook `.gml` files become scripts
  `jmk_<id>_<key>_<hook>`. Adds placeholder scripts for every `jamm_*` API function it calls (body `return -1;`) so the
  archive compiles standalone; JAMM's runtime replaces them in merged builds. Writes a new data.win (never the input).
  The output is an ordinary mod: `cgfmm diff/check/build` work with it unchanged.
* CLI: `cgfmm modkit new <dir> [--template fish|upgrade|boat|hat]` (scaffold with a placeholder PNG),
  `cgfmm modkit build <dir> [--base <data.win>] [--out <file.win>]` (base defaults to the configured base),
  `cgfmm modkit check <dir>` (validate only), and `cgfmm add <dir>` builds and adds a package folder.
* Examples in `examples/modkit/` with ORIGINAL art generated by `examples/modkit/make-art.py` (stdlib only: write PNGs
  with zlib; simple cute pixel shapes). Commit the generated PNGs (they are ours, not game art).
  * `example-fish`: two fish — "Goldie" (simple small fish, path-free swimming) and "Glowfin" (medium, night-only,
    with a `step` hook that makes it glow/pulse via image_blend and dart away when the lure is close).
  * `showcase-upgrades`: five upgrades whose effects must be idempotent (recomputed from reset state in Rod Other_10)
    — suggested: **Turbo Reel** (reel speed, `Rod.upgrades[1]`), **Deep Diver** (sink rate, `Rod.upgrades[0]`),
    **Iron Line** (tension, `Rod.upgrades[13]`), **Fish Magnet** (fish lunge more eagerly: `base_lunge_mod` of all Fish,
    as the base Attract does), **Golden Touch** (+25 % sale value per stack; choose a safe mechanism, e.g. a step hook
    that scales `value` of the hooked fish once per catch with a marker variable). Verify every variable you touch in the
    audit/dumps and explain the mechanism in the hook file header comment.
* Tests: package parsing/validation (good + each error), builder on a synthetic base (objects/scripts/sprites created,
  compiles, input untouched), CLI smoke. Opt-in real test: build both examples on `JAMM_ORIGINAL` and merge them with
  the base through ArchiveBuilder.
* Docs: `docs/modding/README.md` (index), `docs/modding/getting-started.md` (your first fish in 10 minutes),
  `docs/modding/modkit-reference.md`. (The runtime API reference pages are written in 22e.)
