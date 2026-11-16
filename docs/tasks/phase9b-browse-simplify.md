# Phase 9b — Browse mods for normal players: simple, deterministic, auto-detected (implementer: Codex)

Continue on branch `ui/browse-mods` (this worktree). Read AGENTS.md, docs/tasks/phase9-browse-mods.md and
docs/reports/phase9-browse-mods.md first. Do not commit, push or change git config; do not touch `tools/`.
Network access is enabled for public read access to Litterbox/Game Jolt and Steam metadata only.

## User feedback after trying Phase 9 (German, verbatim)
"der browse mods tab funktioniert nicht richtig. außerdem ist er zu cluttered. ich will incht 1:1 die
description sondern eine wirklihc algorithmisch geprüfte angabe: Titel, kurze liste: mit "Adds" und dann zb
x Fish... konkrete deterministische angaben. ein links zur litterbnox page. ein download button. und wenn man
was anklickt bilder. versionen und so soll man im advanced mode auswählen können anostsne wird eif geschaut ob
kompatibel mit aktueller version wenn unsicher: einfach "Unsure" oder "Compatible" oder "Incompatible" oder so
anzeigen aber nicht als text sondern als icon mit kreuz haken oder fragezeichen in gelb und es soll automatisch
die installation vom game dtecten unabhängig ob linux windows oder whatever. der modmanager soll für normies
nutzbar sein"

Answers to orchestrator questions:
- What did not work: "Download/Install geht nicht, "choose a file/release" ist unsinnig zb weil es meist eh nur
  eine version gibt und download & add und download & install is komisch, mach lieber ein button mit "Download"
  der es runterlädt und automatisch aktiviert und einfach ne liste mit toggles wo man klicken kann "Enable" und
  "Disable" mit farben"
- Where deterministic facts come from: analyze on selection — when a mod is selected, download its file in the
  background, analyze it, cache the result locally; until then the card shows that it is being checked.

## Requirements
1. **Find and fix why Download/Install did not work** in the real GUI (reproduce with a temp CGFMM_DATA_DIR and
   the real catalog; document the root cause in the report). Add a regression test.
2. **Simple mode (default)** per mod card: title, compatibility icon (green check = Compatible, red cross =
   Incompatible, yellow question mark = Unsure; icon + tooltip, not a text label), a short deterministic
   "Adds" list, a link to the Litterbox page, one **Download** button. Clicking the card shows its images
   (gallery). No raw description, no release picker, no stats clutter.
3. **"Adds" facts are computed, never copied from text**: on selection resolve + download (bounded, cancellable,
   cached by SHA-256 under the manager data dir), run the existing patch/diff pipeline against the configured
   base and derive concrete counts, e.g. new fish species (objects descending from Fish), new boats, new
   rooms/areas, new sprites/sounds, changed game code entries, catalog entries. Only counts the code can prove;
   omit a line rather than guess. Cache results (keyed by file SHA-256 + base hash + analyzer version).
4. **Compatibility** from facts: Compatible = base hash/GEN8/bytecode match and patch builds cleanly against the
   configured base; Incompatible = different engine (e.g. YYC / no CODE chunk) or patch cannot apply; Unsure =
   not analyzed yet, older/other base build with differences, or analysis failed. Tooltip says why.
5. **Download = download + import + enable**: imports through the existing library (dedupe by hash, provenance),
   adds it enabled to the active profile (create a default profile if none exists). If several files/releases
   exist, choose deterministically (the single `data.win`/zip-with-one-data.win of the newest release); only
   ask when truly ambiguous; the release/file picker lives in **Advanced mode**.
6. **Library/profile list with colored Enable/Disable toggles** (clear colors, e.g. green enabled, grey disabled)
   instead of the current controls in simple mode. One prominent **Play** action that builds/verifies/installs
   when the enabled set changed (keep backups and the one-time confirmation with destination paths; keep
   conflict resolution UI available, default "later mod wins" as today) and then launches.
7. **Advanced mode toggle** (persisted): shows versions/releases/files, full description, load order, conflict
   details, the existing manager controls.
8. **Automatic game detection** (Windows, Linux incl. Flatpak/Snap Steam, macOS if trivial): find Steam roots
   and `libraryfolders.vdf`, `steamapps/common/Cat Goes Fishing`, depot download folders
   (`steamapps/content/app_343780/depot_343782`), Proton/Flatpak paths. Classify each found data.win as legacy
   bytecode (usable base) vs current YYC (not usable) via the existing inspector. First-run: if a legacy build is
   found, offer one click to use it as base and to create the separate modded game copy automatically; if only
   the current build is found, show the short "download the legacy depot" instruction with a copy button.
   Never modify the found original.
9. Normies: plain words, no jargon in simple mode, keyboard/mouse accessible, empty/error states with one clear
   next step.

## Validation
0 warnings, tests for: deterministic fact extraction on synthetic archives, compatibility classification,
auto-detection with fake Steam directory trees and vdf files on each OS layout, Download = import + enable,
toggle semantics, Play-needs-apply logic, regression for the original Download bug. If the sandbox blocks
tests, say so (the orchestrator runs them on the VPS). Manual live check of Colour Expansion (litterbox id 40)
including analysis result (it should report its added fish and content, compatible with base
9039707068725406600 only with the documented caveat that it was made from the 2023-08-15 build).
Report: docs/reports/phase9b.md. Questions for the user (if any) at the top of the final message.
