# Weak PC preset

`exp/weak-pc` started at `05a443b` on main. Texture repacking was
already included via `94c6f6a`, and the parallel PNG renderer from the
resource audit was also on main. This branch merged `exp/fish-perf` at
`7a437bd` in `13cacee`, retaining both rebuild signatures.

## Profile behavior

**Weak PC** enables JAMM Perf, defaults the fish grid and F8 mode on,
repackages textures, and turns off JAMM HD Lite. The same controls can be
changed separately in the Manager or from the CLI. Turning the preset off
restores prior profile settings and retains changes the player made while
it was active. The install signature includes the preset and Perf defaults.

The installer adds `[Perf] Mode=1` when the configured runner has a
recognizable save location and there is no saved Mode. An existing Mode
wins, including a choice made by pressing F8. The archive supplies the
same defaults on first launch if the prefix or save location was unavailable
at install time. An existing `Interval` or `FishGrid` in the INI also wins.
The first-run seed persists as a game preference across profile changes.
Players can change it with F8.

Scheduling only changes the full End Step rate for fish that pass Perf's
existing ancestry, state, interaction and camera checks. Near, hooked,
netted, fleeing and other excluded fish keep the existing update schedule.
Weak PC uses scheduling as its fish-thinning control. The vanilla
FishThinner limits, respawns and spawn tables are untouched. FishThinner
still reacts to rendered FPS, so active fish counts can vary between samples
even with matched saves and seeds. [Perf scheduling details](phase6c.md)

## VPS benchmark

The baseline on 2026-11-21 is the repacked seven-mod build without JAMM Perf
or HD Lite.
The Weak PC comparison uses the same ordered seven mod IDs in an isolated
manager store and applies the preset with `cgfmm profile preset seven weak-pc`
and `cgfmm apply --profile seven --yes`. Each benchmark runs the 2023-08-16
Windows legacy runner in Wine with llvmpipe on the shared VPS. The scene
fixtures use seed 1337, a 1200×720 window and application surface, and sonar
off. The event profiler compares a six-second warmup and 30-second capture.
The archives were prepared with `jamm-modmaker bench --profile`, then run
with `NOBENCH=1 ~/jamm-night/bin/run.sh <profiled.win> <label> <scene> 600`.
The verified Weak PC build was 120,314,070 bytes. Its repack retained five
original pages for empty or out-of-bounds rectangles and discarded 73
unowned texture regions. The guarded fish grid was installed successfully.

`Active fish` is the mean of `instance_number(Fish)` after each sampled
logic tick. `Fish End Step` is inclusive time in `gml_Object_Fish_Step_2`
divided by the number of profiled ticks. This captures the targeted GML
path and its callees. The FPS numbers include profiling overhead and the
VPS software renderer.

| Scene | Setting | Ticks | Mean active fish | Fish End Step ms/tick | Whole loop ms/tick | Mean FPS |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Dock | Default | 32 | 3,725.44 | 464.70 | 936.23 | 1.05 |
| Dock | Weak PC | 33 | 3,716.94 | 362.24 | 897.48 | 1.09 |
| Dock | Weak PC with `Interval=4` in the INI | 15 | 3,857.00 | 758.09 | 2,000.01 | 0.48 |
| Deepwater | Default | 30 | 3,742.10 | 471.03 | 1,003.35 | 0.98 |
| Deepwater | Weak PC | 30 | 3,741.47 | 367.82 | 983.44 | 1.00 |

The dock captured 75,465 skipped out of 86,311 eligible events at
interval 8 (87.4%) and 39,153 out of 52,274 at interval 4 (74.9%).
Those counters include warmup. Weak PC lowered profiled Fish End Step
time by 22.1% at the dock and 21.9% in deepwater versus the default
build. Mean active fish count changed by 0.23% and 0.02%, respectively.
The interval-4 control confirmed that a saved
`Interval` overrides the preset. Its overall FPS also differed
substantially, so the timings cannot isolate the effect of the
interval alone.

At interval 8, an eligible fish can defer part of its off-screen
movement or response for seven logic ticks, about 0.23 seconds at the
intended 30 ticks per second. The delay takes longer in real time if
the runner is below 30 FPS. Fish resume their usual updates as soon as
they fail the eligibility checks.

The Steam test client was refreshed between launches when needed. The runs
held the shared game lock and waited for the previous legacy game process
to exit. Both baseline and both Weak PC runs reported `RECORDED`. The
first Weak PC deepwater attempt exited after Steam initialized and left
no CSV or code-error report. The retry used the same build and settings.
CSVs and logs stay on the VPS in
`~/jamm-night/runs/weakpc-{off-,}{dock,deepwater}/`, with the build log in
`~/jamm-night/weak-pc/all7-rebuild.log`.

## Startup texture memory

Repacking was already the default for modded builds. The audit measured
417 original pages versus 119 repacked pages on all-7. The legacy runner
still uploaded all 119 before its first title marker. No Low-VRAM mode
is available. HD Lite exposes fullscreen and render sizing controls, and
there is no separate tested lighting-quality switch in HD Lite or Smooth.
[Resource audit](resource-optimization-audit.md)

The mipmap probe compares two copies of the same repacked all-7 archive
with the same instrumentation. On the second copy, the two interpolation
bits `GEN8.Interpolate` (0x8) and `OPTN.InterpolatePixels` (0x2) are
cleared together. The probe matches each Wine `+d3d9` `UpdateTexture`
destination to its `CreateTexture` dimensions and levels, as in the
resource audit. These are API allocation requests for RGBA atlas pages.
Both trace runs reached the end of the deepwater fixture.

| All-7 copy | Atlas uploads by the title | Level 0 MiB | Full chain MiB |
| --- | ---: | ---: | ---: |
| Original flags | 119 | 2,880.50 | 3,840.67 |
| Interpolation bits cleared | 119 | 2,880.50 | 3,840.67 |

Every page requested a full chain in both runs. The level count distribution
was identical (1 page with 10 levels, 93 with 12, 19 with 13, 5 with 14 and 1
with 15). The tested flags give no startup memory savings. The legacy TXTR
format has no per-page `GeneratedMips` field. The preset leaves the flags
at their original values.

The largest page still requests 16,384×8,192 pixels with its full mip
chain. Weak PC's scheduling starts after atlas upload. The all-7 pack can
still fail to reach the title on hardware that cannot allocate these textures.

The unreferenced-asset screen produced candidates but could not rule out
computed IDs, saved numeric references or later content paths. Deleting
entries also moves the surviving IDs. The two dedicated background pages
would remove roughly 24 MiB of D3D9 requests if they could be proven
unused. The three orphan audio records amount to roughly 39 KiB and
would save no atlas memory. Asset stripping is excluded from the preset.
[Asset reference screening](resource-optimization-audit.md#asset-reference-screening)

Player-side FPS and GPU-memory verification steps are in
[My PC is slow](../user-guide.md#my-pc-is-slow).

## Automated checks

The Release solution build reported zero warnings and zero errors.
The final Release test run passed 474 core tests and 128 GUI tests with
16 opt-in real-file tests skipped. Coverage includes preset round-trip
persistence, individual overrides, install-signature invalidation, INI
seeding and the headless Manager toggle.
