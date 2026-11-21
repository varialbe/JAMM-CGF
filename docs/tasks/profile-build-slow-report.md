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
