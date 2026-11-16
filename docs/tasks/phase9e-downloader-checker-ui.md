# Phase 9e — robust downloader, honest checker, friendlier "Your mods" and progress (implementer: Codex)

Continue on `ui/browse-mods`. Same rules (no commits/pushes/git config, no tools/ changes). Network enabled.
The orchestrator runs the live end-to-end tests with its probe harness (catalog -> resolve -> download ->
analyze for every Litterbox mod); keep the public Core APIs used by it stable or tell me what changed.

## User requests (German, verbatim)
1. "wir müssen den downloader viel stabiler bauen und die tests sollst du machen und der "checker" funktioniert
   noch nicht richtig bzw die sachen werden nicht gut runtergeladen:
   https://gamejolt.com/get/build?game=839850&build=1700162 ist zb der antiquity link."
2. "und die mods in your mods sollen alle ein kleines icon / bild bekommen und dropdowns sein wo man raufklicken
   kann und kurze summary sieht was alles drin ist. und der progress von dem patch log bzw dieses fingerprinting
   etc soll viel intuitiver und zugänglicher aussehen mit einer progressbar oder so"

## Live probe result (orchestrator, real catalog, 24 mods, base 2023-08-16)
- Works end to end: 40 Colour Expansion, 36 Pondweed Basin, 34 Memory Lane, 30 Oops! All Chaws!, 16 Verdant,
  7 Schizophrenia (download 5-12 s, analysis 10-19 s).
- Previews without file_url: 13 (correct).
- FAILS — unsupported Game Jolt URL shapes:
  - 4 Antiquity: `https://gamejolt.com/get/build?game=839850&build=1700162` (game id + build id given directly).
  - 23 Flora N Flores: `https://gamejolt.com/dashboard/games/1093210/packages/1119262` (game id + package id).
  Support both: derive game/package/build ids and resolve through the same public, read-only Game Jolt website
  endpoints you already use (overview / get-download-url), never invent endpoints; if a build id is given,
  fetch exactly that build.
- 33 mod do artico (`https://gamejolt.com/games/betamoddoarticoCatGoesFishing/1045151`): "did not publish a
  supported .win or ZIP". Inspect what it actually publishes (e.g. .rar/.7z, data.win inside another archive,
  multiple packages, external link) and support it if it is a common archive with exactly one data.win
  (7z/rar only if a managed, safe .NET reader is available without native code execution; otherwise a precise
  message). Same evidence-first approach for 27 Open Silo (itch.io): implement only if itch.io offers a public,
  documented, no-login path for free files; otherwise keep the honest "Open source page" fallback.
- CHECKER BUG: 7 Schizophrenia is shown "Incompatible: ... Compilation failed in merged archive:
  gml_Object_CatalogData_Create_0: Cannot use array literals before GMS2". That is JAMM's own
  decompile->recompile round trip failing (the decompiler emitted array-literal syntax this bytecode-16 compiler
  rejects), not a mod incompatibility. Fix the round trip (decompiler settings for pre-2.3 output, or a narrow
  rewrite of array literals into GMS1-compatible assignments) with a regression test, and make the checker
  distinguish "JAMM could not process this mod (tool limitation)" from "incompatible with your game".
- CHECKER USEFULNESS: every mod is "Unsure — source build differs". Make the verdict informative: Compatible when
  the patch builds+verifies against the configured base and the only difference is the GEN8 build timestamp of a
  2023 legacy build (state the source build in the tooltip); Unsure only when real code differences from a
  different base remain; Incompatible for engine mismatch / unappliable patch. Show the source build date.

## Downloader robustness
Retries with backoff for transient errors (DNS, connect, 5xx, timeouts, truncated bodies), resume with HTTP
Range when the server supports it, re-resolve expired/short-lived signed URLs automatically, per-phase timeouts
and stall detection (no bytes for N seconds), verify declared length, keep the existing safety checks (public
addresses, redirect validation, size limits, HTML-instead-of-file detection, ZIP rules). One download per mod at
a time, deterministic cache by SHA-256, cleanup of partial files on cancel/failure. Clear user messages.
Tests with a fake HTTP handler: flaky connection then success, resume, expiring URL, stall, wrong length.

## UI
- "Your mods": every mod row gets a small icon/image (catalog icon when provenance is known, otherwise a
  neutral generated placeholder) and is an expandable item: click to see a short computed summary of what it
  contains (the ModFacts "Adds" lines, size, source, compatibility icon); toggles stay as they are.
- Long operations (fingerprinting, decompiling, patching, building, verifying, installing, downloading,
  analyzing): replace the raw log as primary feedback with a clear step indicator + progress bar (determinate
  where counts are known, e.g. "Decompiling 812/1,839", indeterminate otherwise) and a plain-language step name;
  keep the detailed log collapsible ("Show details").
Report: docs/reports/phase9e.md. Questions for the user at the top of the final message.
