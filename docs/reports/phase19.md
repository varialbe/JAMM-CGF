# Phase 19 Report — Protect the HUD, Fix the F8 Toast, and Optional Console Mod (`unlockall`)

## Summary

Implemented all Phase 19 deliverables on branch `feat/hud-protect-console`:

1. **HUD Protection in `ModSanitizer` (`src/Jamm.Core/Patching/ModSanitizer.cs`, `src/Jamm.Core/Building/BuildPlan.cs`, `src/Jamm.Core/Building/ArchiveBuilder.cs`)**:
   - Added category `hud-style` (`ModSanitizer.HudStyle`) and bumped `ModSanitizer.Version` to `2` (`ModFactAnalyzer.Version` to `6`).
   - Defined and documented the evidence-based HUD/UI asset sets (`HudSprites` and `HudFonts`, plus `HudBackgrounds`).
   - Reverts third-party replacements of base HUD/UI sprites, fonts, and backgrounds to the base asset.
   - When a mod **appends frames** to a base HUD sprite (such as `spr_upgradeicons` in *Oops! All Chaws!* adding frame 19 for the Bulbguard upgrade), `ModSanitizer` verifies that kept mod code references the new frame index (or the corresponding `frameIndex - 1` upgrade index in `UpgradeData`/`UpgradeMenu`/`UpgradeCard`), reverts the base frames (`0..baseFrameCount-1`) and base sprite properties to the original HUD art, and keeps only the appended frame(s) from the mod. `ArchiveBuilder.TryApplyHybridSpriteFrames` preserves the base sprite's `TextureEntry` items for base frames and imports only the appended frame `TextureEntry` items from the mod archive.
   - Leaves newly added HUD sprites (`sprites.Added`, e.g., `spr_boat_icon8`, `spr_map`) untouched.
   - Leaves JAMM's own `obj_jamm_*` mods (`Smooth`, `HD` / `HD Lite`, `Perf`, `Console`) untouched (`IsJammMod = true`).
   - Added `KeepAuthorHudDesign` to `ManagedMod`, `ModLibrary.SetKeepAuthorHudDesign(query, keep)`, and the per-mod Advanced switch `"Keep the author's HUD design"` in the GUI (`ModListRow.axaml`, `MainWindow.axaml`, `ModRow.cs`, `MainViewModel.cs`, `BrowseViewModel.cs`) alongside `"Keep the author's performance changes"`.
2. **Fixed the F8 Toast (`PerfMod` / `PerfMode.cs`) and Audited All JAMM Toasts/Overlays (`SmoothMod.cs`, `HdMod.cs`, `ConsoleMod.cs`, `HudRuntime.cs`)**:
   - **Root Cause**: During normal `Draw` events in `NewWorld`, `ExperienceBar_Draw_0`, `BreathBar_Draw_0`, `Quest_Draw_0`, and `Wallet_Draw_0` set `draw_set_halign(fa_right)` (and `Wallet_Draw_0` sets `draw_set_font(fnt_babyMoney)`). `PerfMode`'s `DrawGUI` event drew a black rectangle at `x = 8..490, y = 48..80` and then called `draw_text(14, 54, ...)` without setting `draw_set_font`, `draw_set_halign(fa_left)`, or `draw_set_valign(fa_top)`. With `fa_right` still active from the `Draw` phase, the text was drawn to the left of `x = 14` (off the left edge of the screen) and/or with `fnt_babyMoney` (which only has digit/currency glyphs), leaving only a black block visible on screen.
   - **Fix**: Updated `PerfMode.cs` (as well as `SmoothMod.cs`, `HdMod.cs`, and `ConsoleMod.cs`) so every `DrawGUI` toast/overlay explicitly sets `draw_set_font(fnt_babySmall)` (or `-1` on synthetic test models without `fnt_babySmall`), `draw_set_halign(fa_left)`, `draw_set_valign(fa_top)`, `draw_set_alpha(...)`, and `draw_set_color(...)`, and sizes its background box dynamically via `string_width`/`string_height`.
3. **Optional `console` Mod (`tools/Jamm.ModMaker/ConsoleMod.cs`, `Program.cs`, `docs/example-mods.md`)**:
   - Added `jamm-modmaker console <original.win> <out.win>`, creating persistent controller `obj_jamm_console` (recognized by `ModSanitizer.IsJammMod` and never sanitized).
   - Opens/closes with **F12** (or backtick/grave key `192`, stripping any `` ` ``/`~` characters from `keyboard_string`) and closes on **Esc** or `close`/`exit`.
   - Provides a single-line text input (`keyboard_string`), **Up/Down** command history (`ds_list`), `help`, `clear`, `close`, and `unlockall`. While open, consumes gameplay hotkeys in `BeginStep` (`keyboard_clear`) and locks `Rod.swing_locked` so typing commands like `help` or `unlockall` never triggers lure purchases (`B`/`H`/`L`/`R`), selling (`S`), swimming (`Q`), or boat movement (`A`/`D`).
   - `unlockall` unlocks every upgrade (`UpgradeData.unlocks`), rod (`RodData.unlocked` + `Girl.bestRod`), lure (`LureData.unlocked`, including keeping Trawler `unlocked[5] = 1` in `EndStep`), boat (`BoatData.unlocked` + `Girl.bestBoat`), tool/gear (`ToolData.unlocked` + `active = 1`), hat (`GearData.unlocked`), HUD shop/menu icon (`Icons.unlocked`), and nightfall flag (`Girl.nightfallUnlocked = 1`, `AmbianceController.lockDay = 0`). It does **not** add money and does **not** modify `CatalogData`, prints the exact counts unlocked, and warns that the next autosave (or opening the Esc save menu) persists the unlocks. There is no hotkey cheat outside the console.
4. **Solution & Tests (`JAMM-CGF.sln`, `ModSanitizerTests.cs`, `SmoothGeneratorTests.cs`, `Phase14HeadlessTests.cs`)**:
   - Added `tools/Jamm.ModMaker/Jamm.ModMaker.csproj` to `JAMM-CGF.sln`.
   - Both `dotnet build -c Release` and `dotnet build tools/Jamm.ModMaker -c Release` pass with **0 warnings, 0 errors**.

---

## Documented HUD/UI Asset Set (`ModSanitizer`)

### `HudSprites` (34 Base Sprites)

| UI Component / Objects | Base Sprite Names |
|---|---|
| **Wallet** (`Wallet`) | `spr_wallet` |
| **Sonar Panel & Radar** (`SonarPanel`, `SonarPanelAdvanced`, `RadarButton`) | `spr_sonarpanel`, `spr_sonarnoise`, `spr_sonarblip`, `spr_sonarhat`, `spr_lureblip`, `spr_radarbutton` |
| **Catalog & Hatalog UI** (`Catalog`, `Fishalog`, `Hatalog`, `CatalogTab`, `CatalogUp`, `CatalogDown`, `CatalogItem`, `HatalogItem`, `CatalogHandle`, `CatalogSurface`, `icon_Catalog`) | `spr_catalog_arrow`, `spr_catalog_bar`, `spr_catalog_btn`, `spr_catalog_itembg`, `spr_catalog_tab`, `spr_hatalog_itembg`, `spr_pixel` |
| **Shop/Save Cards, Menus & Mode Buttons** (`Card`, `RodCard`, `LureCard`, `BoatCard`, `ToolCard`, `SaveCard`, `GearCard`, `GearCardBlank`, `SCB*`, `Controls`, `GM_Classic`, `GM_Realism`) | `spr_rodcard`, `spr_lurecard`, `spr_rowcard`, `spr_blankcard`, `spr_savecard_btn`, `spr_lock`, `spr_tools`, `spr_noboat`, `spr_controls`, `spr_gm_classic`, `spr_gm_realism`, `spr_button_icons` |
| **Upgrade Menu & Emblems** (`UpgradeMenu`, `UpgradeSlotMenu`, `UpgradeCard`, `UpgradeSlot`, `EmblemMaster`) | `spr_upgradeicon_bg`, `spr_upgradeicons`, `spr_emblemholder`, `spr_emblems`, `spr_crankbait_emblem` |
| **Buttons & HUD Icons** (`Buttons`, `Icons`, `IconsTop`, `IconsBottom`, `IconsRight`, `icon_*`) | `spr_icon`, `spr_iconsmall`, `spr_icon_labels` |
| **Level / Experience / Breath Bars** (`ExperienceBar`, `BreathBar`) | `spr_xpbar` |
| **UI Data & Save Controller Placeholders** (`SaveData`, `ToolData`, `RodData`, `LureData`, `SaveManager`) | `spr_huddata`, `spr_roddata`, `spr_luredata`, `spr_floppy` |

### `HudFonts` (10 Base Fonts)

`fnt_babyBig`, `fnt_babyBigOld`, `fnt_babyMedium`, `fnt_babyMediumOld`, `fnt_babyMoney`, `fnt_babyQuest`, `fnt_babySmall`, `fnt_babySmallOld`, `fnt_babyTiny`, `fnt_babyVerySmall`.

---

## Real-File Verification: 7-Mod User Set + Console Mod

Verified against the legacy 2023 base (`/run/media/varialbe/Festplatte 2/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600/data.win`):

| Mod | `IsJammMod` | `RemovedPerformanceCount` | `RemovedHudCount` | Details |
|---|---|---:|---:|---|
| **Abyss** (`the-abyss-beta.win`) | `false` | 0 | **11** | Reverted all 11 purple HUD sprites (`spr_catalog_arrow`, `spr_catalog_btn`, `spr_catalog_tab`, `spr_icon`, `spr_iconsmall`, `spr_lurecard`, `spr_radarbutton`, `spr_rodcard`, `spr_savecard_btn`, `spr_sonarpanel`, `spr_wallet`) to base; kept added sprites (`spr_boat_icon8`, `spr_map`, etc.) |
| **Cave** (`cave-expansion.win`) | `false` | 0 | 0 | No HUD or performance hacks removed |
| **Colour** (`colour-expansion.win`) | `false` | **1** | 0 | Removed `[distance-cull]` `gml_Object_FishThinner_Step_0`: `do_zoning = 0; -> do_zoning = 1;` |
| **HDLite** (`jamm-hd-lite.win`) | `true` (`obj_jamm_hd`) | 0 | 0 | JAMM own mod; never sanitized |
| **Perf** (`jamm-perf.win`) | `true` (`obj_jamm_perf`) | 0 | 0 | JAMM own mod; never sanitized |
| **Smooth** (`jamm-smooth-fps.win`) | `true` (`obj_jamm_smooth`) | 0 | 0 | JAMM own mod; never sanitized |
| **Console** (`jamm-console.win`) | `true` (`obj_jamm_console`) | 0 | 0 | JAMM own mod; never sanitized |
| **Oops! All Chaws!** (`d73e967c….win`) | `false` | **22** | **1** | Removed 22 performance hacks; on `sprites/spr_upgradeicons`, reverted base frames `0..18` to original HUD art while keeping appended frame `19` (Bulbguard) |

### Verification Results

- `dotnet build -c Release`: **0 warnings, 0 errors**
- `dotnet build tools/Jamm.ModMaker -c Release`: **0 warnings, 0 errors**
- `dotnet test -c Release`:
  - `Jamm.Core.Tests`: **292 passed, 0 failed** (including `ModSanitizerTests`, `SmoothGeneratorTests`, and `RealSevenModSetSanitizesBuildsAndVerifiesWithContentIntactAndHacksRemoved` with `cgfmm verify`)
  - `Jamm.Gui.Tests`: **103 passed, 0 failed** (including `Phase14HeadlessTests`)
