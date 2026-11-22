# Fish Sleep – local Part A results

## Implementation

Asleep fish now take the `Fish_Step_2` fast path first: one `jamm_asleep` read and a comparison against the global sleep tick. Wake checks run once per eight ticks, with the next slot staggered from the fish instance ID. Awake fish also check eligibility only in their eight-tick slot. Fish Create initializes the slot without reading controller globals, so fish created before the sleep controller remain safe.

Lure Step advances the targeted fish's slot to the current tick using nested instance-existence checks. This wakes a targeted sleeper without waiting for its normal slot. Covered subclass Step, Draw, and Collision guards now use a direct `jamm_asleep` read, which Fish Create initializes. The eligibility analyzer recognizes both the older safe guard and this direct guard.

FishHuge descendants remain exempt as huge/boss species. Ordinary FishLarge descendants are covered when their events pass the existing eligibility checks; custom AI, alarms, collisions, global side effects, and nontrivial Draw logic remain exempt. There is no general proof that a custom movement event is safe to pause and catch up, so Fish Sleep does not infer safety from movement-looking code alone.

## Local benchmark

Two 30-second runs per setting, scene and input; seed 1337; 1200×720. Off runs omit Fish Sleep, on runs use `--fish-sleep`. Values below are the mean across the two runs. Clean vanilla is reported separately from the `builds/decor/vanilla.win` input.

| Input | Scene | Off FPS / loop ms | On FPS / loop ms |
| --- | --- | ---: | ---: |
| vanilla (`builds/decor`) | dock | 49.1 / 19.9 | 63.8 / 14.9 |
| pub5 | dock | 12.3 / 79.9 | 21.2 / 45.8 |
| all7 | dock | 16.8 / 57.8 | 20.1 / 48.1 |
| clean vanilla | dock | 48.5 / 20.1 | 68.1 / 13.9 |
| vanilla (`builds/decor`) | deepwater | 49.5 / 19.7 | 70.0 / 13.5 |
| pub5 | deepwater | 12.7 / 77.3 | 20.2 / 47.8 |
| all7 | deepwater | 16.5 / 58.8 | 19.4 / 49.8 |
| clean vanilla | deepwater | 49.2 / 19.8 | 68.3 / 13.9 |

Unprofiled `fish_step_us` averages were 20.6 → 12.4 ms/tick for all7 dock and 23.1 → 14.9 ms/tick for all7 deepwater. Fish Sleep improved FPS in every measured pair, with the largest gain in pub5.

The all7 script-profile run (`fsa2-profile-all7-on-dock`) measured a 32.3 ms/tick aggregate fish-step time with profiler overhead. `Fish_Step_2` still had 2,200 calls/tick and 46.6 ms/tick inclusive in the entry profile; `FishLarge_Step_0` had 474 calls/tick and 6.4 ms/tick. Fish Sleep exits GML logic early but does not remove GameMaker's per-instance event dispatch, so profiled numbers remain much higher than the unprofiled capture.

## Population and counter observations

Fish counts at capture ranged from 945 to 1,622 for vanilla (`builds/decor`) across off/on and scenes, and from 928 to 1,540 for clean vanilla. Pub5 ranged from 2,038 to 2,152; all7 stayed near 2,200. The vanilla increase is outside Part A and is assigned to Part B's population investigation. Fish Sleep's debug counter is also not reliable yet: the profile CSV reported `asleep=2766` with 2,202 active fish, confirming the Part B counter-drift issue.

The profile still shows meaningful `Fish_Step_2` cost despite the unprofiled improvement. The fast path reduces fish logic work, but sleeping instances continue to receive the engine event callback. FishLarge species with custom or boss behavior were kept awake unless the existing event eligibility analysis could establish coverage.

## Validation

- `dotnet build -c Release`: passed with zero warnings.
- `dotnet test -c Release`: passed; Core 659 passed / 18 skipped, GUI 130 passed / 1 skipped.
- Fish Sleep eligibility and zone/wake helper tests passed, including assertions for staggered checks and the direct Lure wake signal.
- Benchmark matrix: vanilla, pub5, all7 and clean vanilla; dock and deepwater; off/on; two rounds each.
- All7 dock: one `--profile-scripts` capture with Fish Sleep enabled.

Benchmark inputs and generated game archives remained local; only this report and source/test changes are committed.
