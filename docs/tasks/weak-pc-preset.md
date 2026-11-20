# Task: "Weak PC" preset (one click for iGPU laptops)

Target: Vel's laptop with integrated graphics (black screen / 2–3 FPS with the all-7 pack). Branch `exp/weak-pc` in `~/jamm-wt/weak-pc`,
from main; merge `exp/texture-repack` and `exp/fish-perf` if they are not on main yet (state in the report what was merged).
Report `docs/reports/weak-pc-preset.md`.

## Do
* Profile setting `preset: weak-pc` (CLI `cgfmm profile preset <profile> weak-pc|default`, GUI: one toggle/button "Weak PC" in the profile
  view with a one-line explanation of what it does and costs). Applying it sets, all visible and individually revertable:
  * Perf builtin enabled, with F8 mode pre-seeded ON (`jamm_perf.ini [Perf] Mode=1` written at install if the user has no own setting –
    never overwrite an existing user choice), fish grid on.
  * Texture repack on (and Low-VRAM option if one exists by then; if not, state that).
  * Sensible fish thinning: a capped fish population / more aggressive distant-fish scheduling using existing Perf knobs (or vanilla
    `FishThinner` parameters). Pick numbers from measurements, not guesses: fish count and fish CPU per tick for all-7 dock/deepwater with
    preset off vs on (profiler, VPS). Document what it costs (e.g. "~30 % fewer fish in distant zones", any visible effect).
  * Other cheap knobs that already exist (HD off, lighting quality if the HD/Smooth builtins have one) – only existing, tested switches.
* Installation signature includes the preset so changing it rebuilds.
* Docs: user docs section "My PC is slow" with the preset and what each part does; PC measurement plan (FPS, VRAM via the GPU tool of the OS)
  for the user to verify on real hardware.

## Tests
Unit tests for preset application/revert/persistence and that user overrides win; headless GUI test for the toggle; build with preset on all-7
and in-game `~/jamm-night/bin/run.sh <win> weakpc dock 600` + `deepwater` RECORDED (one game at a time; other agents share the VPS: check
`pgrep -f "Cat Goes Fishing.exe"` and wait), with before/after numbers.

## Rules
AGENTS.md. Zero warnings, all tests green. Commit on the branch, no push, no merge to main.

## Addendum (orchestrator, 2026-11-20 morning) – VRAM findings from the resource audit
docs/reports/resource-optimization-audit.md shows the legacy runner uploads **all** atlas pages before the title screen, and the D3D9 requests
for all-7 total **3,841 MiB** (power-of-two rounding + mipmaps) although the RGBA8 page estimate is 2,171 MiB. Investigate as part of the preset:
* Does the GM 1.4 runner create mip chains for every page, and can that be switched off safely from data.win (option flags in GEN8/OPTN, e.g. an
  interpolation/mipmap option) without changing how sprites look at 1x? Measure with the same D3D9 trace method as the audit; only include it in the
  preset if pixels at 1x stay identical (screenshot comparison) and RECORDED runs stay green.
* Unreferenced-asset stripping (audit candidate 3) as an opt-in part of the preset if it is safe and measurable.
