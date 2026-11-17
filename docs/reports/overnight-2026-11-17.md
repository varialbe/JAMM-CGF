# Overnight 2026-11-17 — unlockall, modding API, ModKit, newcomer UX, packages

Orchestrated by Claude on the VPS (the PC was off). Code written by Gemini workers (agy:
Gemini 4 Hydrogen High/XHigh, Gemini 3.8 Flash High); Claude wrote the specs, reviewed every diff,
fixed integration bugs, verified on the real legacy base + real mods, and merged. Codex was not needed.
`main` was not touched. Everything is on **`experimental`** (28 commits ahead of `main` 296a224) and on
the `exp/*` sub-branches, all pushed.

## Branches (all merged into `experimental`, in this order)

| branch | what | verification |
|---|---|---|
| `exp/console-unlockall` | Phase 21: `unlockall` = equipment + max level + full catalog; new commands `unlockgear`, `maxlevel`, `level <n>`, `catalog all`/`unlockfish`, `money <n>`/`money +<n>`; fixes the unlock flag leaking into other save slots; GeneratorVersion 2 | tests; real build Pondweed + Colour + Console (the user's exact setup) built+verified; new code present in the merged archive |
| `exp/modding-api-audit` | game audit for the API (data arrays, shop layout, save keys incl. hard-coded loops, upgrades end-to-end, fish state machine) | read by Claude; basis of all specs |
| `exp/api-menus` | Phase 22a: shops (boat/rod/lure/tool: 2 rows) and upgrade menu (9 rows) **page** when they overflow — mouse wheel, Up/Down, ▲/▼ buttons, "Page x/y"; 18-upgrade limit lifted | real builds: Pondweed+Colour+Console, **Abyss+Cave+Colour** (9+ boats → paging active) |
| `exp/api-items` | Phase 22b: zero-conflict mods via `jamm_init_<mod>` scripts + generated `jamm_api_init`; `jamm_boat/rod/lure/tool/hat_register`, `jamm_item_find`; `[JAMM_Items]` name-keyed saves, equip by key, legacy-save safety | real build with a test mod (6 extra boats + hat) + user mods; without API mods the output is byte-identical in size to before |
| `exp/newcomer-ux` | Phase 23: first-run "Optional JAMM extras" step (Perf/Smooth/HD Lite/Console toggles), Hotkeys button/flyout, single source `BuiltinMods.Hotkeys` (GUI, `cgfmm builtin list`, README) | GUI tests; Xvfb screenshots of the real first-run flow with a fake HOME |
| `exp/modkit` | Phase 22d: **JAMM ModKit** — mods as folders (`jamm-mod.json` + PNG + GML hooks): `cgfmm modkit new/check/build`, `cgfmm add <folder>`; fish API `jamm_fish_register`; examples `example-fish` (Goldie, Glowfin) + `showcase-upgrades` (Turbo Reel, Deep Diver, Iron Line, Fish Magnet, Golden Touch) with original pixel art | both examples built from the real base and merged with Pondweed+Colour+Console, built+verified; newcomer flow `modkit new → check → add` run end to end |
| `exp/api-upgrades` | Phase 22c: `jamm_upgrade_register` (icon, price, max stacks, apply hook on Rod recompute, step hook via `obj_jamm_api`), `jamm_upgrade_stacks`, saved by key; Pondweed-safe (it already adds upgrade 18) | real build examples + user mods; merged archive checked: 5 real registrations, apply calls in `Rod_Other_10`, `obj_jamm_api` |
| `exp/modding-docs` | Phase 22e: `docs/modding/` (index, getting started, ModKit reference, GML API reference, saves, menus, cookbook, troubleshooting), README "Make your own mods", user guide, CLI docs | reviewed; spec-vs-code differences documented in `docs/reports/phase22e-docs.md` |
| `exp/ux-polish` | Phase 23b: window fits 1280 px screens, setup buttons in a fixed footer, no confusing "(not usable)" default, explained "modded copy" | Xvfb screenshot at 1280×720 |
| `exp/modkit-boats-hats` | Phase 24: third ModKit example `harbor-pack` — boats Paddle Raft + Comet Cruiser (10 boats in vanilla → boat shop page 2), hats Fisher's Beanie + Lucky Captain Hat (+15 % sale value via the Gear effect hook), original art | real build harbor + example-fish + showcase-upgrades + Pondweed + Colour + Console verified |
| `exp/gui-modkit` | Phase 25: GUI "Add mod folder (ModKit)…", drag & drop of package folders, ModKit badge with name/version/author, Rebuild action (keeps the mod id, so profiles stay valid) | 7 GUI + 4 Core tests (the Avalonia folder dialog itself was not driven under Xvfb) |
| `exp/packaging` | packages ship modder docs + ModKit examples; `package-windows.sh` builds the **Inno Setup installer** when `ISCC` is set (works under Wine) | see Packages |

Final state of `experimental`: `dotnet build -c Release` 0 warnings; Core tests 341 passed / 9 skipped
(opt-in real-file), GUI tests 124 passed / 1 skipped. Opt-in real-file run: see the end of this report.

## unlockall diagnosis (task 1)

Slot 8's empty catalog / "Lvl. 0" was a **fresh save slot**: the old `unlockall` deliberately did not touch level
or catalog ("no money/catalog changes"). Real bug found: the persistent console kept `unlocked_all = true` after a
slot switch and kept forcing unlocks (lure 5, day lock) in slots where unlockall was never typed. Now reset when
the SaveManager instance or `saveflag` changes. Level is set directly (no XP flood → no chain of level-up popups);
the game then grants level-gated unlocks itself. Catalog: every valid entry gets `count >= 1`, `life >= 0`
(only raised, never lowered), mod fish included; game-controlled hides (Torby, Magnalav) are left to the game.

## Integration bugs found and fixed by Claude during review

* Menu paging failed the **real** Pondweed build (Pondweed changes `iCard <= 17` to `<= 18`): anchors made tolerant,
  and paging can never fail a build any more (a menu a mod rewrote stays unpaged, with a build decision).
* Runtime order: ItemsRuntime owns `jamm_api_init`, FishRuntime runs last (avoids two init paths).
* Items runtime skipped (with a decision) for archives without SaveManager save events; `gear_equip` resolved at
  runtime so the load script always compiles; a fish-only standalone init no longer triggers the items injection.
* Console tests adapted to the decompiler (`false` → `0`).

## Packages (task E)

Built on the VPS from `experimental` with `Version=0.10.0-exp.20261117` into `~/JAMM-CGF/out/packages/`
(not committed): `.deb`, Windows portable zip, **Windows setup.exe** (Inno Setup 7 under Wine in a Docker
container), Docker image `jamm-cgf:0.10.0-exp`. Docs and examples are installed under `/usr/share/jamm-cgf/`
(first build used `/usr/share/doc`, which minimal Ubuntu images strip via dpkg excludes — caught by the smoke test, fixed).

Smoke tests:

| package | test | result |
|---|---|---|
| `.deb` | `apt install` in a clean `ubuntu:24.04` container; `cgfmm --help`; docs present; `modkit build` of both installed examples against the real base | pass (both "Built and verified") |
| Docker image | `modkit check` of the bundled showcase package; `cgfmm verify` on the real data.win | pass |
| Windows `setup.exe` | silent install (`/VERYSILENT /CURRENTUSER`) under Wine 10 (Debian trixie container); `cgfmm.exe --help`, `verify` real data.win, `modkit build` example-fish | pass (Wine 8 cannot start .NET 10 CoreCLR — a Wine limitation, not a package bug) |
| Windows zip | same payload as the installer | built |

The GUI executables were not exercised on Windows/Wine; the Linux GUI was exercised under Xvfb.

## Other checks

* `install.sh --get-game` (task 3): `bash -n` + shellcheck clean; fake-HOME tests for Flatpak (steamrt64),
  Snap, native `ubuntu12_32`, `debian-installation`, extra `libraryfolders.vdf` library — all found and SHA-verified;
  negative cases (no Steam, wrong hash) print the manual instructions; interactive run in a pseudo-TTY with a fake
  `steam` binary opens `steam://open/console` and the wait loop can be skipped with Enter.
* The game itself could **not** be run on the VPS: it exits without a running Steam client
  ("SteamAPI_InitSafe() failed"). I did not use a Steam emulator (that would bypass the game's Steam check).
  Wine + Xvfb in Docker (`~/wt/winebox`) works and is used for the Windows installer.

## Needs the user's decision / in-game testing before merging to main

1. **In-game test on the PC** (nothing GML-side ran in a real game tonight; everything is compile-, semantics- and
   build-verified only). Suggested 20-minute pass with Pondweed + Colour + Console + both ModKit examples:
   console `help`/`unlockall`/`level 30`/`catalog all`/`money +5000`, switch slots (unlocks must not leak);
   boat shop paging (harbor-pack alone gives 10 boats; or Abyss/Cave); upgrade menu with the 5 showcase upgrades (icons, stack limits,
   effects, Golden Touch value); catch Goldie (day, shallow) and Glowfin (night); save, reload, switch profile.
2. Merge order suggestion: `experimental` as a whole (sub-branches are already integrated and tested together).
3. Version number for a release (packages tonight use `0.10.0-exp.20261117`).
4. Whether `examples/modkit` art and the ModKit format are the public "v1" contract (docs call it that).
