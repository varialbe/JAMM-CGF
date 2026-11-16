# Phase 6c — switchable performance mode and 4x MSAA

Implemented on `mods/6c-perfmode`. No commits, pushes, git configuration changes,
input game writes, or upstream source edits. Generated archives, inspections,
regression harness and logs are confined to `out/phase6c/`.

## Controls and implementation

`perf <original.win> <new-output.win> [--debug]` retains all seven Phase 6a patches
and adds a persistent controller in NewWorld and room11. Its runner-sandbox
`jamm_perf.ini` settings are:

```ini
[Perf]
Mode=0
Interval=4
```

Mode defaults OFF (experimental; see Follow-up 2). F8 toggles and saves Mode, with a four-second startup toast and
2.5-second toggle toast. Interval is rounded and clamped to 1–60; 1 disables
skipping while retaining eligibility checks. The setting is read at startup.
Mode 0 bypasses the new fish scheduler and executes exactly the Phase 6a fish
body. It does not rewind changes already made while Mode 1 was running.
The controller and toast still exist in Mode 0.

The patch is a prefix to Fish End Step; the original body and the Phase 6a line
edits remain intact. It never deactivates an instance. The scheduling phase is
`(logic_tick + instance_id) mod Interval`, spreading work across ticks. Smooth's
extra render frames do not advance this controller's Begin Step or tick phase.

## Original eligibility and exceptions (superseded by Follow-up)

The generated allowlist contains only original FishSmall/FishMedium descendants
whose ancestry below Fish has no Step override and no visibility user12 override.
Only custom user events 1, 2, 3, 7 and 11 are accepted on species (these cover
non-paused state work); the audited FishMedium parent's standard flee handlers
are accepted. Custom ordinary-swimming user0 and other user events are excluded.
FishLarge/FishHuge families and their specials therefore do not qualify.
Dragon, Noctis, Torby, Ghost, Abyssal, Kingfish, Shaku, Bagu and Curby are also
explicitly rejected throughout ancestry. Newly added species are absent from the
compiled original allowlist. Cached eligibility is invalidated on object changes.

The 39 accepted object types, including two unplaced family objects, are:
Swooper, Roundfin, Mustardfish, Grumper, Queenfish, Sibilfin, Beakfish, Lumina,
Gawker, Ladyfish, Garump, Leofish, Angle, Sassy, Purdy, Hue, Sea_Jeepy, Casper,
Blissy, Wombler, Bo, Cavin, Yellowjack, Spinefish, Crystalfish, Jackfish, Glacier,
Bitterfish, Furrow, Phase, Tim, Underfin, Garm, Dijon, Cuda, Fin, Mudback,
FishSmall and FishMedium.

Each tick also rejects always_zoned, netherFish, deactivated, hollow, dead,
dragged, on-boat and thrall-controlled instances. Only state 12, or state 0 with
isVisible=0 and no active lungeCooldown, can skip. All hooked, netted, fleeing,
returning, departing, dead/respawning and other states run normally.

A candidate must be outside all these safety regions, checked again every tick:

- Lure: radius at least 3,000; CatSwim: at least 6,000. Both radii expand to
  absolute base lunge offset + absolute lure scale × largest absolute start/stop
  lunge range × max(1, absolute base lunge modifier) + 500 when larger.
- Every Boat: radius 3,000. Every Net: radius 6,000. Every Girl: horizontal
  distance 1,000, independent of depth. This additionally protects the original
  trawler test, which only catches within 500 horizontally of Girl.
- Every enabled camera rectangle, expanded by 3,000 in both axes.
- View 3's horizontal strip, expanded by 3,000, even when disabled. Its right
  edge uses the larger of view width/height because the original visibility
  script uses height in its horizontal test. No minimap/sonar camera is ignored.

If any condition fails, the full original body runs immediately, regardless of
phase. Thus visibility, night logic, lure proximity and trawler state resynchronize
on the first near tick. Original zoning remains owned by FishThinner; the patch
does not invent a state-12→0 transition when the game still considers a fish zoned.

## Mode 1 behavioral differences

For an eligible far fish, three of four End Step bodies are skipped by default.
On skipped state-12 ticks the original x/y positional clamp still runs. On
skipped paused-state-0 ticks, life is replenished as in the original, speed/spd
are zeroed and any active path's speed is set to zero. Standard user0 otherwise
restarts path thrust even after user12 pauses speed: intentionally suppressing
that unseen movement is part of Mode 1. Normal sampled/near user0 restores path
thrust; original user12 restores its saved speed/direction on becoming visible.
Engine motion already performed before End Step is not replayed or rolled back
for state 0.

Visibility updates, path thrust, medium-fish predator searches, lunge decisions,
night departure checks, cached lure distance, full_reset/direction updates and
trawler checks in the skipped body occur only on sampled ticks while far. Night
departure and off-screen predator responses can be delayed by up to Interval−1
logic ticks. Once departure/fleeing/return begins, those states are never throttled.
Far cached fields can be stale during the interval. Fewer random checks also
change the shared random-number sequence; bit-identical play is not promised.

`frames_since_last_flip` receives its End Step increment on every skipped tick;
Draw remains active and retains its own original increment. Native alarms are
never suppressed or replayed: Fish's alarm 0 can set state 2 on schedule, which
immediately disqualifies the fish. Positive state-0 lunge cooldowns disqualify it,
so their tick countdown is not lost. No accepted swimming handler has another
skipped countdown requiring catch-up. Respawn uses current_time in state 6,
which is never throttled. No catalog/live-count, death, save, or FishThinner code
is patched. Active instances remain visible to `with (Fish)`, including zoning,
kill_extras and light destruction. New scheduling fields do not enter the game's
explicit save-field lists.

## Offline census and expected savings

`perf-census <original.win>` prints the full structural allowlist, placements and
initial view rectangles. NewWorld contains **369 allowlisted placements**, of
which **189** lie beyond the initial view-3 right edge plus 3,000 (x > 7,600).
This is a placement-based upper bound for that starting-camera configuration,
not 189 proven runtime eligible instances. Creation scripts, save loading,
pausing, motion, CatSwim/Net positions and enabled cameras determine the actual
count. Original FishThinner disables zoning; Colour enables it. A large enabled
sonar/minimap view can conservatively eliminate eligibility.

For E continuously eligible instances, default scheduling avoids about 0.75E full
End Step bodies/tick, or 22.5E bodies/second at 30 logic ticks. For the 189 placement
candidates this is at most about 142 bodies/tick or 4,253/second, before runtime
exclusions. Proximity checks, alarms, Draw and native motion still cost time.
Those checks may outweigh savings for cheap zoned fish. No local game runtime
benchmark, 8 ms tick target, or 120 FPS success is claimed.

## Debug measurement and orchestrator checks

`perf --debug` logs Mode, Interval, eligible event count and skipped event count
approximately once per second. Separate `tick_ms_avg/max` samples span this
controller's Begin Step through its first GUI End for each logic tick. Extra
Smooth render frames are not sampled. Timing excludes work before this
controller, later GUI events, presentation/frame limiting and subsequent log I/O;
it is not a full runner frame measurement. F8 resets the measurement window.
Combine with Smooth-debug's existing tick/render/bypass measurements to assess
whether interpolated frames become possible. Debug log I/O itself has overhead.

The orchestrator must compare Mode 0/1 with the same save, location, gear and
camera setup, then repeat with debug disabled. Check F8 persistence/toasts,
Interval 1/4, Tab and F7; rapid lure/CatSwim/boat travel and camera/sonar changes;
zoning boundaries; day/night and nightfall Gear; hooked/netted/fleeing fish;
catalog/live counts, save/reload, respawn timing, and FishThinner culling/lights.
Check all named specials and mod creatures. The actual three-way merged mod set
must be playtested: if a mod adds custom behavior to an originally allowlisted
type, its inherited-event assumptions need re-auditing. Sequential Perf+Smooth
validation below is not a six-mod merge or an in-game test.

## HD and review fixes

HD's unsaved AntiAliasing preference now defaults to 4, retaining support for 8
and existing saved choices. Fullscreen, aspect, textures and all other behavior
are unchanged. At 3440×1440 one 4x RGBA8 MSAA color target is about 75.6 MiB,
versus 151.2 MiB for 8x; texture residency remains 1,760 MiB with supplied HD pages.

All output-taking commands reject absent/option-shaped positional outputs before
output path comparisons or loading. Decompiled helper text normalizes CRLF to
LF before matching. Both nested-if optimization patches assert that their guarded
braced statement has no following else; unexpected shape fails with an entry
name and advice to use the original legacy archive.

## Validation

Read-only input:
`/run/media/varialbe/Festplatte 2/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600/data.win`.
SHA-256 matches before/after. Final archives:

| File under out/phase6c | Bytes | cgfmm verify entries |
| --- | ---: | ---: |
| perf-mode.win | 44,577,670 | 1,831 |
| perf-mode-debug.win | 44,578,950 | 1,832 |
| perf-mode-smooth-debug.win | 44,630,022 | 1,843 |
| hd.win | 192,751,302 | 1,835 |
| hd-debug.win | 192,753,222 | 1,835 |

All five pass reload, references and every-entry decompile/recompile. Both HD
commands used `/home/varialbe/.local/share/jamm-tools/cgf-pages/pages2x` and passed
34-page/1,264-TPAG/10-font invariants, replacing 26 safe pages. Compiled inspections
confirm 4x default and the new scheduler. Removing the Mode 1 prefix yields the
same seven patched entries as the real Phase 6a follow-up archive, ignoring only
whitespace. The local guard harness passes four checks (including outer else,
CRLF and nested inner else). Fifteen CLI rejection checks cover missing output,
option-shaped output and existing files for all five output-taking commands.

Tool and whole-solution Release builds pass with **0 warnings, 0 errors**:

```sh
dotnet build tools/Jamm.ModMaker -c Release -m:1 --disable-build-servers -p:NuGetAudit=false
dotnet build -c Release -m:1 --disable-build-servers -p:NuGetAudit=false
```

The initial unmodified-audit invocation built but reported NU1900 warnings because
nuget.org is unreachable; disabling only network vulnerability auditing gives
the clean builds above. No project or dependency configuration was changed.
`dotnet test -c Release --no-build -m:1 --disable-build-servers` was attempted;
both VSTest runners abort because the sandbox denies their TCP listener. This is
not a passing test run. `git diff --check` passes. Earlier inspection archives
also remain in out/phase6c; use the five final filenames above for runtime review.

## Follow-up

The orchestrator found Mode 0/1 tick times identical within noise in the merged
Abyss+Cave+Colour+PerfMode+Smooth-debug game. The original compiled allowlist
omitted the content mods' roughly 3,000 added fish (roughly 6,700 total NewWorld
instances). The old census and savings estimates above describe that original
implementation only, not the new scheduler.

Runtime eligibility now uses a controller-owned `ds_map` keyed by `object_index`.
On the first encounter of each type, `object_get_parent` walks the actual merged
ancestry once. The chain must contain FishSmall or FishMedium, must not contain
FishLarge/FishHuge, and must not contain any explicitly excluded special named
above. Special names are compared at runtime throughout the chain. This admits
new mod species without baking their names or indices into the Perf archive.
The persistent controller destroys its map on destruction; duplicate controllers
do not own or destroy it. The historical `perf-census` is labeled as an offline
static audit and is no longer used to generate runtime eligibility.

GM1 provides no event-override reflection here. The per-instance proxy is
`jamm_perf_seen_type`, written only inside the patched Fish End Step in Mode 1.
Its first invocation for an instance/type always falls through to the complete
Phase 6a body; later invocations can schedule. Changing object type requires a
new warm-up. An End Step override that never calls Fish's inherited body never
gets this sentinel or this scheduler. Mode 0 bypasses all cache/sentinel code and
runs the exact existing Phase 6a body.

Proxy limits: an override using `event_inherited()` also reaches Fish's body and
can qualify on later ticks. Its surrounding override code still runs, but may
depend on Fish's body running every tick. Runtime ancestry and the sentinel do
not audit custom Step, user0, visibility or other user-event handlers. An
`instance_copy` may inherit the sentinel; dynamically changing parent chains
would leave the type cache stale. Thus this proxy is evidence of use of Fish's
body, not proof of no overrides or standard behavior. This expands the old
conservative event audit deliberately; content species with these behaviors
require orchestrator playtesting. No claim of universal mod safety is made.

All existing per-tick exclusions, state checks, lunge-expanded safety regions,
camera checks, positional clamp and tick-counter handling remain unchanged.
Debug eligible/skipped counts are event counts over approximately one second,
not unique fish counts. Their deadline now advances independently of GUI timing,
so counter logging continues even if GUI timing samples do not run. F8 resets
both measurement windows. At Interval 4, steadily eligible fish should skip
approximately three quarters of eligible event invocations after warm-up.

Follow-up outputs (use these instead of the earlier Perf archives):

- `out/phase6c/followup-perf.win`
- `out/phase6c/followup-perf-debug.win`
- `out/phase6c/followup-perf-smooth-debug.win`

All three pass `cgfmm verify` (reload, references, every-entry decompile/compile).
Tool and solution Release builds pass with zero warnings/errors using the same
`-p:NuGetAudit=false` offline audit setting documented above. Decompiled
comparison against the prior Perf archive confirms all seven Phase 6a patched
entries are unchanged after removing the Mode 1 prefix (whitespace normalized).
Input SHA-256 is unchanged. Logs, compiled inspection and hashes use the
`out/phase6c/followup-` prefix. No commits, pushes or git configuration changes.
The three archives are standalone/sequential builds, not a newly generated
content-mod merge. In-game eligible/skipped totals, behavior and tick-time
improvement remain for the orchestrator to measure with the same save/setup.

## Follow-up 2 (measured)

The orchestrator measured the merged Abyss+Cave+Colour+Perf-followup+Smooth-debug
build in the same scene for 80 seconds per mode: `tick_ms_avg` was **47–49 ms
in Mode 1**, versus **41 ms in Mode 0**. Runtime eligibility/proximity checks
cost more than the skipped fish bodies save; performance mode makes this setup
slower. The `perf` command now defaults to **Mode=0 (OFF)** when no Mode is saved.
Existing ini values, F8 persistence, Interval and all scheduling/Phase 6a behavior
remain intact. Startup/toggle toasts now explicitly say **experimental**.

The `jamm_perf` debug log file did not appear in the game save folder during the
run. Inspection found missing file output, rather than an incorrect path:
Perf only called `show_debug_message`. Debug builds now additionally append both
counter and timing messages to **`jamm_perf_log.txt`**, using a relative writable
runner-save-sandbox path as Smooth does, closing the file after each line.
Actual file creation/location still needs an in-game check; no runtime check is
claimed here. Normal builds have no added file logging.

Rebuilt `out/phase6c/perf.win`; `cgfmm verify` passes all **1,832** code entries
and references. The corrected `out/phase6c/followup2-perf-debug.win` also passes
(**1,833** entries). Tool and solution Release builds pass with zero warnings
and errors using the offline audit setting above. Original input SHA-256 is
unchanged; the previous generated perf archive is retained as
`out/phase6c/followup2-previous-perf.win`. Build/verify logs use the `followup2-`
prefix. No commits, pushes or git configuration changes.
