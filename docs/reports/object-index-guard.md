# Object index and inactive fish

The legacy runner evaluates `Bo.object_index` by reading the first active Bo. The bite handler used that read as a type constant, so a larger fish attacking a small hooked fish could crash even when neither fish was a Bo. The modded builder now replaces that expression with the typed Bo asset index.

## Instance lifecycle

The legacy archive places one Bo in each of `NewWorld` and `room11`, at `(19183, 1839)` in both rooms. An active Bo can satisfy the old lookup from anywhere in the room. A camera change matters when it changes which instances are active.

- Perf scheduling skips selected distant Fish End Steps for standard fish. Bo inherits from FishMedium and can qualify. The scheduler keeps the instance active and leaves engine alarms, motion and Draw running. The grid hooks invalidate their cache on existing activation and deactivation calls.
- Vanilla FishThinner deactivates distant fish only while the active count for the selected object exceeds five. It cannot remove the sole placed Bo by itself. The existing light-owner protection remains in effect.
- Vanilla zone deactivation and the Fish End Step can deactivate fish. Smooth freezes active instances between logic ticks and restores its own frozen set before the next logic tick.
- Weather Pack parks eligible free-swimming vanilla surface fish and restores them when the weather ends. Bo's spawn depth is inside the parking band of 1294–1950, and the parking check does not use distance from the camera. Its schools also give the second-bite path more opportunities to run. The earlier guarded personal weather run recorded zero visible vanilla surface fish and nine weather fish in the sunny capture, with 192 parked instances.

The crash proves that Bo had no active instance at the bite. The original error did not record which subsystem last changed Bo's state.

## Build repairs

- `CGF-INSTANCE-004` substitutes a typed constant for a leaf object in type comparisons. The guard preserves parent selectors, whose first active instance can have a child type. Lamp restore uses the Bulbfish asset as a typed value even when no Bulbfish is loaded.
- `CGF-INSTANCE-005` sends eligible scalar reads through `jamm_iv_*` scripts and wraps eligible writes in `instance_exists`. The wrappers preserve the outer statement's `else` binding. The scanner excludes shadowed asset names, strings and comments. It resolves qualified object symbols before testing eligibility.
- `CGF-INSTANCE-007` uses the same accessors for simple unchecked instance lookup reads. Accessors reject negative IDs before reading a member. Typed fields and chained accesses need their own semantics.
- `CGF-INSTANCE-006` protects Chook's state handoff, including the alarm write, as a unit. Thermo falls back to following the school when no Magmer is available. Torby's ghost draws with the object sprite when no Torby can supply an instance sprite. Snobfish can look for a Nightfish when its stored target is unavailable. Flash validates its stored navigation node and tries the nearest node when necessary. The Maize and shared navigation repair remains under `CGF-NAV-001`.

Final-layer guards run on modded builds. A vanilla-only build copied the legacy archive byte for byte. Both files had SHA-256 `dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1`.

After merging local `main` at `d9d9353` into the experiment branch, the Release build passed with zero warnings. The standard suite passed 565 core and 129 GUI tests, with 17 opt-in tests skipped. A separate run passed the real-archive FishThinner check and four base-mod tests.

## Archive audit

`cgfmm audit instances <archive> --base <legacy-archive> --json <report.json>` reads decompilable entries and uses the original archive to identify added objects. The table counts scanner findings and the subset with automatic repairs. A `with (Object)` selector is recorded separately because the runner simply skips its body when no instance exists. The scanner handles recognizable assignments and dereferences within a code entry. It cannot prove a dynamic ID is live across arbitrary events.

| Archive | Findings | Guarded | Safe `with` selectors |
|---|---:|---:|---:|
| Vanilla | 209 | 208 | 254 |
| Cave Expansion | 217 | 216 | 254 |
| Colour Expansion | 225 | 224 | 254 |
| The Abyss Beta | 225 | 224 | 256 |
| Abyss (pack input) | 225 | 224 | 256 |
| Antiquity (pack input) | 223 | 222 | 260 |
| Colour (pack input) | 225 | 224 | 254 |
| Verdant (pack input) | 209 | 208 | 254 |
| PWB | 215 | 214 | 255 |
| TGE | 287 | 264 | 272 |
| ModKit Endless Ocean | 209 | 208 | 256 |
| ModKit Example Fish | 209 | 208 | 254 |
| ModKit Example Rod | 209 | 208 | 254 |
| ModKit Harbor Pack | 209 | 208 | 254 |
| ModKit JAMM Console | 209 | 208 | 258 |
| ModKit JAMM HD Lite | 209 | 208 | 254 |
| ModKit JAMM Perf | 209 | 208 | 259 |
| ModKit JAMM Smooth | 209 | 208 | 316 |
| ModKit Showcase Upgrades | 209 | 208 | 255 |
| ModKit Silo | 209 | 208 | 256 |
| ModKit Weather Pack | 209 | 208 | 254 |

## VPS matrix

The personal profile uses Perf, HD Lite, Console, Smooth, Silo, Weather Pack and Endless Ocean. Public-5 uses Abyss, Antiquity, Cave, Colour and Verdant. All-7 adds PWB and TGE. The two packs include Perf, Silo, Weather Pack and Endless Ocean as benchmark fixtures so they can run the same scenes.

The stress scene enables Perf, adds fish, selects small fish from the sunny school, hooks them, and drives a larger fish through the native lunge and bite path. Each cycle deactivates the active Bo instances and reactivates them after the attack. The CSV measures active Bo at start, absent-Bo cycles, reactivations, confirmed school bites, scheduler skips and elapsed time. The scene runs for at least 180 seconds after initialization.

Runs use the VPS launcher at `~/jamm-night/bin/run.sh` and its shared game lock. Set `DUR=200` for stress because the launcher passes its duration to ModMaker. Raw audits and build logs are in `~/jamm-night/n21/object-guard.IEINQy/`. Run directories are under `~/jamm-night/runs/` unless otherwise indicated.

The first personal stress trial reached gameplay and found that the benchmark's new Grumper could step before Rod initialized its lunge multiplier. The fixture now initializes that value for every fish it creates. The runner's clipboard capture was empty, so the fatal error was recovered from the game log and saved in `~/jamm-night/runs/ig-object-guard.IEINQy-personal-r1-stress/error.txt`.

| Scene | Personal | Public-5 | All-7 |
|---|---|---|---|
| Dock | Recorded, 201 frames (`guard3-dock`) | Unverified | Earlier build recorded, 110 frames (`all7-final-dock`) |
| Deepwater | Earlier guarded build recorded, 214 frames (`guard2-own-deepwater`) | Unverified | Earlier build recorded, 36 frames (`n21-all7-deepwater`) |
| Silo | Recorded through all four catches (`guard3-silo`) | Unverified | Earlier Silo build recorded through save (`silo-v22-all7-silo`) |
| Weather | Recorded all eight captures (`guard3-weather`) | Unverified | Earlier Weather build recorded all eight captures (`~/jamm-night/weather/screens/verified-all7/`) |
| Worldgen | Earlier guarded build recorded through return (`interim2-worldgen`) | Unverified | Unverified |
| Stress | Recorded, 200.097 s (`guard3-stress`) | Running | Recorded, 200.177 s (`all7-guard-stress`) |

Earlier runs document their own builds. The archived runs labeled public-5 in the Silo and Weather reports used a different five-pack, so they do not verify the five public inputs listed here.
