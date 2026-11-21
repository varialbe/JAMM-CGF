# Perf without Smooth: Round 2 report

Date: 2026-11-21
Branch: `fix/perf-without-smooth`

## Finding

The large-pack regression came from installing the fish-neighbour grid in every Perf archive, even when the runtime INI switch said `FishGrid=0`. The grid rewrites and wraps broad fish movement and lifecycle paths. The disabled runtime flag stopped grid queries, but the injected hooks still ran to invalidate or maintain the grid state. With thousands of fish, these per-instance probes were costly. The Round 2 capture also showed `fish_grid=1`, which meant earlier comparisons had not actually disabled the grid.

The profiler evidence from the all-7 capture is consistent with that cause: `jamm_Script_jamm_fg_touch` took 31,928 µs/tick and `fish_disabled` took 17,124 µs/tick. `SceneItem_Step_2` took 45,659 µs/tick versus 14,957 in the pure build, `LightingSystem_Draw_0` took 21,292 versus 502, and `Fish_Step_2` took 99,677 versus 59,415. In the Perf+Smooth capture, `Fish_Step_2` returned to 59,235 µs/tick and the grid touch script was not a leading entry. Smooth deactivates instances between its 30 Hz logic ticks and culls off-screen draws, so fewer fish and scene-item events reach the grid hooks. Smooth was masking the added per-event work through its own scheduling and culling; it did not make the grid hooks free.

## Change

- The generated JAMM Perf builtin and recipe no longer include the grid, scheduler controller, or the independent Phase 6a event edits. A plain Perf selection therefore has no additional per-frame code.
- Fish scheduling is added at final profile build only when the profile opts into `Perf.StartEnabled`; the F8 control remains available with that scheduler.
- FishGrid is installed only when `Perf.FishGrid=true`. The default is off, because disabling a runtime grid switch does not remove the cost of its installed hooks.
- Standard archive output omits `obj_jamm_perf`; the profile builder adds it when a runtime Perf setting requires the controller. The builtin generator version was bumped so existing caches regenerate.
- The bench CSV reads runtime state from the instrumented archive and reports the actual values. Fresh plain-Perf captures report `fish_grid=0` and `perf_mode=0`.

## Performance

Round 2 values below are the serialized pre-fix measurements recorded in the task. Final values are fresh branch captures measured with `~/jamm-bench/run.sh` (30-second capture). The all-7 dock Perf result is the mean of two runs; the other pack/scene cells use one run each. Vanilla Perf output was byte-identical to the base. A repeat vanilla dock pair is shown to account for runner variance.

| Pack | Scene | Round 2 base (µs) | Round 2 + Perf (µs) | Final base (µs) | Final + Perf (µs) | Change vs base |
|---|---|---:|---:|---:|---:|---:|
| all-7 | dock | 62,900 | 89,600 | 62,272 | 62,421 | +0.2% |
| all-7 | deepwater | — | — | 62,703 | 63,478 | +1.2% |
| pub5 | dock | 85,700 | 81,300 | 87,774 | 87,947 | +0.2% |
| pub5 | deepwater | — | — | 84,405 | 83,642 | −0.9% |
| vanilla | dock | 20,960 | 22,390 | 21,322 | 21,074 | −1.2% |
| vanilla | deepwater | — | — | 20,443 | 20,488 | +0.2% |

Round 2's Perf+Smooth result was 63,200 µs/tick against the 62,900 all-7 base (+0.5%). The new plain Perf payload has no per-frame runtime code, so adding it beside Smooth cannot reintroduce the former grid or scheduling cost. Vanilla's final Perf archive SHA-256 matched the original base exactly (`dbb2e5c4…9286d2c1`); paired 30-second captures were within 1.2% dock and 0.2% deepwater. No game archives or extracted game assets are included in the repository.

## Weak-PC profile

The Weak-PC preset enables Perf scheduling at interval 8, disables HD-Lite, and leaves FishGrid off. It does not enable Smooth. On the exact seven-pack order, a profile-native pair measured 199,603 µs/tick dock and 197,143 µs/tick deepwater without Weak-PC; with Weak-PC it measured 156,919 and 152,812 µs/tick respectively. That is approximately 21% faster in both scenes. The comparison uses the same profile build path on both sides.

## Verification

- `dotnet build -c Release`: passed, zero warnings and errors.
- `dotnet test -c Release`: passed, 590 passed and 17 real-file tests skipped; GUI 130 passed and 1 skipped.
- Fresh plain Perf CSV headers: `fish_grid=0`, `perf_mode=0`.
- Weak-PC profile captures: `perf_mode=1 interval=8`, `fish_grid=0`; separate installed-grid runs read `FishGrid=0` and `FishGrid=1` from the runner's `jamm_perf.ini` and reported those actual values in CSV.
