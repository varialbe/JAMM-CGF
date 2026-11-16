# Phase 6b — HD textures + real fullscreen (implementer: Gemini)

Read AGENTS.md first. Work only in `tools/Jamm.ModMaker/HdMod.cs`, `TexturePages.cs`,
`upscale_pages.py` and, if needed, a new command line in `Program.cs`. Do NOT touch `SmoothMod.cs`
or `PerfMod.cs` (Phase 6a runs in parallel on another branch). Do not commit, push or change git
config; the orchestrator commits after review.

## Background (measured by the orchestrator, do not re-derive)
- CGF is GameMaker 1.4 (bytecode 16). Window 1200x720; the main view is 800x480 world units shown in a
  1200x720 port (view follows `Camera`). Other views exist at runtime (minimap view 1, sonar view 3,
  view 2). Option flags include "Scale" (keep aspect ratio) and InterpolatePixels.
- `tools/Jamm.ModMaker` builds mods from the ORIGINAL data.win; GML is compiled with UndertaleModLib's
  `CompileGroup`. Not every GMS function exists for bytecode 16 (compile fails with "Failed to find
  function"); check `external/UndertaleModTool/UndertaleModLib/Compiler/BuiltinList.cs`.
- `HdMod.cs` today: `application_surface_enable(false)` (draw straight into the back buffer, so the
  scene renders at the real window resolution) and `display_reset(aa, false)` with the best supported
  MSAA level (the application surface supports neither). Verified in game: a 1800x1080 window renders
  natively, HUD/camera correct, smoothed fishing line.
- Texture pages: 34 pages of 2048x2048; 26 are safe to upscale (`TexturePages.Describe`: pages with
  font glyphs or tile backgrounds stay 1x, glyph/tile rectangles are in page pixels and also define
  the drawn size). `upscale_pages.py` already produced the 26 pages at 4096x4096 with Real-ESRGAN
  (`realesr-animevideov3`, colour and alpha separately): they are in
  `~/.local/share/jamm-tools/cgf-pages/pages2x/page_N.png` (game-derived: NEVER commit or copy into
  the repo). `HdMod.ReplacePages` swaps them in and doubles each item's Source rectangle while Target
  and Bounding sizes stay, so the runner samples 2x detail into the original on-screen size.
  The exe is Large Address Aware (4 GB under Wine), but page memory quadruples: check the total.

## Goal
1. **HD textures**: make `hd <original.win> <out.win> --upscaled-pages <dir>` produce a working mod
   with the 2x pages. Verify offline: page sizes, every TPAG item on an upscaled page has
   Source = 2 x original and unchanged Target/Bounding, fonts untouched, and estimate the GPU/RAM
   footprint. Reject pages with the wrong size with a clear message.
2. **Real fullscreen, everything scaled up** (user request: "fullscreen, but really so that
   everything is correspondingly bigger"):
   - start in fullscreen by default (setting in `jamm_hd.ini`, key to toggle, e.g. F11 or Alt+Enter);
   - the whole game (world, HUD, text, menus, GUI layer) scales with the screen; nothing stays at
     1200x720 size in a corner, nothing is cut off, mouse input stays correct (the game uses
     `mouse_x/mouse_y` in room coordinates and `collision_point` on HUD buttons);
   - the user's monitor is 3440x1440 (21:9) while the game is 5:3: implement the correct letterboxed
     scale (fill the height, black bars left/right) as the default, and optionally a "wide" mode that
     shows more world horizontally ONLY if you can show that every HUD element stays in place (HUD is
     drawn in Draw events relative to `view_xview/view_yview` and in Draw GUI at fixed coordinates;
     check every object that does that). If wide mode is not safe, do not ship it and say why.
   - Draw GUI must scale too (`display_set_gui_size` / `display_set_gui_maximise` as appropriate for
     GMS 1.4) so quest panels keep their place and size.
3. Debug aids for the orchestrator's in-game test: with `--debug`, save `screen_save` screenshots a
   few seconds after start in windowed and fullscreen mode, and log window/port/GUI sizes once.

## Rules
- Never write next to or over the original game files; outputs go to `out/phase6b/` (gitignored).
  Original: `/run/media/varialbe/Festplatte 2/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600/data.win` (read-only).
- `dotnet build -c Release` with 0 warnings; the produced data.win must pass `cgfmm verify`.
- Small readable classes, XML doc comments, WHY-comments for every engine quirk relied upon.
- Finish with a short report in `docs/reports/phase6b.md`: what changed, how scaling works, memory
  estimate, open risks for the in-game test.
