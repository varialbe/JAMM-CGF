# Example mods: Smooth, HD / HD Lite, Perf and Console

The app provides **Built-in (JAMM)** mods after you configure the untouched legacy
2023 base. Toggle Perf, Smooth, HD Lite or Console on to generate it locally and
enable it in the active profile, then Check/Install or Play. Progress and Cancel
are available. No SDK or terminal is required. Archives are cached by base SHA-256
and generator version; game archives and textures are never distributed.

CLI users can use `cgfmm builtin list` and `cgfmm builtin add <perf|smooth|hd-lite|console>`.
Add the resulting `JAMM …` library entry to a profile using the usual profile commands.

## Developer ModMaker CLI

The commands below are for developers building standalone variants (including optional
upscaled HD pages). Install the [.NET 10 SDK and recursive source checkout](user-guide.md#build-from-source).
Run from the checkout with new output paths: ModMaker refuses existing outputs and
opens the input read-only. It calls the same shared generators as the app.

```sh
dotnet run --project tools/Jamm.ModMaker -c Release -- perf /path/to/original/data.win /path/to/mods/perf.win
dotnet run --project tools/Jamm.ModMaker -c Release -- smooth /path/to/original/data.win /path/to/mods/smooth.win
dotnet run --project tools/Jamm.ModMaker -c Release -- hd /path/to/original/data.win /path/to/mods/hd-lite.win
dotnet run --project tools/Jamm.ModMaker -c Release -- console /path/to/original/data.win /path/to/mods/console.win
```

- **Smooth** separates the rendering target (60 or 120 FPS) from the 30 ticks/s
  simulation. It can fall back under load; a target is not a guaranteed frame rate.
  **F7** cycles 60/120/off and saves the selection.
- **HD Lite** is the `hd` command **without** `--upscaled-pages`. It improves
  fullscreen rendering while retaining the original texture pages. **F11** toggles
  fullscreen with the HD controller.
- **Perf** applies targeted code optimizations and includes experimental fish
  scheduling. **F8** toggles that performance mode and saves it to `jamm_perf.ini`.
  The FishLarge avoidance grid has its own switch. Set `FishGrid=0` under `[Perf]`
  in the same file to disable it.
  Mode defaults to **off** for a fresh configuration; an existing saved choice
  remains in effect. The experimental mode measured slower in the tested scene.
- **Console** adds an optional in-game command console (`obj_jamm_console`).
  Press **F12** (or the backtick/grave key `` ` ``) to open or close the console
  overlay, use **Up/Down** for command history, and type `help` to list commands:
  - `unlockall`: unlocks every upgrade, rod, lure, boat, tool/gear, hat, HUD
    shop menu icon, and the nightfall flag, sets level to max, and marks all
    catalog fish (including mod fish) as caught (does not change money).
  - `unlockgear`: unlocks equipment, upgrades, hats, HUD icons, and nightfall only.
  - `maxlevel` / `level <n>`: sets level (clamped to `[0, max_level]`).
  - `catalog all` (alias `unlockfish`): marks every catalog fish as caught.
  - `money <n>` / `money +<n>`: sets or adds dollars (clamped to `0..1e15`; large amounts display compactly when the profile allows big money).
  - `clear`, `close` / `exit`: clears the console log or closes the overlay.
  Commands that change game state warn that the next autosave (or opening the
  Esc save menu) persists changes to your save file. Switching save slots
  automatically resets the persistent console unlock flag. There is no hotkey
  cheat outside the console.

Optional `--debug` adds diagnostic logging/controllers; it is useful for local
measurements and may affect performance. See the implementation reports for
[Perf/Smooth](reports/phase6a.md), [HD](reports/phase6b.md),
[experimental performance mode](reports/phase6c.md), and the
[local benchmark mode guide](benchmarking.md) (`jamm-modmaker bench`).

## HD with 2x texture pages

The helper needs Python 3, NumPy, Pillow, and a separately installed
`realesrgan-ncnn-vulkan` distribution with its `models` directory and the
`realesr-animevideov3` model. Its Vulkan runtime/GPU must work on your system.
These are extra requirements for generating textures, not for using JAMM.
Use separate directories for each source archive; exported game assets stay local.

```sh
dotnet run --project tools/Jamm.ModMaker -c Release -- export-pages /path/to/original/data.win /path/to/pages
python3 tools/Jamm.ModMaker/upscale_pages.py /path/to/pages /path/to/pages-2x /path/to/realesrgan-ncnn-vulkan
dotnet run --project tools/Jamm.ModMaker -c Release -- hd /path/to/original/data.win /path/to/mods/hd.win --upscaled-pages /path/to/pages-2x
```

The export includes `pages.json`; the helper upscales pages marked `Upscale`,
processing colour and alpha separately. ModMaker verifies saved texture-page
geometry. This example upscales the original game's pages; it does not automatically
upscale new textures added by content mods. To process a merged archive, export
and build from that same archive into a separate output, then verify it; do not
import a mod based on a merged archive as if it targeted the untouched base.
Do not enable HD and HD Lite together; select one variant.

## Observed performance and memory

These are **orchestrator measurements supplied with the Phase 10 task**, not new
benchmarks performed by this documentation change. Hardware, scene, save and
runner affect the result; there is no universal FPS guarantee.

| Configuration (fullscreen) | Observed result |
| --- | --- |
| Base + HD + Perf + Smooth | 112–127 FPS at 30 simulation ticks/s |
| All six mods (Abyss, Cave, Colour, HD, Perf, Smooth), 2x textures | Exceeded 8 GB VRAM |
| All six with HD Lite instead | 47–52 FPS, about 4.5 GB VRAM |

Use **HD Lite** when texture memory is limited. Keep the screen unlocked and the
game visible: screen lock can throttle rendering to 1 FPS.

The [Phase 6c measured follow-up](reports/phase6c.md#follow-up-2-measured)
records experimental Mode 1 at 47–49 ms per tick versus 41 ms in Mode 0 in the
same scene (80 seconds per mode). Its runtime checks cost more than the skipped
work saved, so it now defaults off. This does not imply all static Perf changes
are slower. Test your actual profile in game after verification, especially
[shared catalog-slot conflicts](conflicts.md).
