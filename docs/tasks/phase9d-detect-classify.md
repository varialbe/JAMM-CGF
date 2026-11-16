# Phase 9d — classify found games precisely (implementer: Codex)

Continue on `ui/browse-mods`, same rules. Orchestrator ran SteamGameDetection.Detect() and
GameFolderSearch.Scan(GameFolderSearch.Roots()) on the user's real desktop (2.3 s, good). Every bytecode-16
hit was reported as "✓ Legacy game found", including unusable ones:

| Hit | Facts (cgfmm inspect) | Correct verdict |
|---|---|---|
| Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600/data.win | SHA-256 dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1, GEN8 ts 1692207312, 44,572,166 B | ✓ supported original — preselect |
| Games/CatGoesFishing-legacy/2023-08-15_manifest-2811289735487427888/data.win | legacy, other build | ? older legacy build (usable with differences) |
| Games/CGF-modded, CGF-test* | GEN8 ts 1692207312 but 105 MB, folder has data.win.cgfmm-original / data.win.cgfmm-build.json | ✗ "modded copy made by JAMM" (offer: use its data.win.cgfmm-original if it hashes to a supported original) |
| Festplatte 2/Steam/steamapps/common/Cat Goes Fishing/data.win | 56,078,502 B, GEN8 ts 1692057761, no JAMM marker | ? "modified or other build" (not the known original) |
| Downloads/Cat-Goes-Fishing/.../data.win | GEN8 build 1772, ts 1509155170 (2017) | ✗ too old / unsupported version |
| games/Cat.Goes.Fishing.v11.13.2019, v2022.08.15 | older builds | ✗ unsupported version |
| Flatpak Steam common/Cat Goes Fishing | YYC | ✗ current Steam build (already correct) |

Requirements:
- Classify by evidence: known original SHA-256 list (at least the 2023-08-16 manifest 9039707068725406600 hash
  above; add the 2023-08-15 public manifest only if you can derive its hash from files present to the user —
  do not invent hashes; otherwise classify by GEN8 timestamp as "older legacy build"), JAMM markers, GEN8
  timestamp/build, YYC. Three icons: green check (supported original), yellow question mark (usable but not the
  known original — explain), red cross (not usable — explain). Never preselect yellow/red.
- Never offer a JAMM-built data.win as base; if its data.win.cgfmm-original matches a supported hash, list that
  backup as the candidate instead (read-only).
- Hash only candidates that pass cheap checks (size/GEN8) to keep the scan fast; show progress.
- Sort: green first, then yellow, then red; collapse red ones under "Other folders found (not usable)".
- Tests with fixtures for every row of the table (synthetic files, markers, fake hashes via injected hash list).
Report: docs/reports/phase9d.md. No commits/pushes/git config.
