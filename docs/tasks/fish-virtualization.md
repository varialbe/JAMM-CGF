# Task: fish virtualisation research (branch `exp/fish-virtualization`, Codex Luna)

User request (BRIEFING section 14). Research + prototype + plan; full implementation later by Hydrogen after review.
Idea: fish far from camera AND lure do not exist as instances but as records (species, position, state, seed,
relevant instance variables) in arrays/ds_grid per zone/chunk; coarse movement per zone (no Step/Draw). When the
camera/lure/sonar radius approaches → create a real instance with exactly that state; when it leaves → back to a
record. Goal: ~100–300 relevant instances instead of ~2,000. Additionally: a per-tick time budget (far fish
round-robin up to e.g. 8 ms, near fish always).

## Clarify
- Endless Ocean chunk streaming as template (`src/Jamm.Core/Builtins/Worldgen/jamm_chunk_stream.gml`,
  `jmk_endless_ocean_load/dispose.gml`, the shared `jamm_fish_sonar_init` on `fix/fish-sonar`).
- Which instance variables must be stored per species (use `docs/reports/ocean-fish-parity.md`).
- Who counts/iterates all fish (FishThinner, SonarPanel, spawners, SaveManager, CatalogData, mods/packs incl. the
  7 packs on the VPS) → compatibility list per pack.
- Spawn/despawn logic and population must stay equal (catch-rate/sonar parity via autoplay CSVs).
- Relation to Fish Sleep (`origin/exp/fish-sleep`), fish-AI batching (`exp/fish-ai-batching`) and the native DLL
  offload (`exp/native-offload`): overlap, combinable, order.
Prototype with vanilla + 1 pack, off by default, measured serially (shared game lock via `~/jamm-night/bin/run.sh`;
on EXITED `~/jamm-night/bin/steam-restart.sh` and retry; delete `~/jamm-night/runs/*/game` copies).
Plan in `docs/design/fish-virtualization.md` with steps and a measurement point each.
Commit on this branch; do not push. AGENTS.md applies.
