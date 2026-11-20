# Local Benchmark Mode (`jamm-modmaker bench`)

`tools/Jamm.ModMaker` provides a test-only, minimally intrusive benchmark instrumentation pass (`bench`) for comparing frame-time behavior across identical conditions (for example, original `data.win` vs. modded builds) without modifying original archives or touching real user saves.

## Safety invariants

- **Read-only inputs**: `jamm-modmaker bench <in.win> <out.win>` opens `<in.win>` read-only, refuses to overwrite an existing `<out.win>` or write to `<in.win>`, and publishes `<out.win>` only after writing a temporary file and reloading it to verify `obj_jamm_bench`.
- **No real user saves**: `obj_jamm_bench` locks `SaveManager` (`SaveManager.saving_locked = 1`) and clears `saveflag = 0` / `loadflag = 0` before `SaveManager` loads a slot, so it boots into a clean fresh session in `NewWorld`, dismisses `GameModeMenu`, and never reads or writes `sdata*` or `meta.ini`.
- **Test-only**: `BenchmarkMod` lives only in `tools/Jamm.ModMaker`, not in `BuiltinMods` or player-facing `cgfmm`. Never commit or upload any `.win` game archives or extracted game assets.

## How to build and run

Apply the **identical instrumentation pass** to each archive you want to compare (for example, original vs. modded), choosing a new output path for each:

```sh
# Baseline (original legacy archive -> instrumented benchmark archive)
dotnet run --project tools/Jamm.ModMaker -c Release -- bench \
  /path/to/original/data.win /tmp/bench/orig-bench.win \
  --scene dock --warmup 5 --duration 15

# Comparison build (e.g. merged/modded archive -> instrumented benchmark archive)
dotnet run --project tools/Jamm.ModMaker -c Release -- bench \
  /path/to/merged.win /tmp/bench/merged-bench.win \
  --scene dock --warmup 5 --duration 15
```

Because runtime `jamm_bench.ini` overrides the baked defaults, you can also build **one** instrumented archive per variant and switch scenes/durations by placing a `jamm_bench.ini` in the game runner's writable save/working sandbox:

```ini
[Bench]
Scene=deepwater
WarmupSeconds=5
CaptureSeconds=15
MaxSamples=50000
AutoExit=1
```

When launched, the game transitions past `room11` into `NewWorld`, applies fixed scene initialization, waits through `WarmupSeconds` (plus at least 10 frames), records `CaptureSeconds` (capped at `MaxSamples`) purely in memory with zero disk I/O and zero HUD drawing, flushes one CSV file (`jamm_bench_<scene>.csv` by default) in the runner sandbox, emits a `show_debug_message` summary, and exits (`game_end()`) if `AutoExit=1`.

After scene initialization, the benchmark requests a windowed `1200x720` window and a `1200x720` application surface. The CSV records the resulting window and application-surface dimensions and fullscreen state; `render_size_normalized=0` plus a warning means the request did not take effect or was overridden. No per-frame resize enforcement is used.

## Deterministic scenes and fixed initialization

All scenes run inside `NewWorld` with `random_set_seed(Seed)` (default `1337`), daytime locked (`AmbianceController.lockDay = 1`), `SaveManager.saving_locked = 1`, and any startup `GameModeMenu` dismissed:

| Scene | Setup |
| --- | --- |
| `dock` | `Girl` positioned at `DockX` (default `815`), no `Boat`, sonar disabled (`view_visible[3] = 0`, `SonarPanel.active = 0`). |
| `sonar` | Same `dock` position with `ToolData` sonar slot unlocked, `SonarPanel.active = 1`, `SonarPanel.shouldDraw = 1`, and `view_visible[3] = 1` (add `--deep-sonar` or `DeepSonar=1` to also enable `SonarPanelAdvanced`). |
| `deepwater` | Sonar disabled, `Boat` slot `BoatSlot` (default `7`) unlocked/spawned and placed at `DeepX` (default `21000`), `Girl.x = DeepX`, and `Camera` + `view[0]` pinned to `(DeepX, DeepY)` (default `(21000, 7200)`). When the base archive has `Light` or `Light2` using `spr_light_radgrad` and inheriting `AbstractLight`, one such instance is created at the viewport center with fixed `4x` scale and `0.85` alpha, then kept centered during the scene. |

## CLI options and INI keys

| CLI flag | INI key (`[Bench]`) | Default | Purpose |
| --- | --- | --- | --- |
| `--scene <name>` | `Scene` | `dock` | `dock`, `sonar`, or `deepwater` |
| `--warmup <sec>` | `WarmupSeconds` | `5` | Warmup duration before capture (`0..3600`) |
| `--duration <sec>` | `CaptureSeconds` | `15` | Bounded capture duration (`0.1..3600`) |
| `--max-samples <n>` | `MaxSamples` | `50000` | Hard in-memory sample cap (`1..200000`) |
| `--out-file <name>` | `OutputFile` | `jamm_bench_<scene>.csv` | Relative sandbox CSV filename (rejects paths, `sdata*`, `meta.ini`, `*.win`) |
| `--auto-exit` / `--no-exit` | `AutoExit` | `1` (`true`) | Exit cleanly after flush (`0` allows `F9` to rerun) |
| `--seed <n>` | `Seed` | `1337` | Fixed `random_set_seed` value |
| `--dock-x <x>` | `DockX` | `815` | Dock horizontal coordinate |
| `--deep-x <x>` | `DeepX` | `21000` | Deepwater boat/camera X coordinate |
| `--deep-y <y>` | `DeepY` | `7200` | Deepwater camera Y coordinate |
| `--boat-slot <n>` | `BoatSlot` | `7` | `BoatData` slot used in `deepwater` (clamped to `[1, nItems - 1]`) |
| `--deep-sonar` | `DeepSonar` | `0` (`false`) | Also enable `SonarPanelAdvanced` in `sonar` scene |
| `--show-hud` | `ShowHud` | `0` (`false`) | Show status box outside capture (suppressed during capture) |

## Distinguishing game loop vs. present

Each frame in GameMaker 1.4 executes in this order:

```text
[BeginStep] -> [Step / Alarms / Motion] -> [EndStep] -> [PreDraw .. DrawGUI] -> [DrawGUIEnd]
   |<------------------------- loop_us (active GML + draw) ------------------------->|
                                                                                     |
   |<------- present_sleep_us (Swap/Present, VSync, room_speed sleep, OS pump) ----->| (to next BeginStep)
|<---------------------------- frame_us (DrawGUIEnd to DrawGUIEnd) ----------------------------->|
```

The output CSV starts with `#` metadata/summary lines (`min`, `p50`, `p95`, `p99`, `max`, `mean`, `avg_fps`) followed by `frame_index,frame_us,loop_us,present_sleep_us` rows in microseconds (`get_timer()`):

- **`frame_us`**: `get_timer()` frame-to-frame at `DrawGUIEnd` (placed at `Depth = -999999`, running after normal draws/GUI and before `obj_jamm_smooth` at `-1000000`). Equals the complete wall-clock frame period (`game loop + present/sleep`).
- **`loop_us`**: `get_timer()` at `DrawGUIEnd` minus `get_timer()` at `BeginStep` of the same frame. Measures active CPU/GML Step + Draw work, excluding post-`DrawGUIEnd` swap/present, VSync wait, and `room_speed` frame-limiter sleep. On `SmoothMod` render-only frames where `BeginStep` is frozen, `loop_us` is `-1` (while `frame_us` is still recorded).
- **`present_sleep_us`**: `frame_us - loop_us` when `loop_us >= 0` (else `-1`), which equals the exact gap from frame `k-1`'s `DrawGUIEnd` to frame `k`'s `BeginStep`: SwapBuffers/present, VSync blocking, `room_speed` pacing sleep, and engine/runner inter-frame overhead.

`avg_fps` and `draw_samples` count `DrawGUIEnd` callbacks. `step_samples` counts `BeginStep` callbacks during the same capture, and `draws_per_step` exposes their ratio. These counts can differ when the engine renders more than once per simulation step; they are reported separately and are not deduplicated. Use the independent MangoHud presented frametimes for presentation cadence.

**Reading the numbers:**
- **Game-loop bound**: `loop_us` approaches or exceeds the frame budget (`33,333 us` at 30 FPS, `16,667 us` at 60 FPS, `8,333 us` at 120 FPS) and `present_sleep_us` is small.
- **Paced / present bound**: `loop_us` is well below budget while `frame_us` sits at a pacing step (`33,333 us`, `16,667 us`, or display refresh) and `present_sleep_us` absorbs the remainder (`room_speed` cap, VSync, or Wine/DX9 swap/present overhead).
