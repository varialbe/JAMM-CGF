# Getting the bytecode (2023) version of Cat Goes Fishing

**Short version:** JAMM needs the *old* GameMaker 1.4 build of Cat Goes Fishing as its base.
You can download it legally from Steam if you own the game. For the known mods the right
build is **depot 343782, manifest `9039707068725406600`** (16 August 2023).

## Why the current Steam build does not work

| | Current Steam build | Mods (The Abyss, Cave Expansion, Colour Expansion) |
|---|---|---|
| GameMaker | 2023.6 (built 5 Aug 2024) | 1.4, bytecode 16 |
| Compiled as | **YYC** (native code inside the `.exe`) | VM bytecode inside `data.win` |
| `CODE` / `VARI` / `FUNC` chunks | **missing** | present (~1,900 code entries) |

With YYC the game logic lives in the 18 MB `Cat Goes Fishing.exe`, not in `data.win`.
There is nothing to merge code into, and the mods cannot even run on that build individually:
a mod `data.win` needs the old bytecode runner (the ~4.5 MB `Cat Goes Fishing.exe` of 2023).

## Which old build matches which mod

When a mod is made with UndertaleModTool, the `GEN8` header keeps the build timestamp of the
game version it was created from. Matching that timestamp against Steam's manifest history:

| Mod | Game build timestamp (GEN8) | Matching manifest (depot 343782, public branch) |
|---|---|---|
| The Abyss | 2023-08-16 17:35 UTC | `9039707068725406600` (seen 2023-08-16 17:38 UTC) |
| Cave Expansion | 2023-08-16 17:35 UTC | `9039707068725406600` |
| Colour Expansion | 2023-08-15 00:02:41 UTC | `1512054085756769140` (`fast` branch, seen 2023-08-15 00:05 UTC) — exact; `2811289735487427888` (public, build 2023-08-14 23:57 UTC) differs by a single code entry |

Verified result with `cgfmm compare` against manifest `9039707068725406600`
(its `data.win` has exactly the same GEN8 timestamp, `1692207312`):

| Mod | Code identical | Changed | Added | Removed |
|---|---|---|---|---|
| The Abyss | 99.45 % (1818/1828) | 10 | 93 | 0 |
| Cave Expansion | 98.85 % (1807/1828) | 21 | 98 | 0 |
| Colour Expansion | 98.85 % (1807/1828) | 20 | 115 | 1 |

`9039707068725406600` is used as the common base. Colour Expansion against its nearest public build
`2811289735487427888`: 98.91 % identical code (19 changed) — the game builds differ by one code entry, so
the common base works; the exact `fast` build `1512054085756769140` can be downloaded if a perfect
three-way base for that mod is ever needed.

Steam splits the game into two depots: **343781** holds shared content (unchanged since 2017 — the
copy from your normal install is fine) and **343782** holds the actual game (`Cat Goes Fishing.exe`
+ `data.win`). Despite SteamDB naming 343782 "Beta", it is what the public branch installs.

## Download it (Steam console, legal for owners)

1. Open the Steam console: `steam://open/console`
   (Flatpak Steam: `flatpak run com.valvesoftware.Steam steam://open/console`).
2. Run **one** download at a time (both go to the same folder):
   ```text
   download_depot 343780 343782 9039707068725406600
   ```
3. Wait for `Depot download complete`. The console prints where the files went. With the
   current Steam client on Linux this is
   `<Steam>/steamrt64/steamapps/content/app_343780/depot_343782/`
   (older clients: `<Steam>/ubuntu12_32/steamapps/content/...`).
   Flatpak Steam root: `~/.var/app/com.valvesoftware.Steam/.local/share/Steam`.
4. Move the **whole folder** to its own place, named after the manifest, e.g.
   `~/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600/`.
   Never copy it over your Steam install.
5. Optional, for Colour Expansion: repeat with `1512054085756769140` (exact) or `2811289735487427888`
   and move that folder to e.g. `.../2023-08-15_manifest-<id>/`.

To find the base for other mods: read the mod's build timestamp with `cgfmm inspect <mod.win>`
and pick the public manifest of depot 343782 seen right after it on
[SteamDB](https://steamdb.info/depot/343782/manifests/) (the full history needs a SteamDB login).

## Verify

```sh
cgfmm inspect  ~/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600/data.win
cgfmm compare  ~/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600/data.win  path/to/mods/*.win
```

`inspect` must show `VM (bytecode available)`, `Bytecode: 16` and timestamp `1692207312`;
`compare` should show about 99 % identical code per mod, as in the table above.

## Running it

The old `Cat Goes Fishing.exe` runs through Proton/Wine. Set the legacy `base` and
`gameDir`, import your mods, select a profile, then run `cgfmm apply` and `cgfmm play`.
See the [six-step quick start](../README.md#cli-quick-start-six-steps)
and [runner settings](data-layout.md) for Wine/Proton configuration.

Do not redistribute game files or depot downloads. Only use builds you own.
