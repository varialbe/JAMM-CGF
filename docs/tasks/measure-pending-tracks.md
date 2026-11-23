# Task: measure the built-but-unmeasured perf tracks (bench/pending-tracks)
Tracks: fish-micro (option exists, default off), fish-virtualization 2b, engine-opt phase 3 (empty Step stripping, Water1→tiles),
light culling (partial). Find their branches (`git branch -a | grep -i 'micro\|virtual\|engine\|light'`, remotes origin+vps; fetch vps).
For each: rebase/merge onto current main (56aa26c+, includes FishSleep), build vanilla/pub5/all7, measure LOCAL via ~/jamm-bench/run.sh
(flock serialized, isolated prefix) 2× old vs new on dock+deepwater, crash scenes. Finish half-done ones (light culling) if small.
Verdict per track (keep/drop) with numbers in docs/reports/pending-tracks.md. Never CGF-Personal/cgf-personal/343780, no xdotool.
Commit on branch, no push.
