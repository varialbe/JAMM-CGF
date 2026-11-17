# Phase 24 Report — `harbor-pack` ModKit Example (Boats + Hats + Shop Paging)

## Summary of Changes

### 1. Third ModKit Example Package (`examples/modkit/harbor-pack/`)
Added `examples/modkit/harbor-pack/` showcasing ModKit's boat and hat APIs alongside boat-shop paging:

* **Boats** (registered via `jamm_boat_register`; combined with the 8 vanilla boats, total boats = 10, causing `BoatMenu` to spill from page 1 of 8 cards onto **Page 2** via `MenuRuntime`):
  * **Paddle Raft** (`"harbor-pack:paddle-raft"`): `cost: 8000`, `unlocked: false`, `stats: { maxSpeed: 3.2, capacity: 3, maxDistance: 2800, moveBonus: 0 }`, `card: "art/paddle_raft_card.png"` (`100x40`), `sprite: "art/paddle_raft.png"` (`200x80`, drawn at `0.5x` in-world).
  * **Comet Cruiser** (`"harbor-pack:comet-cruiser"`): `cost: 950000`, `unlocked: false`, `stats: { maxSpeed: 9.5, capacity: 14, maxDistance: 28000, moveBonus: 6 }`, `card: "art/comet_cruiser_card.png"` (`120x48`), `sprite: "art/comet_cruiser.png"` (`240x96`, drawn at `0.5x` in-world).
* **Hats** (registered via `jamm_hat_register`, inheriting `Gear`, drawn at `0.25x`):
  * **Fisher's Beanie** (`"harbor-pack:fishers-beanie"`): `value: 400`, `weight: 10`, `rangeMin: 0.0`, `rangeMax: 0.25`, `offsetX: 14`, `offsetY: -42`, `sprite: "art/fishers_beanie.png"` (`96x76`). Cosmetic hat with no effect hook.
  * **Lucky Captain Hat** (`"harbor-pack:lucky-captain-hat"`): `value: 3000`, `weight: 4`, `rangeMin: 0.05`, `rangeMax: 0.75`, `offsetX: 14`, `offsetY: -42`, `sprite: "art/lucky_captain_hat.png"` (`104x80`), with `hooks.effect: "hooks/lucky_captain_effect.gml"` setting `value_scalar[0..3] = 1.15` (`+15%` sale value across all fish sizes, following `Gear_Other_15` / `Tophat_Other_15` / `Sunhat_Other_15` conventions).

### 2. Original Pixel Art Generator (`examples/modkit/make-art.py`)
* Extended `examples/modkit/make-art.py` (Python standard library only: `math`, `os`, `struct`, `zlib`) with `make_paddle_raft()`, `make_comet_cruiser()`, `make_fishers_beanie()`, and `make_lucky_captain_hat()`.
* Generated and saved all 6 RGBA PNGs under `examples/modkit/harbor-pack/art/` with dark outlines (`add_outline`) and shading consistent with `example-fish` and `showcase-upgrades`.

### 3. Tests & Documentation
* `tests/Jamm.Core.Tests/ModKitTests.cs`: Updated the opt-in `[RealFilesFact]` test (`RealBaseBuildsAndMergesAllExamplePackages`) to build `harbor-pack` alongside `example-fish` and `showcase-upgrades`, verify zero conflicts, merge all three into a single archive, and assert the generated `jmk_harbor_pack_*` objects, `jamm_init_harbor_pack()`, `jamm_boat_register`, `jamm_hat_register`, and `CardHandler` paging (`scroll_row`).
* `docs/modding/README.md`: Added `examples/modkit/harbor-pack/` to the repository examples list.
* `docs/modding/cookbook.md`: Added a dedicated `harbor-pack` walkthrough section under Recipe 7 pointing to `examples/modkit/harbor-pack/` and explaining boat-shop paging and the `lucky_captain_effect.gml` hook.

---

## Verification & Test Results

* `dotnet build -c Release`: **0 Warning(s), 0 Error(s)** across all projects.
* `dotnet test tests/Jamm.Core.Tests -c Release --no-build`: **337 passed, 0 failed, 9 skipped** (opt-in `[RealFilesFact]` tests skipped when env vars are unset).
* Opt-in real-base `ModKitTests` (`JAMM_ORIGINAL` + `JAMM_MODS_DIR` set, `--filter ModKitTests`): **8 passed, 0 failed, 0 skipped**, including `RealBaseBuildsAndMergesAllExamplePackages`.
* CLI verification against read-only `/home/varialbe/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win` using `--data-dir /home/varialbe/wt/modkit-out/data`:
  * `cgfmm modkit check examples/modkit/harbor-pack`: `ModKit package 'Harbor Pack' (harbor-pack v1.0.0) is valid: 0 fish, 2 boat(s), 2 hat(s), 0 upgrade(s).`
  * `cgfmm modkit build examples/modkit/harbor-pack --base ... --out /home/varialbe/wt/modkit-out/harbor-pack.win`: `Built and verified '/home/varialbe/wt/modkit-out/harbor-pack.win': 6 sprite(s), 4 object(s), 4 script(s).` (`1837` code entries verified).
  * `cgfmm build --base ... --order /home/varialbe/wt/modkit-out/harbor-pack.win,/home/varialbe/wt/modkit-out/example-fish.win,/home/varialbe/wt/modkit-out/showcase-upgrades.win --out /home/varialbe/wt/modkit-out/merged-all.win`: `Built and verified '/home/varialbe/wt/modkit-out/merged-all.win': 44,759,286 bytes.` (`1881` code entries, `199` catalog fish, `MenuRuntime` paging, `ItemsRuntime` with all 3 `jamm_init_*` scripts, and `FishRuntime` verified).

---

## Pending In-Game Desktop Verification
* Launch the game with `merged-all.win` (or a JAMM profile containing `Harbor Pack`, `Example Fish`, and `Showcase Upgrades`) and check:
  * Boat shop displays `Page 1/2` and `Page 2/2` with **Paddle Raft** (`$8,000`) and **Comet Cruiser** (`$950,000`) cards, and both boats handle/hold fish according to their stats.
  * **Fisher's Beanie** and **Lucky Captain Hat** appear in Hatalog / ocean drops, align properly on the cat's head, and **Lucky Captain Hat** grants `+15%` sale value when equipped.
