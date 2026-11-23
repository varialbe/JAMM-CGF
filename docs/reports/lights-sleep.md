# Owner-anchored fish lights and DecorSleep

## Cause and fix

The old exclusion avoided several bad interactions by leaving every externally positioned
light out of DecorSleep. That hid the actual problem: these light instances are owned by a
fish and follow it through a `light_anchor` helper. Generic DecorSleep treated them like
independent decorations, so it could save a visibility value that was already stale while
the owner/helper changed visibility. It could also restore saved home coordinates for a
light that had moved with its owner. Deactivating such an instance was unsafe because the
owner can still dereference it.

DecorSleep now identifies instance variables assigned by `instance_create` and synchronized
by helper scripts. Those owner-anchored instances remain tracked, use their live position,
never restore home coordinates or deactivate, and do not save/restore cached visibility.
The `light_anchor` suffix hides each linked light whenever its owner's `jamm_ds_vis` marker
is false. Crystalfish's `visible = instance_exists(...)` synchronization is treated as a
light-sync step: it is guarded while sleeping and recalculated when the owner wakes.

This increases all7 tracking from 4,032 to 5,200 instances in dock and 5,201 in deepwater;
the total deactivated sleeping decorations remain 3,156 and 3,001 respectively.

## Visual check

I added the `lightcycle` benchmark scene. It cycles Bulbfish, Glowfish, and Crystalfish near
the camera, moves the fish and camera together 5,000 pixels away for 60 ticks, then returns
for 60 ticks while fish simulation remains enabled. Recordings used `all7.win`, 60-second
captures, and `--fish-sleep`; DecorSleep was disabled for the off capture and enabled for
the on capture:

- Off: `ls-final-off-lightcycle-lightcycle/video.mp4`
- On: `ls-final-on-lightcycle-lightcycle/video.mp4`

The files are in `/home/varialbe/.var/app/com.valvesoftware.Steam/data/JAMM-CGF/bench-runs/`.

Frames were extracted at 10, 20, 30, 40, and 50 seconds. At 10 seconds, the off capture
shows several yellow-lit fish while the on capture shows dark water and a dim blue glow.
Later frames cycle through the same surface and crystal regions. I did not see a detached
or displaced light in the on capture. The tutorial overlay and moving scene make this a
useful owner-following check, but not a tightly aligned close-up comparison of each species;
the frames do not establish pixel-identical light coverage.

## Performance

All runs used `all7.win`, `--fish-sleep --decor-sleep`, a 60-second capture after warmup,
and three repetitions per cell. The main-versus-branch interleaved matrix is below; values
are means across the three captures. The branch in this interleaved matrix already had the
owner-anchored mode; a subsequent code review expanded the `light_anchor` suffix to handle
both `light` and `light2`. The final code was then captured in a second branch-only matrix,
shown below as a separate sanity check because it was not interleaved with main runs.

| Scene | Main tracked / deactivated | Branch tracked / deactivated | Main FPS | Branch FPS | Main loop mean / p50 (ms) | Branch loop mean / p50 (ms) |
|---|---:|---:|---:|---:|---:|---:|
| Dock | 4,032 / 3,156 | 5,200 / 3,156 | 21.57 | 21.56 | 44.02 / 44.08 | 43.85 / 43.90 |
| Deepwater | 4,032 / 3,001 | 5,201 / 3,001 | 20.32 | 20.91 | 46.58 / 46.23 | 45.22 / 45.15 |

The interleaved comparison was effectively neutral in dock and improved mean deepwater FPS
by about 2.9%. It did not recover the historical FPS loss described in the task. This fix
restores coverage and owner-safe visibility/position handling; it deliberately leaves
owner-referenced light instances active rather than deactivating them.

The post-expansion branch-only refresh recorded 20.90 mean FPS and 45.05 ms mean loop time
in dock, and 20.99 mean FPS and 45.02 ms mean loop time in deepwater (three captures each).
These values are not a controlled comparison against main because they were recorded later
and without interleaving; the interleaved table above is the comparison result.

## Crash checks and verification

The branch recorded each crash scene without a timeout or exit error:

| Scene | Duration | Result |
|---|---:|---|
| Sonar | 180 s | Recorded |
| Deepwater | 180 s | Recorded |
| Deep sonar | 180 s | Recorded |

`dotnet build -c Release -m:1` completed with zero warnings and zero errors. The full test
suite passed: 685 Jamm.Core tests and 131 GUI tests; 19 real-file tests were skipped because
the local game-file environment variables were unset.
