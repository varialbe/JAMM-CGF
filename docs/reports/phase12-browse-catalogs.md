# Browse catalogs and analyzer 6 shared status

Browse loads Litterbox, the Varialbe first-party catalog, and the official Game Jolt project for The Abyss (project 842483). Each feed has independent snapshot caching and failures are shown alongside successfully loaded feeds. Catalog identity is retained in imports and compatibility association. Source checkboxes persist independently; entries without supported downloads remain governed by the existing opt-in filter.

The first-party feed contract is a JSON array of `CatalogMod` objects at `https://varialbe.xyz/cgf-mods/catalog.json`; `../inputs/catalog-contract.md` documents the fields and `../inputs/catalog.json` is the shared empty-array fixture for the silo branch.

Compatibility misses now explain whether the current shared export was unavailable or had no analyzer 6 fact for that entry. The VPS cron scanner is pinned to a separately published analyzer 6 CLI and writes to private `private/review-analyzer6` staging. Its script validates schema 1 and analyzer 6 before replacing that directory, preventing stale scanner output from replacing reviewed v6 exports. No publication path is modified.
