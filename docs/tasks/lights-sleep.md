# Re-enable DecorSleep for owner-anchored fish lights — correctly

## Why
Merge 142bbb5 (fix/bulbfish-light) excluded every externally positioned object from DecorSleep:
Light, Light2, BulbfishLight, ThermoLight, CrystalfishLight, GlowfishLight, GeigerLight, LampisLight, MagmerLight.
In all7 that cut DecorSleep tracking from 8928 to 4032 instances and cost ~45% FPS
(deepwater 27.3 → 18.8 FPS; dock 33 → 21.5). The user reported Bulbfish "rendered a bit weird" with lights covered,
but the actual root cause was NEVER identified or visually verified. Goal: get the FPS back AND no visual glitch.

## Steps
1. Reproduce the glitch visually BEFORE changing anything. Build a bench scene variant (or extend an existing scene in
   tools/Jamm.ModMaker) that places the camera next to several Bulbfish / Glowfish / Crystalfish with their lights,
   moves the camera away (> SleepMargin 2000 px) and back, and lets the fish swim. Compare light coverage on
   (revert the exclusion in DecorSleepAnalysis.cs locally) vs off.
   Record video: `REC=1 REC_SEC=60 ~/jamm-bench/run.sh <win> <label> <scene> 60 --fish-sleep --decor-sleep`
   (MM=<your built jamm-modmaker.dll>); video at ~/.var/app/com.valvesoftware.Steam/data/JAMM-CGF/bench-runs/<label>-<scene>/video.mp4.
   Extract frames with ffmpeg (grim does not exist). Base archive: ~/jamm-bench/builds/pd/all7.win.
2. Identify the exact mechanism (candidates: visible save/restore `jamm_ds_bvis` racing light_anchor's visible;
   home-position restore (flags 4/8/12) teleporting a light away from its owner; sleep test using a stale light position
   while the owner moved; light Step guard freezing alpha/scale; deactivation of lights whose owner later references them).
3. Fix narrowly: keep anchored lights tracked, but give them a mode that e.g. uses live position, never restores x/y,
   does not restore stale visibility (owner/light_anchor owns it), and is never deactivated if the owner dereferences it.
   Keep the existing light_anchor suffix logic consistent.
4. Verify visually with the same scene (frames before/after, describe them in the report) and measure:
   all7 dock+deepwater, main vs branch, identical flags `--fish-sleep --decor-sleep`, 60 s captures, 3 interleaved reps.
   Then crash scenes: sonar 180, deepwater 180, sonar 180 --deep-sonar.
5. Report docs/reports/lights-sleep.md (mechanism, frames, table). Commit on branch fix/lights-sleep; do not push.

Rules: NEVER touch CGF-Personal, compatdata cgf-personal or 343780. No xdotool/key injection. Use run.sh (it flocks).
Labels prefix `ls-`. Clean bin/obj is fine; disk is tight (~20 GB free).
