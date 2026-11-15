# Task: Phase 0 fixes + legacy-version documentation

Read `AGENTS.md` first. Findings come from an independent review of commit 367384d.

## A. Fixes (src/, tests/)
1. **Privacy:** reports and JSON must not contain absolute paths. Store/print the file *name* only
   (keep the full path internally where needed for loading). Regenerate `docs/reports/phase0.md` and
   `docs/reports/phase0-verification.md` without `/home/...` paths. Grep the repo afterwards for `/home/` —
   nothing may remain in committed files.
2. **YYC handling:** when either side of a comparison is YYC, skip code-dependent families
   (code, scripts, variables, functions) and do not print an "% unchanged" verdict; print
   `NOT COMPARABLE: <file> is YYC (no bytecode)` instead. `compare`/`phase0` exit code: 0 when the analysis
   ran (even if a file is YYC), 1 on read errors; only `inspect` returns 2 for YYC (as specified).
3. **Robust fingerprinting:** an unsupported sprite/texture (external texture, Spine, SWF, out-of-range region)
   must not abort the file. Record a warning and use a deterministic marker fingerprint `unsupported:<reason>`.
   Separate load errors ("could not read <file>: …") from analysis errors ("could not analyse <asset> in <file>: …").
4. **Strings / texture pages** must not dominate the verdict: compute "% unchanged" over named assets only
   (code, scripts, sprites, objects, rooms, sounds, fonts, backgrounds) and report strings separately.
5. Rooms: also fingerprint GMS2 layers (instance/tile/background elements) when present.
6. `script.Code` lookups must not throw if the code entry is missing from `data.Code`.
7. Structure for later phases: split `GameFileAnalyzer` into a loader that returns the live
   `UndertaleData` (read-only, caller disposes) and a fingerprinting step on top of it. Replace magic
   asset-family strings with an `AssetKind` enum. Don't keep all decoded texture pages in memory at once.
8. `OutputFile.Validate`: add a parameter that allows writing a new `.win` path (for the future build
   command) while still refusing to overwrite any input file.
9. Update/add tests for 2, 3, 4, 6.

## B. Documentation
1. New `docs/legacy-version.md` (English) — "Getting the bytecode (pre-2024) version of Cat Goes Fishing":
   - Why: the current Steam build (GameMaker 2023.6, built Aug 2024) is compiled with **YYC**; its
     `data.win` contains no CODE/VARI/FUNC chunks, so code mods cannot run on it and cannot be merged.
     All known mods (The Abyss, Cave Expansion, Colour Expansion) are **GameMaker 1.4 bytecode 16** files
     built for the old version, and they need the old `Cat Goes Fishing.exe` runner too.
   - Legal route: Steam can download older builds of a game you own via the Steam console.
     Steps: find a manifest ID for depot **343781** (Cat Goes Fishing Content, app 343780) dated before
     August 2024 on SteamDB (`https://steamdb.info/depot/343781/manifests/`, requires a SteamDB login);
     open the console (`steam://open/console`; Flatpak: `flatpak run com.valvesoftware.Steam steam://open/console`);
     run `download_depot 343780 343781 <manifest-id>`; files appear in
     `<Steam>/ubuntu12_32/steamapps/content/app_343780/depot_343781/` (Flatpak Steam root:
     `~/.var/app/com.valvesoftware.Steam/.local/share/Steam`). Copy that folder to its own location
     (e.g. `~/Games/CatGoesFishing-legacy`), never over the Steam install.
   - Verify with `cgfmm inspect <legacy>/data.win` (must say VM / bytecode 16) and
     `cgfmm compare <legacy>/data.win <mods…>` — the right base shows ~99%+ identical code per mod.
   - Running it: through Proton/Wine (detail to be added when `cgfmm play` exists).
   - Do not redistribute game files; only use builds you own.
2. `docs/reports/phase0.md` gets a short "Conclusion" section at the top summarising the findings above
   (YYC Steam build, mods = GM 1.4 bytecode, mods 93% identical to each other, next step legacy base).
3. README.md: replace the stub with a proper README: name **JAMM-CGF — Just Another Mod Manager for
   Cat Goes Fishing**, short pitch, status/roadmap table (Phase 0 done; Phases 1–4 planned, GUI later),
   "Important: you need the legacy bytecode version" box linking docs/legacy-version.md, requirements
   (.NET 10 SDK, Linux), build steps incl. `git clone --recursive`, all current commands with examples,
   exit codes, project layout, contributing (link AGENTS.md), license GPL-3.0 + credit to
   UndertaleModTool (GPL-3.0). No absolute personal paths. Keep it clean and well formatted; badges for
   .NET 10 and GPL-3.0 are welcome (shields.io).

## Verify
- `dotnet build -c Release` zero warnings, `dotnet test` green (also with JAMM_ORIGINAL/JAMM_MODS_DIR set).
- Re-run `cgfmm phase0` on the real files to regenerate the reports.
- `grep -rn "/home/" -- ':!external'` over tracked files finds nothing.
- Do NOT commit (the orchestrator commits). Finish with a summary of what changed and what didn't work.
