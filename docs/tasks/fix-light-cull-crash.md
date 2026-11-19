# Task: CGF-INSTANCE-003 — stop FishThinner from destroying fish lights (Magmer crash)

## Verified root cause (real game, VPS, 2026-11-19)
Pack `Abyss,Verdant` (and the public 5-pack) crashes at the dock:
`FATAL ERROR Draw Event Magmer: Unable to find any instance for object index '107113' at gml_Object_Magmer_Draw_0`.

* Vanilla room `NewWorld` places 2 `Magmer`; Abyss adds 2 (4 total), Verdant adds 1 (3 total).
  The additive room merge yields **6 Magmer**.
* Vanilla `gml_Object_FishThinner_Step_0` fires `event_perform(7, 10)` (= `FishThinner_Other_10`) whenever
  `fps < 60` for a while (always on weak PCs / heavy packs). `FishThinner_Other_10` culls random fish whose
  `instance_number(object_index) > 5` and that are far from the lure:
  ```gml
  deactivated = 1;
  if (light > 0)
  {
      instance_destroy(light);
  }
  instance_deactivate_object(id);
  other.n_culled++;
  ```
* Zoners (`ZonerDeactivator_Other_11`) later `instance_activate_object(fish[i])`. A reactivated Magmer runs
  `gml_Object_Magmer_Draw_0`: `light.image_alpha = 1 - power(1 - heat_level, 2);` → the light was destroyed → crash.
  With ≤5 Magmer (vanilla, single mods) the fish is never culled, so this vanilla bug is latent until mods are merged.
  Any fish object that owns a `light` instance and dereferences it (Magmer Draw, Volteen user events, mod fish) is affected.

## Required change (Jamm.Core, build pipeline)
Extend `src/Jamm.Core/Building/InstanceCompatibility.cs` (or a sibling class called from `ArchiveBuilder` right after
`InstanceCompatibility.Repair`) with rule **CGF-INSTANCE-003**:

* In the final merged `gml_Object_FishThinner_Other_10` (base text when no mod changed it; the selected/merged mod text
  otherwise), fish that own a light are not culled. Required semantics: a fish with `light > 0` is skipped (not deactivated,
  light not destroyed), and `other.attempts++` still runs for it so the `while (n_culled < number_to_cull && attempts < max_attempts)`
  loop always terminates. Preferred minimal rewrite: change the condition
  `if (instance_number(object_index) > 5 && point_distance(x, y, Lure.x, Lure.y) > 2000)` to
  `if (instance_number(object_index) > 5 && point_distance(x, y, Lure.x, Lure.y) > 2000 && !(light > 0))`.
  Match the known vanilla shape tolerant to whitespace/CRLF; if the text does not match (a mod rewrote the event),
  leave it unchanged and add a `WARNING [CGF-INSTANCE-003] ...` decision instead of guessing.
* The event is usually NOT in `plan.Assets` (no mod touches it). Find out how other runtime patches (e.g. `HudRuntime`,
  `FishRuntime`, `ItemsRuntimePatches`) modify unchanged base code entries and follow the same mechanism so the code is
  recompiled into the output and semantic verification (`semantics.Expected` / `ArchiveVerifier.ValidateSemantics`) still passes.
* Apply the rule to every build that contains at least one mod (it is a crash fix with no visible change otherwise).
  Add one decision line: `[CGF-INSTANCE-003] Kept fish lights alive: FishThinner no longer culls fish that own a light (prevents
  "Unable to find any instance" crashes in Magmer/Volteen draw code when merged rooms place more than 5 of them).`
* Bump `InstanceCompatibility.Revision` (it is part of the profile build cache key in `ProfileInstaller`).

## Tests
Add xUnit tests in `tests/Jamm.Core.Tests/InstanceCompatibilityTests.cs` (follow the existing style, synthetic data only):
rewrite applied on the vanilla text; idempotent; unknown/modded text → unchanged + warning; loop-termination property
(`other.attempts++` still reachable for light fish). If real-file tests exist that use `JAMM_ORIGINAL`, add one that builds a
single-mod plan and checks the merged FishThinner_Other_10 text contains the guard (skipped when env unset).

## Rules
Read AGENTS.md. `dotnet build -c Release` with zero warnings; `dotnet test -c Release` green. Do NOT commit or push.
Real files for optional manual checks: base `~/cgf-files/legacy/2023-08-16_manifest-9039707068725406600/data.win`,
mods `~/cgf-files/mods/pack-inputs/{Abyss,Verdant}.win`. Never write into those paths.
Finish with a short summary of changed files and test results.
