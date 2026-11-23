# All7 benchmark gap

Date: 2026-11-23. Branch: `inv/all7-gap`.

## Cause

The 33 FPS archive used the pre-proxy Fish Sleep implementation. The 21 FPS bench archive
received the later dormant-fish implementation when `jamm-modmaker bench` installed Fish
Sleep. Commit `9a7f0fa` added per-species dormant objects, `instance_change` on sleep/wake,
and a controller Step event that scans the dormant species every eighth tick. The old path
does each staggered wake check inside that fish's own `Fish_Step_2` guard. The new scan and
object swaps added work outside the measured fish-step bucket; similar fish and decoration
counts therefore did not explain the lost loop time.

Bytecode dumps of the regenerated copies confirmed the difference. The old `Fish_Step_2`
begins with the `jamm_asleep` / `jamm_sleep_next` check and calls `jamm_sleep_wake()` itself.
The proxy version adds `DormantWakeGml` to
`gml_Object_obj_jamm_fish_sleep_Step_0`; it iterates
`global.jamm_sleep_dormant_objects` with a `with` block, checks every sleeping instance's
state and position, and can wake it. `jamm_sleep_enter` and `jamm_sleep_wake` also switch
object types and save/restore sprite and image state. The proxy implementation was the one
material Fish Sleep change between the archive generations that reproduced the gap.

## Controlled confirmation

I used the same raw `pd/all7.win` input, current Decor Sleep, dock scene, seed 1337, 8-second
warmup, 60-second capture, and `~/jamm-bench/run.sh`. The six runs were serialized and
interleaved. B used the then-current Fish Sleep implementation. B′ used the pre-`9a7f0fa`
Fish Sleep implementation, with current Decor Sleep held constant. Both variants tracked
5,200 decorations and deactivated 3,156; fish counts stayed near 2,200.
Run labels: `gap-b-r1/r2/r3` and `gap-bp-r1/r2/r3`. Player profile labels:
`gap-player-r1` and `gap-player-keep-r1`.

| Run | FPS | Mean loop ms | Mean step ms | Mean fish-step ms |
|---|---:|---:|---:|---:|
| B r1 | 21.40 | 44.20 | 41.04 | 12.29 |
| B′ r1 | 32.40 | 28.58 | 25.76 | 9.51 |
| B r2 | 20.96 | 44.88 | 41.58 | 12.17 |
| B′ r2 | 31.87 | 29.11 | 26.30 | 9.98 |
| B r3 | 21.02 | 44.85 | 41.58 | 12.13 |
| B′ r3 | 32.57 | 28.45 | 25.72 | 9.43 |
| **Mean B** | **21.13** | **44.64** | **41.40** | **12.20** |
| **Mean B′** | **32.28** | **28.71** | **25.93** | **9.64** |

B′ improved mean FPS by 52.8% and reduced mean loop time by 35.7%. It is within 0.7 FPS of
the original 33.0 FPS archive. This identifies the dormant-proxy Fish Sleep revision as the
direct cause of the historical A/B gap; the run measured the proxy path as a unit, including
its scan and object swaps.

## Player build check

I also built the seven-mod profile with `cgfmm build --profile` from the legacy base, plus
JAMM Perf with Fish Sleep and Decor Sleep enabled. This was a real player build from TGE,
Antiquity, PWB, Abyss, Colour, Verdant, and cave-expansion archives. Its normal sanitized
profile recorded **18.41 FPS**, 51.83 ms mean loop, and 27.82 ms fish-step. It did not
contain the dormant proxy implementation after this branch's fix, so another issue remained.

The build's `Fish_Step_2` decompilation explained that result: JAMM's default mod sanitizer
removed TGE/PWB author performance hunks, including `check_timer >= 6` and the 5,000-pixel
camera cull. Those hunks are explicitly classified as tick throttling and distance culling
in Phase 18 because they change when fish logic and visibility update. The archive used for
the original B/B′ comparison retained those mod-author changes.

To isolate that difference, I rebuilt the same temporary player profile with
`KeepAuthorPerformanceChanges` enabled for TGE and PWB only. The resulting `cgfmm build`
archive recorded **32.31 FPS**, 28.63 ms mean loop, and 9.53 ms fish-step in one 60-second
capture. Its decompilation contained the six-tick guard. This is an opt-in profile result,
not a reason to change sanitizer defaults; keeping those hunks restores mod-author timing
and offscreen-culling behavior.

## Fix and validation

`FishSleep` no longer creates dormant proxy objects or scans their species from a controller
Step event. Sleeping fish remain their original species and use the staggered per-fish
`Fish_Step_2` wake guard; the lure wake signal still advances the targeted fish's wake slot.
The regression test now checks that Fish Sleep preserves the original object type and does
not add dormant proxy events or `instance_change` calls.

`dotnet build -c Release -m:1` passed with zero warnings and zero errors. The benchmark
captures were produced only by `run.sh` with `gap-` labels. Temporary archives and profiles
remain under `/home/varialbe/jamm-bench` or `/tmp`; no game archives were added to the repo.
