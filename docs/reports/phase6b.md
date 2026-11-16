# Phase 6b — HD textures and fullscreen

Implemented on `mods/6b-hd`; no commit, push, or git configuration changes.
`SmoothMod.cs`, `PerfMod.cs`, and upstream source were not edited.

## Changes and scaling

HD now requires every safe page when `--upscaled-pages` is supplied, checks both
pixel dimensions exactly, and checks arithmetic when doubling Source rectangles.
Missing/incorrect pages fail with the filename and corrective action. Protected
font/tile pages remain unchanged. Before publishing the output, the temporary
archive is reloaded and checked against an original metadata/hash snapshot:
all page sizes, all TPAG Source/Target/Bounding fields, protected image hashes,
and font metadata, glyphs, and kerning. Existing outputs are refused. Errors show
stack traces only with `--verbose`.

The persistent HD controller defaults to `[HD] Fullscreen=1` in `jamm_hd.ini`.
F11 toggles fullscreen and saves the preference; `Fullscreen=0` starts windowed.
`AntiAliasing` still selects the best supported 2/4/8x MSAA level. Native back-buffer
rendering is always enabled by disabling the application surface.

The original 1200x720 port layout, world views, and camera are preserved, and the
runner's keep-aspect-ratio flag is explicitly enabled with black outside colour.
This delegates both display scaling and inverse room mouse mapping to the same
runner transform, preserving HUD `collision_point(mouse_x, mouse_y, ...)` input.
The GUI is fixed at a logical 1200x720, reapplied in End Step after room/window
changes. It deliberately does not use `display_set_gui_maximise`, which opts into
the full display including bars. Expected on 3440x1440: scale 2, content 2400x1440,
520-pixel bars left and right. Draw-event HUD, text, menus, and GUI should share
this content rectangle. Native resolution and MSAA remain available together.
See the [GUI size documentation](https://manual.gamemaker.io/lts/en/GameMaker_Language/GML_Reference/Cameras_And_Display/display_set_gui_size.htm).

Wide mode is not shipped: fixed-coordinate quest GUI and camera-relative world
HUD would need a complete per-object anchoring/input audit. A wider camera alone
cannot establish that every HUD element and hit target stays correct.

## Debug use

`hd-debug.win` deliberately starts windowed, captures after four seconds, switches
to fullscreen, captures four seconds later, then restores the configured mode.
F11 cancels the automatic sequence and saves the user's choice. `DebugWindowScale`
in `[HD]` controls the debug window size (clamped to 0.5–3; default 1).
Screenshots are `out/phase6b/jamm_hd_windowed.png` and `jamm_hd_fullscreen.png`;
`jamm_hd_sizes.txt` logs window/display/GUI sizes and all eight logical view/port
layouts once per captured mode. These runtime-relative paths resolve in the
GameMaker save sandbox, not necessarily this worktree. The INI also uses the
runner save sandbox. No game launch or install was performed here.

## Validation

- Restored the missing nested Underanalyzer dependency from the existing local
  checkout at pinned commit `4ff50a866b4c1a7acee8cebe6a56d6a48709b453`
  using `git archive`; no git config or upstream source edits.
- `dotnet build -c Release` was attempted; sandbox restrictions prevent MSBuild
  worker socket creation. `dotnet build -c Release -m:1 --disable-build-servers`
  and the equivalent ModMaker build both passed: **0 warnings, 0 errors**.
- Ran the HD command on the spec's exact legacy original, with
  `/home/varialbe/.local/share/jamm-tools/cgf-pages/pages2x`, producing
  `out/phase6b/hd.win` (192,742,598 bytes) and `hd-debug.win` (192,744,518 bytes).
  No source texture pages were copied into the worktree.
- Both outputs passed offline validation: 26 replaced pages, 34 total pages,
  1,264 TPAG items, 10 unchanged fonts. Safe page 33 is 1024x2048 originally,
  becoming 2048x4096; the other 25 replacements are 4096x4096.
- `cgfmm verify` passed for release (1,831 code entries) and debug (1,832):
  references valid and every code entry decompiles and compiles.
- Negative CLI checks passed for a synthetic 4097x4096 page (the old integer
  division bug), a missing page directory, and a missing option value. Each
  returns exit 1 with a clear error and creates no output archive.
- Original SHA-256 was identical before/after. `git diff --check` passed.
- `dotnet test -c Release --no-build -m:1 --disable-build-servers` was attempted;
  both test runners abort because the sandbox denies their TCP listener. This
  is not a passing test run. Logs and generated validation artifacts are under
  `out/phase6b/`.

## Memory and in-game risks

Measured RGBA8 texture residency: **536 MiB original → 1,760 MiB HD**, an increase
of **1,224 MiB**. If the runner retains decoded CPU copies too, combined texture
storage alone is roughly 3,520 MiB; GPU storage is not necessarily all mapped
into process address space. This excludes audio, other assets, driver allocations,
mipmaps, and render targets. At 3440x1440 one RGBA8 buffer is about 18.9 MiB;
an 8x colour MSAA target alone adds about 151.2 MiB before depth/resolve buffers.
A 4 GiB Large Address Aware Wine process may still exhaust or fragment its
address space. Offline checks cannot prove runtime residency or stability.

The orchestrator still needs to test GMS 1.4/Wine aspect correction with the
application surface disabled: inspect both screenshots, GUI quest panels, menus,
minimap/sonar, edge HUD buttons and their mouse hits, F11/persisted preference,
room changes, and a longer play session for allocation failures. The GUI/window
transform is implemented and compiles, but visual alignment and input correctness
have not been observed in the real runner on the 3440x1440 display.

## Follow-up

The orchestrator's standalone test found windowed 1200x720 correct and sharp,
but fullscreen stretched the scene to 3440x1440 (ellipse lure, edge-to-edge HUD,
no bars). This supersedes the runner-aspect-correction assumption above: with
`application_surface_enable(false)`, the Scale flag does not preserve aspect.
The supplied `jamm_hd_windowed.png` and `jamm_hd_fullscreen.png` are retained
unchanged in `out/phase6b/`; they show the pre-fix review, not this revision.

The controller now captures all eight original room ports at Room Start. At
Room Start, Begin Step and Pre Draw it computes `k=min(window_w/1200,window_h/720)`
and centred offsets, then transforms each original content port independently.
At 3440x1440 this gives `k=2`, `ox=520`, `oy=0`, with main port
`520,0 2400x1440`. Minimap/sonar retain their individual layout; camera/world
sizes remain unchanged. Recomputing from the baseline prevents cumulative scaling
and handles F11, room changes, alt-tab and window resizing before drawing.

Merely offsetting the content ports does **not** make their bounding box equal
the window. Unused views 6 and 7 therefore supply one-pixel ports at opposite
window corners when bars exist, pinning that box to the full window. The builder
rejects originals whose rooms already enable those views. Existing ordinary Draw,
Draw Begin and Draw End code exits for these anchor views; default sprite drawing
and room backgrounds are confined to their one-pixel ports. Their world cameras
are placed at -1000000,-1000000. They are hidden when there are no bars. This is an
explicit exception to transforming the original unused ports; actual game views
0–5 keep their own transformed ports. Runtime code enabling 6/7 would conflict
with this reservation and is unsupported.

Pre Draw clears the full display buffer to black every frame, before per-view
rendering. GUI End also paints the bars black, masking the anchor pixels and GUI
overflow. The application surface stays disabled and MSAA selection is unchanged.
`display_set_gui_maximise(k,k,ox,oy)` is present in the pinned BuiltinList for
bytecode 16 and compiles in both outputs. It follows `display_set_gui_size`, since
size overrides maximise. See the [GUI maximise reference](https://manual.gamemaker.io/monthly/en/GameMaker_Language/GML_Reference/Cameras_And_Display/display_set_gui_maximise.htm).

Room mouse coordinates still use the runner's native port mapping. Begin Step
checks physical window mouse position against the visible real ports 0–5 and
clears left/right/middle button state outside their union. This also excludes the
artificial corner pixels and avoids fallback room-coordinate clicks in the bars.
In-game confirmation of press/release and dragging across a bar is still required.
Debug capture logs now include `k/ox/oy` and all resulting ports.

Rebuilt `out/phase6b/hd.win` and `hd-debug.win`; the previous reviewed archives
are retained as `hd-before-followup.win` and `hd-debug-before-followup.win`.
Both pass texture/font invariants and `cgfmm verify` (1,835 code entries each,
all references valid and every entry decompiles and compiles). The solution and
ModMaker single-worker Release builds pass with **0 warnings, 0 errors**.
The exact `dotnet build -c Release` invocation still fails in this sandbox;
`-m:1 --disable-build-servers` succeeds. Tests were attempted again and abort
because the sandbox denies the test runners' TCP listener. The original SHA-256
matches the previous baseline. Follow-up logs are in `out/phase6b/followup-*.log`.
No commits, pushes, git configuration changes, or game installation were performed.

The fixed archives have not been run here. The orchestrator must confirm circular
lure geometry, centred GUI and HUD hits, minimap/sonar, room changes, F11/alt-tab,
black bars without smearing, and bar clicks/dragging in the standalone runner.
The old screenshots are evidence of the reported failure; fresh fixed-build
screenshots must come from that runtime review. Existing memory estimates apply.

## Follow-up 2

The orchestrator observed correct letterboxing but about 1 FPS, including in
windowed mode: `delta_time` was 1,001,040 us while GML work took about 20 ms.
Repeated port/GUI mutations triggered a runner rebuild each frame; the two dummy
views also tripled instance Draw dispatch. This revision supersedes the rendering,
MSAA, dummy-view, and bar-input approach in the earlier sections.

The application surface stays **enabled**. Room Start saves all eight original
ports and applies `k=min(window_width/1200,window_height/720)`. The surface is
resized to `round(1200*k)` by `round(720*k)` (minimum one pixel), and each original
port position and size is multiplied by k without offsets. World views/cameras
and view visibility are untouched. Begin Step reads window dimensions and exits
unless they changed; only then does it resize the surface, rewrite ports and call
`display_set_gui_size(1200,720)` once. If the surface is not available at Room
Start, the update is deferred until it exists. No Pre Draw renderer writes remain.
At 3440x1440, the expected surface is 2400x1440, with the runner supplying centred
520-pixel black bars and native mouse-coordinate mapping.

Removed dummy views, their Draw-code guards, manual bar drawing/clearing,
`display_set_gui_maximise`, mouse-button clearing, and the entire `display_reset`
MSAA selection path. The old `[HD] AntiAliasing` INI key is ignored. Texture
interpolation remains enabled. Window mutations occur only during initialization,
F11 actions, and the debug screenshot sequence, rather than continuously.
Reloaded both output archives and checked the serialized keep-aspect (`Scale`)
and interpolation flags plus black outside colour; all passed
(`followup2-options.log`). No existing game Draw events are rewritten.

Debug retains the windowed/fullscreen screenshots and now appends ten samples,
approximately one second apart, to `out/phase6b/jamm_hd_timing.txt` in the runner
save sandbox. Each sample includes k, actual surface dimensions, all eight ports
and visibility, and average `delta_time` in microseconds over that interval.
Timing stops after ten samples independently of the screenshot sequence.

Rebuilt `out/phase6b/hd.win` (192,744,646 bytes) and `hd-debug.win`
(192,747,462 bytes), preserving the reviewed builds as `*-before-followup2.win`.
Both passed all texture/font invariants (26 upscaled pages, 34 total pages,
1,264 TPAG items, 10 unchanged fonts) and `cgfmm verify`: **1,834 code entries**
each decompile and compile with valid references. Texture residency remains
1,760 MiB versus 536 MiB originally; application-surface storage is additional
(about 13.2 MiB RGBA8 at 2400x1440), while no MSAA target is requested.

Solution and ModMaker Release builds passed with **0 warnings, 0 errors** using
`-m:1 --disable-build-servers`. The exact `dotnet build -c Release` invocation
still fails under the sandbox's MSBuild restrictions. Tests were attempted and
aborted because the test platform cannot create its TCP listener; they did not
pass. Logs use the `out/phase6b/followup2-` prefix. `git diff --check` passed.

The input SHA-256 matches the previous follow-up baseline:
`dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1`.
This run used the file read-only, with no game installation or launch,
and made no commits, pushes, or git configuration changes. Runtime FPS recovery,
one Draw dispatch per enabled real view, GUI/HUD mouse hits, minimap/sonar,
room transitions, F11/resize/alt-tab and black bars still need in-game review.

## Follow-up 3

The reported 1880x1440 back-buffer screenshot does not establish that the
2400x1440 application surface is clipped. Debug now saves
`jamm_hd_surface_windowed.png` and `jamm_hd_surface_fullscreen.png` using
`surface_save(application_surface, ...)`, alongside the existing `screen_save`
images. All runtime captures and logs remain under `out/phase6b/` in the runner
save sandbox. Each capture logs actual surface, window, display and GUI sizes
plus k in `jamm_hd_sizes.txt`. The original delayed capture sequence and
ten timing samples remain.

Mode changes and surface resizes schedule a capture after the following frame
has drawn (a two-Draw-GUI-End countdown). Further resizes restart that countdown.
F11 and the automated mode switches schedule captures too, including restoration
of the configured mode. Files are overwritten with the latest capture for each
mode; the sizes log appends a record for each capture.

Begin Step continues checking window dimensions every frame, indefinitely after
fullscreen changes. End Step now checks again after F11, covering dimensions
that changed during Step. Both checks also compare actual surface dimensions
with the target, repairing a surface reset/recreation even when window dimensions
stay unchanged. Unchanged dimensions cause no renderer mutations. This addresses
the possible transition timing gap; a real clipping defect has not been
demonstrated without new runtime captures.

Rebuilt `hd.win` and `hd-debug.win`; the incoming archives are preserved as
`*-before-followup3.win`. Texture/font verification passes for both. Solution
and ModMaker Release builds with `-m:1 --disable-build-servers` pass with
**0 warnings, 0 errors**. The default parallel solution build again exits
unsuccessfully in this sandbox without compiler errors. Build/archive/check logs
use the `followup3-*` prefix under `out/phase6b/`.
No game installation, commits, pushes, or git configuration changes were made.

Runtime review is still required at 3440x1440: inspect the complete title in the
2400x1440 surface capture, compare it with the back-buffer image, and exercise
F11 and window resizing. The application-surface capture isolates surface
content; it does not prove final back-buffer presentation or GUI compositing.

Both final archives pass `cgfmm verify`: all 1,834 code entries decompile and
compile, and references are valid. The original's SHA-256 is unchanged from
`original-before.sha256`.
