# Next all7 hotspots after the proxy removal (main 8530f68)

State: all7 dock ~30.8 FPS / deepwater ~29.0 (loop ~32 ms), pub5 ~28.8/27.4, vanilla dock ~103 (60 s, `--fish-sleep --decor-sleep`).
Earlier profiles (`--profile`) are distorted by per-invocation timer overhead (both variants ~95 ms/tick).

1. Build a low-overhead profiling method: e.g. sampled profiling (instrument only 1 of N ticks, or only a rotating subset of
   code entries per run and merge), or subtract calibrated per-call overhead; validate it against the known unprofiled
   loop time (the sum must roughly match ~32 ms). Document in docs/.
2. Produce the top-25 cost table for all7 dock and deepwater on current main (Step, Draw, End Step, Alarms, collision,
   engine-side costs estimated as residual).
3. Pick the 3 biggest addressable items (candidates seen before: CaveRock_Step_0, Rock_Step_2, MagnapinnaTentaclesAllEndstep,
   SceneItem_Step_2, SonarPanel_Step_1, Fish_Other_23, AbstractLight_Draw) and implement optimizations — behaviour-preserving
   ones on by default in PerfMod/DecorSleep, behaviour-changing ones as default-off PerfSettings flags.
4. A/B each vs main: all7+pub5, dock+deepwater, 60 s, 3 interleaved reps (≥3% and consistent to count). Crash scenes
   sonar 180, deepwater 180, sonar --deep-sonar. Visual sanity via REC=1 video frames (ffmpeg) for anything that touches drawing.
5. Report docs/reports/next-hotspots.md; commit on perf/next-hotspots; do not push. Run `dotnet test` AND
   `bash tools/run-real-tests.sh` before committing.
Rules: never touch CGF-Personal/compatdata cgf-personal/343780; no xdotool; run.sh only; labels `hs-`.
Home disk is tight (~3.5 GB free): keep scratch in /tmp or on "/run/media/varialbe/Festplatte 2", delete instrumented copies.
