# Resource optimisation audit

## Part A · Baseline

The baseline is the 2023-08-16 Windows legacy runner and `data.win` at `94c6f6a`. Heavy adds TGE, Antiquity and PWB in that order. All-7 adds Abyss, Colour, Verdant and Cave after those three. The merges used the same locally sanitised patches as the texture-repack verification, without built-in mods. The runner is 32-bit PE and the archive's GEN8 version is 1.0.0.9999, VM bytecode 16. The VPS has six logical CPUs, 11.7 GiB RAM, Wine 11.22 and Mesa llvmpipe. Source archives, generated archives, raw traces and captures stayed on the VPS.

### Atlas size and residency

The page budgets use width × height × 4 bytes over TXTR PNGs. Driver allocations may also include depth/stencil, copies, alignment and other overhead. The page total represents the whole atlas at RGBA8 using the PNG dimensions. The D3D9 trace below records the sizes the runner requests at the device.

| Build | Before repack pages | Before MiB | After pages | After MiB | After archive MiB |
| --- | ---: | ---: | ---: | ---: | ---: |
| Vanilla | 34 | 536.00 | 34 | 536.00 | 42.51 |
| Heavy | 196 | 1,816.53 | 67 | 1,048.00 | 71.36 |
| All-7 | 417 | 3,400.92 | 119 | 2,170.81 | 114.73 |

Vanilla is the original archive. All-7 uses the established page counts and pixel validation. Standard DEFLATE already reduced the repacked archive from 282.00 to 114.73 MiB. [Texture repack measurements](texture-repack.md#png-compression-follow-up)

Wine's `+d3d9` trace shows an `UpdateTexture` destination for every atlas page before the first title frame. The build-specific counts are 34, 67 and 119. The dimension multisets match the archive pages rounded up to the next power of two on each axis. The runner uses the full mip chain. The table sums the RGBA8 bytes implied by the texture creation calls. It excludes system-memory staging, depth/stencil, render targets and driver bookkeeping.

| Build | Level 0 MiB from D3D9 | All requested mip levels MiB |
| --- | ---: | ---: |
| Vanilla | 536.00 | 714.67 |
| Heavy | 1,052.00 | 1,402.67 |
| All-7 | 2,880.50 | 3,840.67 |

The largest all-7 PNG is 9650×4852. Wine D3D9 receives a 16384×8192 texture request, which implies 512.00 MiB at level 0 and 682.67 MiB with its mip chain. Upload is eager on this runner/backend. The API trace quantifies the allocation requests on llvmpipe. A hardware capture is needed to measure actual device residency.

The executable contains exact registered names for `draw_texture_flush`, `sprite_prefetch`, `sprite_flush`, `background_prefetch` and `background_flush`, including the sprite/background multi variants. The whole-page names `texture_prefetch` and `texture_flush` were absent from its string table. The three builds and seven input mods have zero calls to these APIs in their decompiled GML. The modern GameMaker manual describes a later page/group interface. [GameMaker texture API](https://manual.gamemaker.io/lts/en/GameMaker_Language/GML_Reference/Drawing/Textures/Textures.htm)

The region scan hashes decoded BGRA8 rows with their dimensions and compares physical page/rectangle coordinates. Heavy had zero cross-mod duplicate sprite images at distinct coordinates. In all-7, Antiquity's `spr_glowcrystalplant` and Cave's `spr_crystalplant` preserve two sprite names and share one 220×266 rectangle. The cross-mod count of distinct copies after repack is zero for valid rectangles. Five all-7 source pages are preserved for empty or out-of-range rectangles, whose original sampling geometry precludes hashing as independent images.

### Render targets

Every input mod and each of the three builds has eight `surface_create` call sites. The Draw sites recover a lost surface or, for TGE lighting, replace a differently sized surface.

| Object | Create event calls | Draw event calls | Allocation |
| --- | --- | --- | --- |
| LightingSystem | Main light at view 0 width × height. Mini light at 100×100. | Recreate both. TGE and both merges use current view 0 plus 4 px on each axis for the main light. | 1.465 MiB at 800×480 or 1.484 MiB at 804×484 and 0.038 MiB mini |
| Fishalog | 566×295 | Vanilla and the input mods use 570×274. Both merges use 566×295. | 0.637 MiB or 0.596 MiB per instance |
| Hatalog | 566×295 | 570×274 | 0.637 MiB or 0.596 MiB per instance |

The runner also owns the application surface. At the benchmark's 1200×720 it costs 3.296 MiB per RGBA8 allocation. The room records set view 0 to 800×480 in NewWorld and room11. With one lighting controller and no catalogues open, the combined colour budget is 4.799 MiB for vanilla and 4.818 MiB for the merges. Keeping one instance of each catalogue adds 1.274 MiB at their Create sizes, for 6.073 and 6.092 MiB respectively. These figures are colour bytes per GML surface. The D3D9 trace shows additional render target and depth/stencil creation at the light surface sizes, so actual backing allocation is larger. The optional JAMM HD mod, outside these three builds, rescales the application surface using the smaller of window width / 1200 and window height / 720. At a 2× effective scale, 2400×1440 is 13.184 MiB. Upscaled HD atlas pages are a separate 4× cost per replaced page.

### RAM and embedded audio

The staged audit copy used the same benchmark controller setup for all three archives. It held the loaded title for 12 seconds, then applied seed 1337 and the dock fixture. After 63 seconds in dock it moved to the deepwater fixture in the same `NewWorld` process. The gameplay fixtures used a 1200×720 application surface with sonar off. Host samples read `VmRSS`, `VmHWM`, `VmSwap` and thread count from the Wine game PID every 0.5 seconds. The title sample is 2 seconds after the first rendered title frame. The other samples are the first at or after 60 seconds from their in-game markers.

| Build | Menu RSS / HWM MiB | Dock +60 s RSS / HWM MiB | Deepwater +60 s RSS / HWM MiB |
| --- | ---: | ---: | ---: |
| Vanilla | 1,169.3 / 1,169.3 | 1,182.9 / 1,182.9 | 1,183.7 / 1,183.7 |
| Heavy | 1,942.3 / 1,942.3 | 1,960.1 / 1,960.1 | 1,960.8 / 1,960.8 |
| All-7 | 4,436.0 / 4,436.0 | 4,458.9 / 4,458.9 | 4,459.8 / 4,459.8 |

All checkpoints recorded 19 threads and zero game-process swap. The all-7 main thread used 59.82 and 59.70 CPU seconds in the two gameplay windows. It accounted for about 66% of the sampled process CPU time, with Wine graphics and llvmpipe workers using much of the balance. These are software-renderer runs. The Wine process includes its texture allocations and CPU-side backing. The matching page upload counts remained flat from title through deepwater.

The three runs all emitted their end-of-deepwater marker. The vanilla launcher copy reported a shell parse error in teardown after the samples were written. The game PID had exited and the trace was complete. The temporary audit tools and build logs are under `~/jamm-night/resource-audit/`. Runtime logs, sampled memory, screenshots and the all-7 thread counters are under `~/jamm-night/runs/resource-audit-{vanilla,heavy,all7}-matrix/`.

| Build | AUDO PCM WAV entries / MiB | AUDO Vorbis entries / MiB | AUDO chunk MiB | SOND entries | STRG entries / chunk MiB | CODE entries / chunk MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Vanilla | 37 / 13.19 | 3 / 0.038 | 13.23 | 44 | 6,375 / 0.207 | 1,828 / 0.689 |
| Heavy | 38 / 13.23 | 14 / 2.108 | 15.34 | 53 | 8,734 / 0.269 | 2,273 / 0.937 |
| All-7 | 38 / 13.23 | 14 / 2.108 | 15.34 | 53 | 10,192 / 0.342 | 2,582 / 1.245 |

WAV header inspection found 16-bit mono PCM at 44.1 kHz in every WAV. The raw UTF-8 string payload is 159,339 / 203,481 / 266,524 bytes, and the root VM instruction streams total 678,356 / 927,932 / 1,243,260 bytes. Chunk sizes include records, pointers, padding and other format overhead.

| SOND flags | Vanilla | Heavy | All-7 | Payload |
| --- | ---: | ---: | ---: | --- |
| `0x65` | 37 | 38 | 38 | Embedded PCM in AUDO |
| `0x66` | 3 | 3 | 3 | Embedded compressed Ogg in AUDO |
| `0x67` | 0 | 8 | 8 | Compressed Ogg in AUDO with both low bits set |
| `0x64` | 4 | 4 | 4 | External Ogg music, audio ID −1 and no AUDO pointer |

Every SOND entry uses group 0 and AGRP is empty. The legacy file stores the group field for these regular entries. UTML's in-memory `Preload` is set by its reader on that path, so it cannot establish startup decoding. The runner logged startup decode of all eight `0x67` Oggs on the repacked all-7 build. The Vorbis granule positions and channel counts give 41.17 MiB of 16-bit PCM equivalent for those eight, including 39.04 MiB for the 213-second cave ambience. This is a PCM-equivalent estimate. It does not measure the mixer or its memory layout. The three `0x66` sounds produce no startup decode messages.

The cross-input SHA-256 scan found 37 vanilla payloads common to all seven mods. The other three vanilla Oggs appear unchanged in Abyss and Cave. TGE, Antiquity, PWB, Colour and Verdant share a different encoded version of those same three sound names. TGE and PWB share the 47,866-byte mod-only WAV under the same sound name. TGE contributes eight additional Oggs. Neither merged archive contains two byte-identical AUDO payloads. Both contain three unreferenced original Ogg records totalling 39,646 bytes after the named sounds switched to the shared alternate encodes.

### Asset reference screening

The static screen covered all decompiled entries, known numeric arguments and typed catalog/array fields, room placements and tiles, object parent/sprite/mask/collision pointers, and names in literal strings. All 33 `asset_get_index` calls in each merge take literals, and those names count as referenced. A runtime-computed name or asset ID, saved numeric IDs, or a table the legacy type map cannot classify may still refer to the remaining entries. Removing table entries would also shift surviving IDs. No entry in this screen was certified safe to strip.

| Build | Sprites entries / SPRT MiB / exclusive pixels MiB | Backgrounds entries / BGND bytes / exclusive pixels MiB | Objects entries / OBJT KiB | Sounds entries / SOND bytes / exclusive AUDO MiB |
| --- | ---: | ---: | ---: | ---: |
| Vanilla | 38 / 0.575 / 26.53 | 2 / 52 / 19.94 | 32 / 8.80 | 2 / 80 / 0.723 |
| Heavy | 103 / 2.510 / 127.98 | 2 / 52 / 19.94 | 58 / 16.24 | 8 / 332 / 0.879 |
| All-7 | 103 / 2.899 / 138.15 | 2 / 52 / 19.94 | 84 / 22.79 | 8 / 332 / 0.879 |

The table gives upper bounds for entries with no resolved incoming reference. Record spans include owned data such as masks or event records and their per-record pointer. Exclusive pixels count unique rectangles with no texture owner outside the screen. Most are on shared pages, so page memory can only be estimated by repacking and checking which pages disappear. The two original mountain background regions occupy 20.00 MiB of dedicated PNG pages in both merges. Rounded to the D3D9 allocations, those two pages use 24.00 MiB. The eager uploads make the dedicated pages a real startup cost. Preserving names and ID positions would still require a separate stripping or placeholder policy. The sound total excludes the three orphan AUDO records listed above.

### Build threads and native offload

A cold Release build used two analysis workers, disabled the base-analysis cache, used one ImageMagick thread, and consumed the existing seven sanitised patches. `AnalysisPhase` wall times and progress markers separated the following work.

| Phase | Wall seconds |
| --- | ---: |
| SHA checks and load original, seven sources and working base | 7.0 |
| Decompile original and seven sources | 1.83 |
| Other source semantics, patch preparation, ordered plan and relocation | approximately 122 |
| Import and compile merged assets | 14.99 |
| Repack textures | 157.51 |
| In-memory semantic validation and acceptance | 24.55 |
| Write archive | 2.61 |
| Reload, decompile and validate written archive | 16.17 |
| End to end | 347.10 |

The planning and semantic estimates use elapsed progress markers, with overlapping load/decompile intervals moved into their own rows. The verify row includes its own reload and 0.17 seconds of decompilation. `/usr/bin/time -v` recorded 336.75 s user CPU, 21.11 s system CPU and 3,867.84 MiB peak RSS. These are single runs on a shared host.

A second probe timed the unmodified repack algorithm alone on the established unrepacked all-7 fixture.

| Repack work | Wall seconds | Work count |
| --- | ---: | --- |
| Region discovery | 31.77 | 412 decodes, 30.25 s of the discovery time |
| Skyline placement | 0.09 | 119 output pages including preserved and reused pages |
| Source decode during rendering | 71.70 | 723 reads across batches, 7,673 MiB decoded cumulatively |
| PNG encode during rendering | 33.71 | 92 newly rendered pages |
| Other render work | 2.33 | Batch setup and pixel copying |

Two render workers with perfect scheduling could save about 53 s of the isolated 140 s, or roughly 15% of the 347 s full build. The actual gain is bounded by memory bandwidth, same-batch work, more expensive sources and VPS contention. Keeping a complete decoded source atlas to avoid repeat decodes would cost up to another 3.3 GiB. The current batch cap is 128 MiB of raw destination pixels, with one oversized destination allowed in a batch of its own.

A separate cold patch-generation pass opened the same original and seven raw inputs. These totals exclude manager sanitisation and patch-file writing.

| Analysis workers | Load seconds | Fingerprint seconds | Decompile seconds | Diff seconds | Total seconds | Peak RSS MiB |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 7.40 | 96.31 | 2.59 | 19.61 | 126.28 | 2,835.18 |
| 2 | 5.40 | 101.20 | 2.28 | 20.42 | 129.68 | 1,889.80 |

The extra analysis worker reduced decompile time by 0.31 s in this pair. It provided no end-to-end win under shared load. Fingerprinting, which includes the serial texture snapshots, accounts for roughly three quarters of patch-generation time. The observed peak was lower on the second run, but the difference cannot establish a memory benefit from additional workers because GC timing and system load varied.

Patch creation already parallelises independent CODE fingerprints, root decompilation and field diffs within a mod. Merge decompilation reads mods one at a time and parallelises within each archive, using worker-local contexts after shared global-function setup. Parallelising whole mods as well would increase simultaneous archive, image and GML working sets and could oversubscribe existing workers. The measured peak on this shared VPS is a better reason to limit concurrency than its six logical CPUs alone. Compilation, ordered planning/relocation, writing and read-back verification all depend on ordered state.

GM 1.4 runs GML events on one thread. The runner exposes `external_define`, `external_call`, `buffer_create` and `buffer_get_address`. A native extension could take a read-only snapshot and run pure neighbour queries on worker threads. At roughly 4,000 fish, one input record with float x/y, ID and type (16 bytes) adds 62.5 KiB per tick, or 1.83 MiB/s at 30 ticks/s. The GML snapshot walk, packing, query results, copy-back and same-tick barrier matter more than that transfer bandwidth. Per-fish extension calls add thousands of crossings and prevent useful batching. A worker would need to preserve active-set order, equal-distance tie handling and in-tick state changes while avoiding calls into the runner or access to mutable runner state. A one-tick delayed result changes existing behaviour.

Windows and Wine/Proton would need a compatible 32-bit Windows DLL and ABI, plus tests under both runners. Linux-native and macOS would need separately built native libraries and legacy runner support. Modern macOS cannot launch a 32-bit native runner. A native crash or dangling buffer can terminate the game. The fish-grid report measures `fish_avoid_fish` at about 4–5% of the loop on VPS dock, and the matched 120-tick heavy trace did not show a CPU win for the grid. That share caps a complete removal of just that work at about a 1.05× loop speedup under those measurements, before copying or synchronisation. No native prototype was justified. Measurements are in `docs/reports/fish-perf-grid.md` on `exp/fish-perf`.

### GPU capture plan for the player's PC

1. Verify the graphics backend. On Windows D3D9, use a short apitrace frame capture and inspect `DrawPrimitive` / `DrawIndexedPrimitive`, texture bindings, render-target changes and presentation. On Proton with DXVK, use `DXVK_HUD=devinfo,frametimes,drawcalls,memory` for a live overview and capture the Vulkan backend with RenderDoc for event and attachment inspection. Keep captures local. [apitrace usage](https://github.com/apitrace/apitrace/blob/master/docs/USAGE.markdown) · [DXVK HUD](https://github.com/doitsujin/dxvk#hud) · [RenderDoc](https://github.com/baldurk/renderdoc)
2. Compare vanilla, heavy and all-7. Match resolution, render scale, seed, dock/deepwater camera positions, save and VSync settings. Capture a cold menu, 60 s dock, 60 s deepwater, and repeat deepwater with sonar on to enable view 1. Compare all-7 repack on/off with the same input order. Repeat each timed capture three times and record median and p95 frame time. Take frame-time readings without the capture layer and use separate short API captures to inspect the frames.
3. Identify the main and mini light attachments by dimensions. Log visible light instances from GML in a separate capture. Count actual draw submissions and surface/texture/blend switches from light target bind through composite. The GML light loop invokes a sprite draw per visible light on the applicable view, and the runner decides which draws batch together. Compare call counts and GPU duration with the scene matched. A fall in binds without a fall in draws indicates a different bottleneck from per-light fill. Use GPU allocation/usage counters at the same checkpoints where the driver exposes them. Wine RSS on llvmpipe combines CPU heaps and software-renderer allocations.

### Largest images

The 30 largest asset-owned rectangles per build are ranked by decoded RGBA8 bytes. Frames are zero-based. Ownership matches asset name, frame and pixel hash against the inputs, then matches pixels for relocated names. Identical inherited frames are attributed to vanilla. Rectangles with multiple owners occupy a single row. All-7’s largest five rectangles alone account for 335.56 MiB.

| Rank | Image | Source pixels | MiB | Owner |
| ---: | --- | ---: | ---: | --- |
| 1 | `spr_great_lurker` #0 | 9650×4852 | 178.611 | Abyss |
| 2 | `spr_oculus` #0 | 6000×2366 | 54.153 | Abyss |
| 3 | `spr_overseer` #0 | 7473×1741 | 49.631 | Abyss |
| 4 | `spr_darken` #0 | 4000×2000 | 30.518 | Cave |
| 5 | `spr_zermon` #0 | 3000×1979 | 22.648 | Abyss |
| 6 | `spr_sky` #0 | 4773×941 | 17.133 | Abyss |
| 7 | `spr_ranaelder` #0 | 2048×2048 | 16.000 | Cave |
| 8 | `bg_mountains_bigger` | 2048×1776 | 13.875 | vanilla |
| 9 | `spr_BGL5` #0 | 3264×1082 | 13.472 | Abyss |
| 10 | `spr_greater_lurker` #0 | 2425×1360 | 12.581 | Abyss |
| 11 | `spr_balaenus` #0 | 3181×1027 | 12.462 | Abyss |
| 12 | `spr_cuddleshark` #0 | 2500×1250 | 11.921 | Abyss |
| 13 | `spr_andlit` #0 | 1999×1514 | 11.545 | Colour |
| 14 | `spr_cliff` #0 | 1656×1656 | 10.461 | vanilla |
| 15 | `spr_cliff` #1 | 1656×1656 | 10.461 | vanilla |
| 16 | `spr_spikulium` #0 | 2270×1170 | 10.131 | Abyss |
| 17 | `elderunderfin` #0 | 2100×1200 | 9.613 | Cave |
| 18 | `spr_boat8` #0 | 2100×1200 | 9.613 | Abyss |
| 19 | `spr_boat8` #1 | 2100×1200 | 9.613 | Abyss |
| 20 | `spr_krustallos` #0 | 1999×1241 | 9.463 | Colour |
| 21 | `spr_BGL3` #0 | 3295×743 | 9.339 | Abyss |
| 22 | `spr_boat_8_jamm_antiquity` #0 | 2000×1200 | 9.155 | Antiquity |
| 23 | `spr_boat_8_jamm_antiquity` #1 | 2000×1200 | 9.155 | Antiquity |
| 24 | `spr_boat_8_jamm_cave` #0 | 2000×1200 | 9.155 | Cave |
| 25 | `spr_echolight` #0 | 2000×1200 | 9.155 | TGE |
| 26 | `spr_BGL4` #0 | 3304×723 | 9.113 | Abyss |
| 27 | `spr_steelhead` #0 | 2647×876 | 8.845 | Abyss |
| 28 | `spr_shados` #0 | 2000×994 | 7.584 | Abyss |
| 29 | `spr_leviafin` #0 | 2663×745 | 7.568 | Abyss |
| 30 | `spr_hyperion` #0 | 2256×879 | 7.565 | Abyss |

<details>
<summary>Heavy top 30</summary>

| Rank | Image | Source pixels | MiB | Owner |
| ---: | --- | ---: | ---: | --- |
| 1 | `bg_mountains_bigger` | 2048×1776 | 13.875 | vanilla |
| 2 | `spr_cliff` #0 | 1656×1656 | 10.461 | vanilla |
| 3 | `spr_cliff` #1 | 1656×1656 | 10.461 | vanilla |
| 4 | `spr_boat_8_jamm_antiquity` #0 | 2000×1200 | 9.155 | Antiquity |
| 5 | `spr_boat_8_jamm_antiquity` #1 | 2000×1200 | 9.155 | Antiquity |
| 6 | `spr_echolight` #0 | 2000×1200 | 9.155 | TGE |
| 7 | `spr_dragon_tail` #0 | 1829×1040 | 7.256 | vanilla |
| 8 | `spr_maskfish_light` #0 | 1900×950 | 6.886 | vanilla |
| 9 | `spr_freshsea` #0 | 1025×1729 | 6.761 | TGE |
| 10 | `spr_freshsea` #1 | 1025×1729 | 6.761 | TGE |
| 11 | `spr_sea` #0 | 1025×1729 | 6.761 | vanilla |
| 12 | `spr_sea` #1 | 1025×1729 | 6.761 | vanilla |
| 13 | `sprite136` #0 | 1025×1729 | 6.761 | vanilla |
| 14 | `spr_maskfish` #0 | 1864×930 | 6.613 | vanilla |
| 15 | `spr_dragon_neck` #0 | 1752×961 | 6.423 | vanilla |
| 16 | `spr_HumpbackWhole` #0 | 2016×808 | 6.214 | TGE |
| 17 | `bg_mountains` | 2048×776 | 6.062 | vanilla |
| 18 | `spr_mavg` #0 | 2000×762 | 5.814 | Antiquity |
| 19 | `spr_boat_6` #1 | 1598×896 | 5.462 | vanilla |
| 20 | `spr_dragon_arm_big` #0 | 1223×1156 | 5.393 | vanilla |
| 21 | `spr_camos` #0 | 1526×888 | 5.169 | Antiquity |
| 22 | `spr_maligna` #0 | 1523×872 | 5.066 | vanilla |
| 23 | `spr_cryovorefrozen` #1 | 1761×715 | 4.803 | TGE |
| 24 | `spr_cryovorefrozen` #0 | 1760×715 | 4.800 | TGE |
| 25 | `spr_dragon_whole` #0 | 1536×795 | 4.658 | vanilla |
| 26 | `spr_cryovorefrozen` #2 | 1760×644 | 4.324 | TGE |
| 27 | `spr_fungfish` #0 | 1356×832 | 4.304 | Antiquity |
| 28 | `spr_boat_8` #0 | 1969×544 | 4.086 | TGE |
| 29 | `spr_cryovorefrozen` #3 | 1746×601 | 4.003 | TGE |
| 30 | `fnt_babyBigOld` | 1024×1024 | 4.000 | vanilla |

</details>

<details>
<summary>Vanilla top 30</summary>

| Rank | Image | Source pixels | MiB | Owner |
| ---: | --- | ---: | ---: | --- |
| 1 | `bg_mountains_bigger` | 2048×1776 | 13.875 | vanilla |
| 2 | `spr_cliff` #0 | 1656×1656 | 10.461 | vanilla |
| 3 | `spr_cliff` #1 | 1656×1656 | 10.461 | vanilla |
| 4 | `spr_dragon_tail` #0 | 1829×1040 | 7.256 | vanilla |
| 5 | `spr_maskfish_light` #0 | 1900×950 | 6.886 | vanilla |
| 6 | `spr_sea` #0 | 1025×1729 | 6.761 | vanilla |
| 7 | `spr_sea` #1 | 1025×1729 | 6.761 | vanilla |
| 8 | `sprite136` #0 | 1025×1729 | 6.761 | vanilla |
| 9 | `spr_maskfish` #0 | 1864×930 | 6.613 | vanilla |
| 10 | `spr_dragon_neck` #0 | 1752×961 | 6.423 | vanilla |
| 11 | `bg_mountains` | 2048×776 | 6.062 | vanilla |
| 12 | `spr_boat_6` #1 | 1598×896 | 5.462 | vanilla |
| 13 | `spr_dragon_arm_big` #0 | 1223×1156 | 5.393 | vanilla |
| 14 | `spr_maligna` #0 | 1523×872 | 5.066 | vanilla |
| 15 | `spr_dragon_whole` #0 | 1536×795 | 4.658 | vanilla |
| 16 | `fnt_babyBigOld` | 1024×1024 | 4.000 | vanilla |
| 17 | `spr_greatcusper` #0 | 1308×766 | 3.822 | vanilla |
| 18 | `bg_parallax` #0 | 2000×500 | 3.815 | vanilla |
| 19 | `bg_parallax_dark` #0 | 2000×500 | 3.815 | vanilla |
| 20 | `spr_bigrock` #0 | 983×1005 | 3.769 | vanilla |
| 21 | `spr_bigrock` #1 | 983×1005 | 3.769 | vanilla |
| 22 | `spr_silo` #0 | 1477×647 | 3.645 | vanilla |
| 23 | `spr_silo` #1 | 1477×647 | 3.645 | vanilla |
| 24 | `spr_silomask` #3 | 1477×644 | 3.628 | vanilla |
| 25 | `spr_abyssal` #0 | 1636×569 | 3.551 | vanilla |
| 26 | `spr_widerock` #0 | 996×883 | 3.355 | vanilla |
| 27 | `spr_widerock` #1 | 996×883 | 3.355 | vanilla |
| 28 | `spr_silomask` #4 | 1477×593 | 3.341 | vanilla |
| 29 | `spr_rocksig` #0 | 1066×817 | 3.322 | vanilla |
| 30 | `spr_rocksig` #1 | 1066×817 | 3.322 | vanilla |

</details>
