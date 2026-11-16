# Phase 6a — conservative Performance mod and adaptive Smooth

Implemented on `mods/6a-perf`. Only ModMaker sources and this report changed;
HdMod and pinned external sources are untouched. No commits, pushes, or git config changes.
All generated game files, decompiled inspection material, logs, and dependency workarounds
are under the gitignored `out/phase6a/`.

## Changes and equivalence

`perf <original.win> <new-output.win>` builds independently of Smooth. It patches four
existing code entries through decompile/edit/compile, with source-shape checks:

- Fish End Step: an outer state guard avoids the precise CaveRockBig query when
  its existing inner state predicate cannot accept a hit. In particular state 12
  fish no longer perform that query. Nested `if` deliberately avoids depending on
  GM1 boolean short-circuit settings. All other fish work still runs.
- `fish_check_trawler`: similarly guard the net collision query by its existing
  catchable states. Existence, distance, depth checks and catch transitions remain.
- `fish_avoid_fish`: hoist radius-squared and force numerator into script locals.
  Keep every candidate, engine traversal order, distance calculation, direction,
  and per-neighbor additions, including the original zero-distance behavior.
- SceneItem: cache the pure depth transform keyed by depth AND object_index;
  recompute on either change. Lazy initialization covers descendants overriding
  Create. Publish scene_depth and update both camera-relative positions every tick.

No intended gameplay differences: fish stay active, special creatures are not
throttled, and catalog/live counts, night transitions, respawn timers, zoning,
hooked/netted behavior and game-owned deactivation remain on their original paths.
New private `jamm_perf_*` variables are reserved implementation state.

Smooth defaults to 120 when no saved TargetFPS exists. Existing preferences and
F7's 60/120/off cycle remain. Each tick checks elapsed Begin Step→Pre Draw work plus
1.5 estimated render-frame intervals against the logic interval. When exhausted
(or off), it skips scans, map rebuilding, recording, culling, interpolation and
restoration, and schedules another logic tick. It retries each tick; entering the
bypass invalidates history so recovery starts with fresh positions. Particle
updates, input handling and tick-delta correction remain. The overloaded path is
constant work (including the existing 64 particle-system probes), rather than
instance-count-dependent work. It can forgo off-screen draw savings on those ticks.

The exact frozen-id wake path, draw-time cross-instance access and fish draw
side-effect restoration are unchanged on interpolated frames. No arbitrary subset
is held frozen during Draw; game-deactivated instances are never activated.

Debug emits `tick_ms_avg/max`, `render_ms_avg/max`, `render_n` and `bypass` once per
second alongside existing phase totals. Timings cover controller Begin Step through
GUI End, including freeze-list construction, excluding log I/O, frame limiting,
and engine/presentation work after GUI End. Zero render count means no samples,
not zero-cost rendering. Removed automatic screenshots that contaminated timings.

ModMaker also rejects missing output arguments and existing output files for mod
commands and gives concise I/O errors (`--verbose` retains details).

## Offline measurements and limits

Actual final compiled instruction counts (static size, NOT executed work):

| Entry | Original | Perf |
| --- | ---: | ---: |
| Fish End Step | 368 | 386 |
| fish_check_trawler | 99 | 129 |
| fish_avoid_fish | 56 | 58 |
| SceneItem End Step | 69 | 100 |

Guards increase static size but remove expensive engine calls on rejected states.
For D deep, non-deepfish instances outside states 0/1/9/5, remove D precise rock
queries per tick (30D/second at 30 ticks). For Z otherwise eligible uncatchable
fish near a deployed net, remove Z net queries per tick. Runtime D/Z cannot be
inferred from room placement. In the supplied merged scenario the rock family has
1,866 instances; this is potential collision candidate work, not a measured scan
count or a promise that the engine tests every rock.

Avoidance has 13 original caller entries, 16 call sites: object assets 242 and 41
and caller object_index; radii/forces include constants and instance variables.
For M non-self candidates and K in-radius neighbors per call, multiplications
change from M+2K to 2 for the hoisted expressions. Traversal remains O(M), or
O(N²) for same-family avoidance; this deliberately does NOT claim to solve the
quadratic traversal. A tick-snapshot spatial grid can miss neighbors moved by
preceding events, and changing traversal order changes floating-point force sums.
A safe exact dynamic index is deferred rather than silently altering movement.

Original NewWorld census includes at least 306 SceneItem descendants in its top
30 object groups: at stable depth this avoids at least 9,180 depth transforms per
second at 30 ticks, replacing them with cache checks. Instance motion and draw
cost remain. Cache overhead needs runtime comparison, especially if depths change.

Rejected a once-per-tick is_it_night cache: Gear position/state/nightfall and
AmbianceController minute/lockDay can change between event calls, and lockDay is
written conditionally. No proof establishes tick-wide identical results.
Rejected paused-fish throttling: user12 overrides, trawler detection, zoning and
night transitions need per-event correctness; state 12 alone is not sufficient.
No in-game millisecond/FPS gain is claimed. True hitch-free 120 requires ~8.33 ms
tick frames; these conservative changes do not establish that target is reached.

## Validation and reproducibility

Read-only original used:
`/run/media/varialbe/Festplatte 2/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600/data.win`.

Final outputs: `perf-verified.win` (44,572,934 bytes), `smooth-final.win`
(44,619,526), `smooth-debug-final.win` (44,623,110), and sequential composition
`perf-smooth-verified.win`. `cgfmm verify` accepts all four: reload, valid references,
and decompile/recompile of 1,828 / 1,835 / 1,839 / 1,839 code entries respectively.
Compiled-source inspections confirm default 120, bypass before instance scans,
separate debug timing classification and the hoisted avoidance locals.
Sequential composition is a smoke test, not a three-way merged-mod playtest.

Build environment issue: the nested Underanalyzer checkout is empty. Exact root
`dotnet build -c Release` cannot pass here: the solution explicitly lists its
missing csproj. NuGet audit also cannot reach nuget.org. No external sources were
edited to mask either issue. Copied the existing local sibling checkout into
`out/phase6a/Underanalyzer` and used an untracked MSBuild ProjectReference override.
Tool and CLI Release builds pass with **0 warnings, 0 errors**. A temporary copy of
the full solution mapping only project locations also passes **0 warnings, 0
errors**, including GUI and test assemblies. Commands:

```sh
dotnet build tools/Jamm.ModMaker -c Release -m:1 -p:NuGetAudit=false \
  -p:CustomAfterMicrosoftCommonTargets="$PWD/out/phase6a/dependency.targets"
dotnet build out/phase6a/validation.sln -c Release -m:1 -p:NuGetAudit=false \
  -p:CustomAfterMicrosoftCommonTargets="$PWD/out/phase6a/dependency.targets"
```

Core tests build but VSTest aborts before running tests: sandbox denies its TCP
communication socket (`SocketException (13): Permission denied`). This is not a
test pass. `git diff --check` passes. Build/verify logs are in out/phase6a.

Remaining validation: orchestrator must run base/Perf/merged comparisons in-game,
check state changes and camera transitions, F7/input/Tab, recovery from overload,
nightfall Gear and special fish, and measure the new per-frame timing fields.
Smooth's bypass decision precedes Draw, so a newly expensive draw or a scan on a
healthy tick can still overshoot. It is a conservative adaptive optimization, not
a hard frame deadline. Full 120 FPS and negligible fallback milliseconds remain
unproven until those runs.

## Follow-up

Orchestrator in-game review measured the merged Perf tick frame at ~29 ms versus
34 ms before Perf (~5 ms, ~15% less tick-frame time). These are supplied runtime
measurements, not a new local benchmark; they do not establish hitch-free 120 FPS.

### Bypass pacing

Fixed GUI End to choose `jamm_logic` as the frame limiter's `room_speed` whenever
`jamm_bypass` is true. Previously it chose `max(target, logic)` even while scheduling
a tick every frame: a ~29 ms tick at a 120 limiter could therefore run ~33 ticks/s.
Bypass now uses the rate captured after Girl's Step (normally 30, Tab held: 60),
so the engine paces ticks as in the original. Interpolated frames still use the
selected render target, and F7's saved 60/120/off cycle is unchanged. The compiled
GUI End was inspected to confirm this branch. In-game F7/Tab/recovery checks remain
for the orchestrator; no local game runtime measurement is claimed.

### Destroyed fish lights

FishThinner user event 0 (`Other_10`) and its Step `kill_extras` path destroy the
fish-owned `light`. A later game-owned activation or `fish_respawn` instance copy
can retain that dead id. This is a base/Colour lifecycle bug, independent of
Smooth. Perf now adds only `instance_exists(light)` guards in these entries:

- `gml_Object_Magmer_Draw_0`: guard the single `light.image_alpha` assignment;
  inherited drawing and cooled-sprite drawing remain untouched.
- `gml_Object_Volteen_Other_13`: guard the two light blend assignments.
- `gml_Object_Volteen_Other_10`: guard each branch's two light blend assignments;
  movement, targets, fear/lunge transitions and the owned pather are unchanged.
  These user events are dispatched from fish Step, so they share the same stale-id risk.

Audited all original and Colour code for `light.`, `instance_destroy(light)`, and
owned `light`/`light2` usage. FishThinner destroys only `light`, not pather, partner,
or other owned instances. Fish light positioning (`light_anchor`) and other fish
light operations use `with (light)`, which skips a missing instance. The remaining
direct accesses are Lightbulb (gear), Dig/Buoy (gear), or scenery light setup
(ChainPlant and related light2 setup), outside FishThinner's `with (Fish)` domain.
`nearest_light` is separately selected and existence-checked. No speculative guards
or light recreation were added. Original and Colour inspection dumps are local in
`out/phase6a/`. Re-decompilation confirms that removing the new guards makes all
three patched entries identical to the originals modulo whitespace. Static sizes:
Magmer Draw 28→32, Volteen Other_13 10→14, Volteen Other_10 83→91 instructions.

### Behavioural interaction and further optimization

FishThinner reads rendered `fps`: below 30 it adds 5 to `cull_trigger`, below 45
adds 1, below 60 adds 0.5, otherwise resets it. Colour enables zoning with
`do_zoning = 1`. Smooth's extra rendered frames raise `fps` and can stop this
Colour culling; bypass renders once per logic tick and retains the old behavior.
This interaction is documented and deliberately unchanged.

No further tick optimization or ini switch was added. State 12 still dispatches
user12, checks night transitions, updates lure/catalog proximity, flip counters,
trawler state and saved speed. Special descendants can override user12, and other
instances read fish state. `fish_disabled` only restoring x/y does not prove that
skipping the surrounding event preserves observable state. Retain the measured
safe savings rather than introduce unproven throttling.

### Follow-up validation

New files under `out/phase6a/`: `perf-followup.win`, `smooth-followup.win`,
`smooth-debug-followup.win`, `perf-smooth-debug-followup.win`. All four pass
`cgfmm verify` (1,828 / 1,835 / 1,839 / 1,839 entries decompile/recompile, valid
references). Perf builds from the read-only original independently of Smooth;
combined output is also a sequential-composition smoke test.

Tool and full relocated validation solution Release builds pass with **0 warnings,
0 errors**, using the previously documented local Underanalyzer override. Exact
root `dotnet build -c Release` remains blocked by the missing nested Underanalyzer
project (this invocation reports Build FAILED with 0 warnings/0 errors). VSTest
again aborts because its communication socket is denied by the sandbox; no test
pass is claimed. Follow-up build, verify and test logs are under `out/phase6a/`.

Also built `merged-followup.win` through the actual three-way merge of Abyss,
Cave, Colour, the locally supplied HD, new Perf and new Smooth-debug. Build and
separate `cgfmm verify` succeed: **2,146 entries**, valid references, 105,121,362
bytes. Decompiling the merged result confirms all four existence guards and the
bypass limiter survived composition. Source texture geometry produces six
out-of-page warnings (PageItems 1276, 1419, 1536, 1587, 1611, 1648); these are
archive geometry warnings, not compiler warnings, and need in-game HD review.
Abyss and Cave light-access audits match the original/Colour findings above.
`git diff --check` passes. No commits, pushes, git configuration changes, input
archive writes, or HdMod edits were performed.
