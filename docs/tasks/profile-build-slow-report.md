# Profile build performance report

Date: 2026-11-21

Branch: `fix/profile-build-slow`

## Reproduction

Built the same order against the legacy base: TGE, Antiquity, PWB, Abyss, Colour, Verdant, cave-expansion. The direct build used `cgfmm build --base --order`; the profile build used `build --profile weakbase` with the same seven archive hashes and default profile settings. Each archive was run twice at the dock for 30 seconds with `~/jamm-bench/run.sh`. The runner lock serialized all captures.

The before pair reproduced the report: direct CLI builds averaged 61,805 µs/tick (15.61 FPS), while profile builds averaged 196,820 µs/tick (5.00 FPS), a 3.18× loop-time regression. The profile build also averaged 2,821 active fish versus 2,190 for CLI.

| Build | Runs | Loop time (µs/tick) | FPS | Active fish | Difference from CLI |
| --- | ---: | ---: | ---: | ---: | ---: |
| CLI, before | 2 | 61,805 | 15.61 | 2,190 | baseline |
| Profile, before | 2 | 196,820 | 5.00 | 2,821 | +218.4% |
| Profile, after | 2 | 62,316 | 15.48 | 2,194 | +0.8% |

## Cause

Profile builds selected sanitized library patches by default. The sanitizer removed author performance edits from TGE and PWB, including fish tick throttling and distance culling, `FishThinner` culling, plus scene-object throttles and culling. The raw `--order` path applies the archive changes directly and kept these edits. The sanitized profile therefore ran materially more fish and draw/step work. Profile settings such as Fair-Play, fish value cap, money mode, and texture repacking matched the CLI build; the profile had no extra builtin mods. This `weakbase` profile had no addon roles, so it used the same single build pass as the CLI. Both builds had the same generic instance guards; there was no extra profile controller or duplicated runtime injection.

Two `--profile --profile-scripts` attribution captures showed the same hot path. In the CLI capture, `Fish_Step_2` measured 80,105 µs/tick at 2,200 active fish. In the sanitized profile capture it measured 301,859 µs/tick at 3,433 active fish. This instrumentation adds overhead, so those event values are used to identify the cause, not for the unprofiled comparison table.

## Fix

Author performance edits are now preserved by default for new and existing library entries. A new `KeepAuthorPerformanceChangesConfigured` marker distinguishes a deliberate per-mod choice from legacy metadata written when `false` was only the old default. Legacy entries now follow the fast default; setting the Advanced-mode checkbox writes an explicit choice. Clearing **Keep the author's performance changes** remains the opt-in to sanitize those edits while retaining content.

The GUI loads the effective value, and the user guide and CLI reference document the behavior. Tests cover legacy metadata, default patch selection, the sanitize opt-out, and the GUI setting.

## Verification

- `dotnet build` for CLI, GUI, and ModMaker projects: passed with zero warnings and errors, using the task's external UndertaleModTool targets fallback because this worktree's submodule is empty.
- `dotnet test` for Core: passed (640 passed, 17 real-file tests skipped).
- `dotnet test` for GUI: passed (130 passed, 1 real-file test skipped).
- Before and after in-game captures used `~/jamm-bench/run.sh`; no game archives or private inputs are in the repository.

## Round 2 — sanitizer split (CGF-PERF-REGRESS-002)

Round 2 supersedes the global preserve-by-default fix above. The default now sanitizes harmful author edits and keeps only recognized benign performance hunks. Sanitizer rules are at version 5 so stored patches and reports refresh. `KeepAuthorPerformanceChangesConfigured` remains as migration plumbing: a legacy record created by a71b728 can contain `KeepAuthorPerformanceChanges: true` even though the player never selected it; unconfigured records now resolve to the harmful-change sanitizer. Turning on **Keep all the author's performance changes** records an explicit choice and keeps every author performance edit.

### Hunk classification

The TGE and PWB inputs were inspected locally in `~/jamm-bench/inputs/private`; none of those archives or extracted content is included in this repository.

| Disposition | Real hunks | Reason |
| --- | --- | --- |
| Keep as benign performance | `Fish_Draw_0`, `fish_basicdraw`, `fish_predraw_switchcase` off-screen draw gates; CaveRock's five-step visual/off-screen check; Weed and Dangler off-screen visual early-outs | These only skip rendering or environmental-object visual work. They do not deactivate fish, hide sonar results, or alter catches or population. |
| Strip as harmful | TGE/PWB `Fish_Step_2` `check_timer >= 6` wrapper and paired `Fish_Create_0` initializer | The six-tick gate wraps the complete fish step body, including in-range fish behavior. It can delay movement and fishing-state updates, so the real hunk is not an off-screen-only throttle. |
| Strip as harmful | TGE/PWB `FishThinner_Step_0`: `if (fps < 60) cull_trigger += 65` | This accelerates destructive fish culling and changes the fish population visible to the player. It is FPS-adaptive population control, not a safe scheduler. |
| Strip as harmful | `SonarPanel_Draw_0` distance visibility checks; `Girl` timing edits; `NewWorld` room speed 1; `fpsController` busy-wait | These directly affect sonar visibility or game timing and pacing. |

The broad `tick-throttle` and `distance-cull` labels are no longer enough to decide the default action by themselves. The sanitizer marks the known render-only and scene-object cases as `benign-perf`; other tick throttles and sonar/population culls remain stripped. The Advanced option still keeps all author performance edits when explicitly enabled.

### Final benchmark

Built the same seven-pack order against the legacy base: TGE, Antiquity, PWB, Abyss, Colour, Verdant, and cave-expansion. The CLI used direct `build --base --order`; the profile used `build --profile weakbase` with its default settings. Both used the same base and archive hashes. Each artifact ran twice per scene for 30 seconds with `~/jamm-bench/run.sh`, seed 1337. The loop-time target was not met because the remaining performance-heavy real hunks are harmful under the rules above.

| Scene | CLI loop mean (µs/tick) | Profile loop mean (µs/tick) | Profile vs CLI | CLI active fish mean | Profile active fish mean |
| --- | ---: | ---: | ---: | ---: | ---: |
| Dock | 63,606 | 171,936 | +170.3% | 2,189 | 2,720 |
| Deepwater | 65,268 | 163,406 | +150.3% | 2,181 | 2,658 |

The harmful `tick-throttle` hunk is the main CPU cost: removing the six-tick wrapper makes the full `Fish_Step_2` body run for more fish updates. Earlier profile-script attribution for this same sanitizer-versus-raw comparison found `Fish_Step_2` at 301,859 µs/tick in the sanitized profile versus 80,105 µs/tick in the raw CLI build, alongside 3,433 versus 2,200 active fish. Retaining that wrapper by default would conceal fish-step updates and can alter fishing results. The `fps-adaptive` `FishThinner` hunk also reduces active fish by repeatedly triggering culls; it must remain stripped because it changes population. Keeping benign draw and scene-object culls alone does not close the performance gap.

### Sonar check

Ran the `sonar` scene twice on each artifact and compared the `jamm_bench_sonar.csv` output. The `fish_count` samples remained nonzero throughout; `active_fish_mean` averaged 2,179.5 on CLI and 2,764.76 on the profile build. The profile still has active fish in the sonar scene, and the `SonarPanel_Draw_0` distance edits that would hide sonar results are stripped. The scene CSV measures active fish, not a separate blip-render count.

### Round 2 verification

- `Jamm.Core.Tests`: **641 passed, 0 failed, 17 opt-in real-file tests skipped**.
- `Jamm.Gui.Tests`: **130 passed, 0 failed, 1 opt-in real-file test skipped**.
- CLI and ModMaker Release builds: **0 warnings, 0 errors**.
- The worktree's UndertaleModTool submodule is empty, so the solution-level `dotnet test -c Release` cannot restore its two missing project paths. Both test projects were run directly in Release with a temporary MSBuild target pointing to the existing external UndertaleModTool checkout; the submodule and private test inputs were not modified.
