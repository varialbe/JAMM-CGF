# Catalog origins Round 2 verification

Date: 2026-11-23

## Fixes

- Auto-linking skips built-in mods, and the core linker refuses explicit attempts to link them.
- Catalog versions are compared as semantic versions. Only a strictly newer release is an update.
- Heuristic and explicit links persist their match kind and local archive SHA-256. Same-version local builds are informational; replacing one requires `update --apply --force-replace-local`.
- Added unit coverage for built-in exclusion, heuristic local-build reporting, same-version handling, and explicit replacement eligibility.

## Personal-store trial

Used a metadata-only copy under `/tmp` made from `~/jamm-personal/store/config.json`, profile JSON, catalog cache and each mod's `mod.json`. No archive, game, Proton or compatdata files were copied. Ran `link --auto --profile personal` and `update --check --profile personal` against that copy only.

Auto-link matched these five local mods by normalized name and base SHA-256: harbor-town, endless-ocean, silo, weather-pack and submarine. The four built-ins were explicitly skipped.

Exact `update --check` output:

```text
The Silo (jamm-silo): local build differs from catalog 2.3.0; no newer catalog version is available.
Weather Pack (weather-pack): local build differs from catalog 1.0.1; no newer catalog version is available.
Endless Ocean (Preview) (endless-ocean): local build differs from catalog 0.2.1; no newer catalog version is available.
Submarine (submarine): local build differs from catalog 0.1.0; no newer catalog version is available.
Harbor Town (harbor-town): local build differs from catalog 0.1.0; no newer catalog version is available.
```

## Verification

- `dotnet build -c Release -m:1`: passed with 0 warnings and 0 errors.
- `dotnet test -c Release -m:1`: passed; 694 core tests and 131 GUI tests passed, with 20 real-file tests skipped because their optional inputs were not configured.
