# Phase 17 — HUD flex column

Implemented on `feat/hud-layout`. No commits, pushes, git configuration changes,
submodule source edits or edits to `tools/Jamm.ModMaker/SmoothMod.cs`.

## Result

JAMM now injects a legacy-compatible runtime when building a CGF archive with the
Quest and ExperienceBar events. QuestEngine Draw Begin resets the layout on each
view draw pass; Draw End flushes queued text. Smooth's render-only frames therefore
start from the same anchor, independently of Begin Step and `quest_counter`.
Existing engine Draw Begin/End bodies are retained around these hooks.

The level label, bar sprite and fill stay in their original band. Above that band,
text is measured with its captured font and scale, placed bottom-to-top with a
four-pixel gap and minimum 16-pixel row height. Shadows include their two-pixel
extension. The anchor follows ExperienceBar's view-relative vertical offset; the
right edge is view-right minus 25. Lane order is extra level information (0), mod
lines (1), quests (2), then normal Draw invocation order within each lane.

In the verified all-mod build, the user should see the unchanged level bar, then
FPS, X, Y **bottom-to-top**, then quests above them. Quest completion, failure and
reward messages use the same layout. Their font, text, colour, alpha and conditions
are retained. Hidden rows take no space. This is expected behaviour from the
verified code and synthetic execution; the orchestrator still needs to check it
visually in game, including Smooth at its different frame rates.

## HUD audit

Read-only audit of all Draw-family code entries, including GUI, Begin, End and
Pre Draw, in the legacy 2023-08-16 base, every `~/Cat-Goes-Fishing-Mods/*.win`, and
the library copy of Oops! All Chaws!. Decompilation and intermediate comparisons
were kept under `/tmp/phase17audit`; no extracted game code/assets are included in
this repository. Compared every mod Draw event against the base, inspected all
text/sprite callers and their coordinate assignments, and checked controller
visibility, depth, level-bar room placement and relevant Create/Step events.

| Archive | Draw-family entries | Entries containing text/sprite calls |
| --- | ---: | ---: |
| Legacy base | 138 | 42 |
| jamm-hd-graphics.win | 140 | 43 |
| jamm-smooth-fps.win | 141 | 43 |
| the-abyss-beta.win | 138 | 42 |
| jamm-hd-lite.win | 140 | 43 |
| cave-expansion.win | 138 | 42 |
| colour-expansion.win | 139 | 42 |
| jamm-perf.win | 139 | 43 |
| Oops! All Chaws! library data.win | 138 | 42 |

These are **1,251 event bodies** in total. QuestEngine is visible and has no
existing Draw events in these inputs; Quest and ExperienceBar both have depth -22.
The only mod-added text in the target column is OAC's three lines.

Complete target-column/boundary call inventory (all archives unless noted):

| Event | Calls and location | Treatment |
| --- | --- | --- |
| `ExperienceBar_Draw_0` | One `draw_text` level label at bar x-10/y+10; one `draw_sprite_ext` for the bar at draw_x/draw_y. The fill also uses `draw_rectangle`. | Retain in level band, below the column. |
| `Quest_Draw_0` | Three mutually exclusive `draw_text_transformed` calls: reward, failure, ordinary label. Right edge view-right-25; old y=view-y+452 minus 20 per counter; scale 0.5. No sprite calls. | Adapt all three branches to quest lane; only the active branch reserves a row. |
| `ExperienceBar_Draw_0`, OAC only | Three `draw_text_shadowed` calls for FPS, Camera X, Camera Y; x=draw_x+430 and y=draw_y-60/-40/-20. No added sprite calls. | Adapt all three to mod lane. |
| `BreathBar_Draw_0` | O2 `draw_text` at draw_x-10/draw_y+10 and one `draw_sprite_ext` at the bar origin; fill rectangle. Visible when swimming. | Boundary band, retained below the column along with the level bar. |

Adjacent/possible HUD callers inspected and excluded from the automatic column:

* `BoatData_Draw_0`: three shadowed Boat/Fish/Value text calls near view-x+300,
  view-y+8 (Colour changes y to +9); top HUD, no sprite calls.
* `UpgradeData_Draw_0`: `draw_sprite_part_ext` attachment images and
  `draw_sprite_ext` upgrade icons; starts at view-x+10, view-bottom-35, bottom-left.
* `Message_Draw_0` / `MessageTwoSize_Draw_0`: one/two shadowed message calls at
  world x/y, with centred alignment. Their computed draw_x/draw_y are not the
  coordinates actually passed to text. No fixed right-hand column anchor.
* `Lightbulb_Draw_64`: one sprite and one timer text call, GUI-scaled from
  (239,435), timer y+25. Different GUI space, away from the right edge.
* `CatchQuest`, `CatchQuestNew`, `CatchQuestNewNew`, `LureQuest`, `RodQuest`,
  `HatQuest`, `UpgradeQuest`, `DistQuest`, `DepthQuest` Draw_64: update label and
  invoke inherited handling; no direct text/sprite calls. Shared Quest Draw_0
  supplies the actual label rendering.
* `Icons_Draw_0`: label text and label sprite at icon-relative coordinates;
  `Wallet_Draw_0`: wallet sprite and transformed amount, separate icon UI.
* `SonarPanel_Draw_0`: panel/noise sprites and radar sprites in view 3, including
  OAC's sonar changes. Its view-0 branch does not draw column text/sprites.
* HD/HDLite and Perf GUI toast text is at (14,50)/(14,54); Smooth toast at
  (14,14). Smooth's GUI End diagnostic path adds no text/sprite call. These are
  top-left GUI overlays, not column entries.
* Abyss/Colour credit changes, Cave fluorescence Draw Begin, Perf Magmer drawing,
  OAC Fish drawing and the remaining sprite callers are credits/world/lighting
  content, not view-relative right-hand HUD additions.

## Adapter and API

`HudTextAdapter` uses GML tokens, not replacements inside comments or strings.
It compares call multisets with the baseline, adapts only added supported text
calls with proven coordinates, and retains the original expression/branch at the
queue point. ExperienceBar aliases require standard assignments before the call
and reject unknown assignments/mutations. Explicit view-relative right-column
coordinates are also supported. Unknown aliases/functions, rotations, computed
scales, other areas and all sprites remain unchanged.

The real build reports exactly:

* 3 adapted text calls in `gml_Object_ExperienceBar_Draw_0` (OAC FPS/X/Y).
* 3 adapted text calls in `gml_Object_Quest_Draw_0` (base quest alternatives).

GM1.4 lacks font/alignment getters. Archive-wide forwarding wrappers track the
standard setters, including setters in scripts and GUI events, and let the flush
restore font, colour, alpha and alignment. The font wrapper's argument is registered
as a font asset in CGF semantic typing, so relocation and post-write verification
continue to validate its font identity. This instrumentation changes setter call
names outside the column but preserves their drawing effect; it does not move
those other HUD elements. Existing single-mod equivalence tests account for this
intentional transformation.

Mod-author API, lane rules, optional-owner semantics, examples and limitations are
in [hud-layout-api.md](../hud-layout-api.md). Queued rows call
`jamm_hud_reserve(height[, owner])` during the flush. Direct reservations are
immediate and require the caller to manage ordering relative to pending rows.
The API does not paginate excessive quests or shrink/wrap long text; enough rows
can extend above the view. Shader/native-extension and DnD font-state changes
are outside the audited automatic adapter.

## Verification

* `dotnet build -c Release --disable-build-servers -m:1`: passed, **0 warnings,
  0 errors**. Single-process MSBuild avoids sandbox restrictions on local pipes.
* Normal `dotnet test -c Release --no-build --disable-build-servers -m:1` was
  attempted with the HUD filter; VSTest aborted before test execution because
  the sandbox denies its TCP listener (`SocketException: Permission denied`).
* A temporary in-process reflection runner invoked the built Core/GUI xUnit
  Fact/Theory cases with their data rows and disposal: **367 passed, 0 failed,
  19 skipped**. This is a fallback, not a successful VSTest run. The skips include
  opt-in real-file/headless cases. HUD-specific subset: **4 passed**.
* Synthetic xUnit coverage: only added/proven calls adapt; unknown coordinates,
  strings/comments, unchanged calls and rotations stay intact; branches and
  expressions survive; explicit view-relative calls work; real synthetic archives
  compile and verify runtime scripts, unique event hooks, font identity and
  human-readable adaptation decisions.
* `node scripts/test-hud-runtime.mjs`: passed. Executes the shipped script bodies
  with mock GM builtins and only translates GML 2D indexing to JS. Checks measured
  multiline/scaled/shadow bounds and four-pixel gaps; lane order with quests
  enqueued before mod lines; captured font and restored state; 20 additional
  render passes without a logic tick; view isolation, moving camera, empty frames
  and low-level reservations. No game files or Node packages needed.

Real-file build used this order: Cave, Colour, HD graphics, HD lite, Perf, Smooth,
Abyss, OAC (alphabetical local filenames, then library OAC). Both HD variants were
included to exercise **all seven** local archives plus OAC; normal conflict/load
order selection remains in force. No manual conflict resolutions were required.

* Base: legacy `2023-08-16_manifest-9039707068725406600/data.win`;
  SHA-256 `dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1`.
* OAC library id/SHA-256:
  `d73e967c87fd0b6212e815840ef81545107b71ab430ea638c807c556d3d75bca`;
  Litterbox 30, Game Jolt build 2020716.
* `cgfmm build --base <legacy> --order <eight archives> --out
  /tmp/phase17-real/merged.win`: passed; 449 compiled entries,
  **2,182 semantically verified code entries**, 380 catalog fish.
* Standalone `cgfmm verify /tmp/phase17-real/merged.win`: passed;
  **2,182 entries decompile and compile; references valid**.
* Output: 274,533,206 bytes; SHA-256
  `6e7f9867e094e24257134c4b732fce35e631b877113a3cad9789dca2f28f7d7b`.
* All nine inputs were rehashed after building and still match the pre-build
  base/patch SHA-256 values. No input was written, installed or replaced.
* Verification reports six preserved source texture-geometry notices for PageItem
  1299, 1625, 2304, 2516, 2547 and 2689 extending beyond their pages. They do not
  prevent compilation/reference verification and require the orchestrator's
  in-game texture check. No claim of an in-game visual pass is made here.

Build, verification and fallback test logs are under `/tmp/phase17-real/`,
`/tmp/phase17runner/` and `/tmp/phase17-final-build.log`.
