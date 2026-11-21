# Static Decoration Sleep (`DecorSleep`)

Branch: `exp/decor-sleep`

## 1. Analysis (Step 1)

### 1.1 Profiler hotspots in `all7`, `pub5`, and `vanilla` (dock & deepwater)

From the existing profiler captures (`~/.var/app/com.valvesoftware.Steam/data/JAMM-CGF/bench-runs/m3-prof-*/jamm_profile_*.csv`), GameMaker 1.4 dispatches `Step`, `End Step`, `Draw Begin`, and `Draw` events for every active / visible instance across the entire `100,000 x 13,000` `NewWorld` room every tick, regardless of whether an instance is inside `view[0]`:

| Code entry | Vanilla dock (calls/tick, share) | Pub5 dock (calls/tick, share) | All7 dock (calls/tick, share) | All7 deepwater (calls/tick, share) |
|---|---:|---:|---:|---:|
| `gml_Object_CaveRock_Step_0` | — (absent) | — (absent) | 3,836 / 9% (17,969 µs) | 3,836 / 10% (21,890 µs) |
| `gml_Object_SceneItem_Step_2` | 366 / 1% (397 µs) | 2,024 / 4% (7,956 µs) | 2,880 / 8% (14,957 µs) | 2,880 / 9% (17,948 µs) |
| `gml_Object_SceneItem_Draw_0` | 366 / <1% (129 µs) | 2,024 / 3% (6,646 µs) | 2,558 / 6% (11,583 µs) | 2,551 / 7% (14,126 µs) |
| `gml_Object_Weed_Step_0` | 246 / <1% (156 µs) | 782 / 1% (2,806 µs) | 1,197 / 3% (5,553 µs) | 1,197 / 3% (6,772 µs) |
| `gml_Object_AbstractLight_Draw_0` | 90 / <1% (68 µs) | 693 / 1% (2,304 µs) | 889 / 2% (3,769 µs) | 889 / 2% (4,533 µs) |
| `gml_Object_Rock_Step_2` | 55 / <1% (15 µs) | 232 / <1% (737 µs) | 472 / 1% (2,064 µs) | 472 / 1% (2,530 µs) |
| `gml_Object_Dangler_Step_0` | 61 / <1% (37 µs) | 234 / <1% (835 µs) | 329 / 1% (1,529 µs) | 329 / 1% (1,862 µs) |
| `GrottoObjectStep` + `GrottoObjectDraw` | — | — | 178+178 / <1% (1,619 µs) | 178+178 / 1% (1,967 µs) |
| `gml_Object_Sea_Step_0` + `NoDrawCodeLit_Draw_0` | 100+102 / <1% | 139+142 / <1% | 139+142 / <1% (1,429 µs) | 139+142 / <1% |
| `gml_Object_Light2_Draw_0` | ~1 / <1% | 131 / <1% (436 µs) | 131 / <1% (621 µs) | 131 / <1% |
| `FrontStep` (`GrottoFrontgroundrock_Step_0`) | — | — | 101 / <1% (494 µs) | 101 / <1% |
| `CrystalAmethystStep` + `CrystalsStep` + `Lightstuff1step` | — | — | 59+59+33 / <1% (848 µs) | — |

Note that `gml_Object_SceneItem_Draw_0` in `all7` drops from 2,880 to ~2,558 calls/tick only because PWB's `Dangler_Step_0` sets `visible = 0` when `point_distance(x, y, Camera.x, Camera.y) > 5000` (and omits setting `visible = 1` in the `else` branch). Meanwhile `CaveRock` in `all7` inherits a TGE/PWB `CaveRock_Step_0` polling loop (`checkTimer` modulo 5 -> `point_distance > 5000 -> visible = false/true`), which cuts default `Draw` on off-screen rocks in `all7` at the cost of 3,836 `Step_0` dispatches/tick; in `vanilla` and `pub5`, `CaveRock` has no `Step_0`, so all 3,149 `CaveRock` instances in `pub5` run default sprite drawing every tick.

---

### 1.2 NewWorld instance counts & mod attribution

Room placements in `NewWorld` (`total_placed`: vanilla 2,266; pub5 8,297; all7 11,004):

| Family / Object | Vanilla | Pub5 | All7 | Runtime after RoomStart (All7) | Primary mod contributors |
|---|---:|---:|---:|---:|---|
| **`CaveRock` hierarchy** | **311** | **3,149** | **3,762** | **3,836** | Base + all 7 packs |
| `CaveRockBig` | 272 | 2,275 | 2,529 | 2,529 | Cave (+889), Abyss (+515), Antiquity (+267), Colour (+188), TGE (+186), PWB (+63) |
| `CaveRockWedge` | 5 | 29 | 132 | 132 | TGE (+97), Cave (+12), Colour (+8), PWB (+5), Abyss (+1) |
| `CaveRockBigCrystal` | 0 | 117 | 117 | ~168 | Antiquity (+117) + spawned by `GlowCrystal_Create_0` |
| `RotOrbHostile1` | 0 | 92 | 92 | 92 | Verdant (+92) |
| `CaveRockTee` | 5 | 43 | 87 | 87 | TGE (+28), PWB (+16), Antiquity (+11), Colour (+11), Abyss (+7), Cave (+6), Verdant (+1) |
| `BlackWedge` / `BlackTear` / `BlackTee` | 0 | 0 | 112 | 112 | TGE (+86/+20/+6) |
| `CaveRockSig` | 4 | 72 | 85 | 85 | Cave (+20), Antiquity (+18), Colour (+17), PWB (+13), Verdant (+11), Abyss (+2) |
| `CaveRockLeg` / `CaveRockTear` / `CaveRockWide` | 13 | 155 | 228 | 228 | TGE, Colour, Cave, Verdant, PWB, Antiquity, Abyss |
| `Magma_Rock` / `AbyssRock` / `BigRockHell` / `BigRockRadianite` / `BigRockHS` / `BigRockThorns` | 0 | 298 | 298 | 298 | Abyss (`Magma_Rock` 78, `AbyssRock` 47), Antiquity (`BigRockHell` 71, `Radianite` 49, `HS` 29, `Thorns` 24) |
| `BreakableRock*` / `Meteor` / `MissileSilo` / `Fossil*` | 12 | 26 | 33 | 33 | Vanilla + Antiquity/Cave/TGE |
| **`SceneItem` hierarchy** | **366** | **2,024** | **2,880** | **2,880** | Base + all 7 packs |
| `Weed` (`Weed` + `Lush_Weed` + `JWeed*` + `Flowerweed*` + `*coral`) | 246 | 782 | 1,197 | 1,197 | TGE (+247 `Weed`), Cave (+213 `Weed` +37 `JWeed`), PWB (+110 `Lush_Weed`, +47 `Weed`), Verdant (+91 `Weed`), Antiquity (+78 `Weed`, +19 `JWeed`), Abyss (+84 `JWeed`), Colour (+39 `Flowerweed`/`*coral`) |
| `Dangler` (+ `DanglerFG`) | 61 | 237 | 334 | 334 | TGE (+95/+2), Cave (+73), Antiquity (+50), Verdant (+34), Abyss (+14/+3) |
| `Abyss1..6` | 0 | 351 | 351 | 351 | Abyss (`Abyss6` 209, `Abyss4` 37, `Abyss3` 35, `Abyss2` 33, `Abyss1` 31, `Abyss5` 6) |
| `GrottoFrontgroundrock` | 0 | 0 | 101 | 101 | TGE (+101) |
| `backgroundhell1..5` | 0 | 101 | 101 | 101 | Antiquity (`backgroundhell5` 95, `1..4` 6) |
| `CoralDark` / `CoralFG*` / `Coral*` / `RotOrbSafe` | 24 | 258 | 299 | 299 | Verdant, Cave, Antiquity, PWB, Abyss, Colour, TGE |
| `Lush_Lilac` / `Lush_Coral` / `LushRock_Weeds` | 0 | 0 | 146 | 146 | PWB (+67/+66/+13) |
| `GlowCrystal` / `Radianite` / `GlowPlant` / `BulbPlant*` / `ChainPlant` / `crystal3` / `crystal4` / `Crystal_Yellow` | 25 | 145 | 150 | 150 | Antiquity (+51/+26), Cave (+13/+4/+12), Vanilla+mods |
| `WeedHell` / `CoralHell` / `RockFG` / `RockSpike` / `CaveFG*` / `Grass` / `Thorns*` / `Spike` | 11 | 137 | 183 | 183 | Antiquity, TGE, PWB, Cave |
| **`Rock` hierarchy** (parent `Collidable`) | **55** | **232** | **472** | **472** | TGE (+151), PWB (+83), Colour (+13), Cave (+20), Antiquity (+5), Verdant (+52 `Rock*`+33 `Rosebush`? Abyss +29 `Steel_Stalk`, +33 `Rosebush`, +16 `Blooddrop`, +11 `Astral`) |
| **`GrottoObject` hierarchy** | **0** | **0** | **178** | **178** | TGE (`GrottoWall*` 58, `GrottoRock1..4` 95, `BackgroundLeaf` 15, `BackgroundSand` 10) |
| **`NoDrawCodeLit` hierarchy** (`Sea`, `Cliff`, `CliffBottom`) | **3** | **4** | **4** | **142** | `Sea` spawns 139 tiles across `room_width` at `Create_0` |
| **`AbstractLight` hierarchy** | **0** | **0** | **49** | **1,054** | Placed (`Lightstuff1` 33, `Lightstuff2yellow` 16) + spawned dynamically by fish, plants, crystals, `MagmaVent`, `Firefly`, `Buoy`, `Lure` |
| Other unparented decor (`Lush_Tree`, `GrottoRock1Front`, `CaveDripstone`, `BackgroundLightBeam`, `BGLightBeam`, `Crystal*`, `Bubble*`, `Lilypad`, `WaterCurrent`, `Stream`) | 0 | 14 | 390 | ~390+ | PWB (`Lush_Tree` 65), TGE (`GrottoRock1Front` 60, `CaveDripstone` 59, `Lilypad` 51, `Crystal*` 59, `BackgroundLightBeam`/`BGLightBeam` 47, `WaterCurrent` 30, `Stream` 6), Cave (`Bubble*` 14) |

---

### 1.3 Per-object event semantics, time/view/gameplay dependencies, and wake catch-up

1. **`CaveRock` (`CaveRock` and static children)**:
   - *Events*: Vanilla/pub5 have only `Create_0` (`image_speed = 0;`) and default sprite draw. TGE/PWB add `CaveRock_Step_0` (`checkTimer += 1; if (checkTimer == 5) { checkTimer = 0; visible = point_distance(x, y, Camera.x, Camera.y) <= 5000; }`).
   - *Gameplay dependencies*: `CaveRock` inherits `Collidable`, which is queried by `collision_point`/`collision_line`/`position_meeting` in fish wall-avoidance (`fish_avoid_wall*`, `fish_standard_step2`), `check_lunge`, `check_lunge_catswim`, `Lure`, `Gear`, `Hookable`, `CatSwim`, `NavBrain`, `Batfin`, `Smarty`/`Bulbeater`/`Sirevine`/`Bloodshark` (`instance_nearest(..., CaveRockBig)`), `MagnapinnaEndstep` (`CaveRock.x`), and `SonarPanel_Draw_0` (`with (CaveRock)`). `EndlessOceanMod` (`jamm_chunk_stream`) also spawns and destroys `CaveRockBig` by ID.
   - *Exempt children*: `BreakableRock` (`BreakableRockA/B/C/D`, `Meteor`) uses `Alarm_0/1/2` + `Other_10` to crack/destroy and `SaveManager_Other_14` checks `instance_exists(BreakableRockA/B/C)` for saves; `MissileSilo` (`canOpen`, read by `Silo`, `Magnalav`, `Noxius`).
   - *Sleepability*: **Cannot be deactivated** (`instance_deactivate` would break off-screen collision, `instance_nearest`, `SaveManager`, `SonarPanel`, and `jamm_chunk_stream`). **Can** safely toggle `visible = false` off-view (skipping default Draw in vanilla/pub5/all7) and skip/replace `CaveRock_Step_0`'s per-instance polling! Purely stateless visibility (`visible` = on-view).

2. **`Rock` (`Rock`, `Rock2..7`, `Rockbed`, `Steel_Stalk`, `Rosebush`, `Blooddrop`, `Astral`, `Sign`)**:
   - *Events*: `Rock_Create_0` sets `depth`, `home_x`, `home_y`, `para_scale = 1`. `Rock_Step_2` is completely empty (0 instructions). `Rockbed_Step_0` calls empty `action_inherited()`. Default sprite draw.
   - *Gameplay dependencies*: Inherits `Collidable`; queried by `Lure_Step_2`, `Gear_Other_10`, `Hookable_Other_10`, and `SonarPanel_Draw_0` (`with (Rock)`).
   - *Sleepability*: **Cannot be deactivated**; **can** safely toggle `visible = false` off-view (skipping default Draw).

3. **`SceneItem` (`SceneItem` and pure visual children: `Abyss1..6`, `backgroundhell1..5`, `CoralDark`, `CoralFG*`, `Lush_Coral`, `Lush_Lilac`, `LushRock_Weeds`, `RockFG`, `RockSpike`, `CaveFG*`, `WeedHell`, `CoralHell`, `CoralAlien*`, `Grass`, `Spike`, `Thorns*`, `SecretRock`, `sharkskele`, `Volcano2`, `Ramen`, `Sign_obj`, `TextSign*`, `BGL*`, `RotOrbSafe`)**:
   - *Events*: `SceneItem_Step_2` computes parallax `x = home_x + (Camera.x - home_x) * scene_depth * para_scale`, `y = home_y + (Camera.y - home_y) * scene_depth * 0.1 * para_scale`. `SceneItem_Draw_0` skips `view_current == 2` and otherwise calls `draw_self()`.
   - *Time/state dependence*: **Pure stateless function** of `(home_x, home_y, depth, para_scale, Camera.x, Camera.y)`. Zero drift when slept off-view; running `Step_2` once on wake snaps `(x, y)` to the exact frame-accurate coordinate.
   - *Special children*:
     - **`Coral`**: `Fish_Create_0` calls `instance_nearest(x, y, Coral)` to initialize `bombed_x`, `bombed_y`. Must stay **active** (not deactivated); `Draw_0` can still be skipped off-view via `visible = false`, and keeping `Step_2` active on those 23 instances keeps `bombed_x`/`bombed_y` exact.
     - **`GrottoFrontgroundrock`**: `FrontStep` (`Step_0`) lerps `image_alpha` toward `0.15` when `Lure.y > 1295` else `1.0`. Needs either catch-up on wake (`image_alpha = target` or power-lerp) or stay exempt from Step skip; keeping active with `visible = false` off-view skips `Draw_0`, and catching up `image_alpha` via `target_alpha + (image_alpha - target_alpha) * power(1 - 0.08, ticks_slept)` is exact!
     - **`GlowCrystal`, `GlowPlant`, `Radianite`, `BulbPlantShort`, `BulbPlantLong`, `ChainPlant`, `crystal3`, `crystal4`, `Crystal_Yellow`**: Have `Draw_72` (`DrawBegin`) syncing `light` and `light2` (`x = other.x; y = other.y; ...`). Note `visible = false` also skips `Draw_72`, which is safe as long as the owned lights are also off-view (or `Draw_72` runs on wake before `Draw_0`).
     - **`SonarPanel_Draw_0`**: Executes `with (SceneItem)` using `home_x, home_y` when `SonarPanel.active && SonarPanel.shouldDraw`. Note: `SonarPanel_Draw_0` does NOT check `visible`, so `visible = false` does NOT affect Sonar, whereas `instance_deactivate` WOULD hide `SceneItem` from Sonar unless reactivated whenever `SonarPanel` is active (or `SceneItem` is kept active and uses `visible = false` + early-exit!).

4. **`Weed` (`Weed`, `Lush_Weed`, `JWeed*`, `Flowerweed*`, `Scoral`, `Mcoral`, `Lcoral`) and `Dangler` / `DanglerFG`**:
   - *Events*: Inherit `SceneItem_Step_2` and `SceneItem_Draw_0`. `Weed_Step_0` / `Dangler_Step_0` / `DanglerFG_Step_0` compute `image_angle = sin((current_time + jitter [+ x]) / 1000) * 10`. (PWB adds an extra `point_distance > 5000` check in `Weed_Step_0` and a buggy `visible = 0` without restore in `Dangler_Step_0`).
   - *Time/state dependence*: **Pure stateless function** of `current_time`, `jitter`, and `x`. Zero jump on wake!

5. **`GrottoObject` (`GrottoWall1/2/3/5`, `GrottoRock1..4`, `BackgroundLeaf`, `BackgroundSand`) and `BackgroundLightBeam` / `BGLightBeam`**:
   - *Events*: `GrottoObjectStep` computes parallax `x = home_x + (Camera.x - home_x) * para_x; y = home_y + (Camera.y - home_y) * para_y`. `GrottoObjectDraw` skips `view_current == 2` and calls `draw_self()`. `BackgroundLeafStep` adds `image_angle = sin((current_time + jitter + x) / 1000) * 9`.
   - *Time/state dependence*: **Pure stateless functions** of `(home_x, home_y, Camera.x, Camera.y, current_time)`. Zero gameplay references.

6. **`AbstractLight` (`AbstractLight`, `Light`, `Light2`, `BulbPlantLight`, `ChainPlant*Light`, `*Light`)**:
   - *Events*: `Draw_0` draws additive glow for `view_current == 0` and resets `image_blend = sub_blend` (note `Light2_Draw_0`, `BulbPlantLight_Draw_0`, `ChainPlant*_Draw_0` only reset `image_xscale`/`image_yscale`/`image_blend`). `LightingSystem_Draw_0` loops `with (AbstractLight) { if (visible) ... }` to render subtractive light into `light_surface` (`view_current == 0`) and `mini_surface` (`view_current == 1`).
   - *Gameplay dependencies*: Many lights are held by instance ID in `light` / `light2` / `my_light` fields on fish, lures, plants, crystals, fireflies, and minebombs (e.g., `Bulbfish`, `Volteen`, `Magmer`, `Lightbulb`, `Crystal*`, `MinebombHead`). **Cannot be deactivated** (deactivation breaks `instance_exists(light)` and `instance_destroy(light)`).
   - *Special cases*: `CrystalfishLight_Step_0` writes `visible = instance_exists(Lightbulb)` (must NOT have `visible` overwritten!). `FireflyLight_Step_0` uses `random_range` (uses RNG -> exempt Step). `Minebombheadlight_Step_2` tracks `head_id` and `Lure` distance. `CrystalLight1..5` + `Lightstuff1/2/3` (`CrystalsStep` / `Lightstuff*step`) compute `image_xscale`/`image_yscale`/`image_alpha` as `f(current_time)` and increment `image_angle += 0.3` per tick (catchable on wake via `0.3 * ticks_slept`).
   - *Sleepability*: Setting `visible = false` when outside all active views (`view[0]` + `view[1]` if enabled) + sprite margin eliminates BOTH `*_Draw_0` AND `LightingSystem_Draw_0`'s `draw_self()` for ~1,000 off-view lights!

7. **`NoDrawCodeLit` (`Sea`, `Cliff`, `CliffBottom`) and `CrystalAmethyst/Ruby/Diamond/Emerald/Amber/Salt/Dark/Light/Pink/BrightPink/Lapis/RubyBright`**:
   - `Sea`: 139 instances across `room_width`, each 1,025 px wide. `NoDrawCodeLit_Draw_0` draws day/night sprite; setting `visible = false` when off-view skips ~136 off-view `Draw_0` calls/tick. `Sea_Step_0` advances wave `phase` with `Girl.DELTA_INSTANT * Girl.time_scalar` and sets `background_color` (also set by `ParallaxBackground_Step_0`).
   - `CrystalAmethyst`..`CrystalRubyBright` (`CrystalAmethystStep`): 59+ instances; Step copies `x, y` to `light.x, light.y`. Since they are static, skipping Step off-view (while keeping `visible` updated) is safe once `light` is placed.

8. **Explicitly Exempted Objects (gameplay / RNG / particles / controllers)**:
   - `BreakableRock*`, `Meteor` (alarms, `Other_10`, `SaveManager_Other_14` existence checks)
   - `MissileSilo` (`canOpen`, Silo mod, `Magnalav`, `Noxius`)
   - `Hookable` (`BombRock`, `Spikefruit`, `Dried_Kelp`, `Egg`, `SmileyEgg`, `Book` — hookable by `Lure`)
   - `Lilypad` (`collision_rectangle` with `Lure`, `random_range`, `effect_create_below`)
   - `WaterCurrent`, `Stream` (`Lure_Step_0` `with`/`place_meeting`, particle systems + `random`)
   - `Minebombspawn`, `MinebombHead`, `MinebombChain`, `Minebombheadlight` (minebomb chain physics, `random_range`, Lure interaction)
   - `FireflySpawner`, `Firefly`, `FireflyLight` (`random_range` RNG, spawning)
   - `Coral` (`Fish_Create_0` `instance_nearest(x, y, Coral)`)
   - `CrystalfishLight` (`Step_0` controls `visible = instance_exists(Lightbulb)`)
   - `CaveRockBig` / `GeigerLight` owned by `jamm_chunk_stream` / Areas: never deactivated; `visible` culling off-view preserves `instance_exists` and `instance_destroy(id)`.

### 1.4 Largest sprite / interaction radii & safe margin

Examining every non-Fish placed sprite and every `AbstractLight` sprite in `all7`:
- Largest placed static sprite half-extent: `Water1` (`1000x750`, `sx=10`, half-width 10,000 — static with no Step/Draw), `CliffBottom` (`790x1000`, `sy=3.26`, half-height 3,260), `JWeedHuge` (`241x2000`, `org=(130, 1976)`).
- Largest `AbstractLight` sprites: `Boat8Light` (`2000x1200` at `scale=2` -> 4,000x2,400), `MaskfishLight` (`1900x950`), `LanternfishLight` (`1300x800`), `MagmaLight` (`1253x508` at `scale=2`), `Parastone_Light` (`1200x650` at `scale=2`), `Light`/`Light2` (`500x500`, scaled up to `4x` = 2,000x2,000 in bench).
- Largest mod view culling radius already used in `all7`: `5,000` px from `Camera` center (`CaveRock_Step_0`, `Weed_Step_0`, `Dangler_Step_0`); `SonarPanel_Draw_0` uses `8,000` px from `Camera` (when `SonarPanel` is active, and ignores `visible`).
- **Safe culling test**: Instead of a fixed small radius around `x, y`, compute each instance's actual scaled sprite extent (or bounding box) plus a generous view margin (`>= 1024` px beyond `view[0]` edges, and `view[1]` when `view_visible[1]` is true). That guarantees sprites like `CliffBottom`, `JWeedHuge`, `MagmaLight`, or zoomed `view_wview[0]` (up to 2x with `LensCap` / `ZoomBox`) never pop in at the screen edge!

---

## 2. Design & Method Choice (Step 2)

### 2.1 Architecture & Generic Build-Time Analysis

`DecorSleep` (`src/Jamm.Core/Builtins/DecorSleep.cs` and `src/Jamm.Core/Builtins/DecorSleepAnalysis.cs`) is integrated into the JAMM Perf profile build path (`PerfSettings.DecorSleep = true` by default in profiles; `StartEnabled = false`, `FishGrid = false`, `Interval = 4`). Following the `CGF-PERF-REGRESS-001` rework, `BuiltinMods("perf")` (`GeneratorVersion = "8"`) and `RecipePackage("perf")` inject no per-frame code on their own (`applyFishGrid: false, applyScheduler: false, applyCoreOptimizations: false, applyDecorSleep: false`), and `ArchiveBuilder` installs `DecorSleep` only when `perfSettings?.DecorSleep == true` (never in plain `perfSettings == null` / defaults-off builds, keeping `vanilla + builtin-perf` byte-identical). When installed, it is also configurable at runtime via `[Perf] DecorSleep=1|0` in `jamm_perf.ini` (and CLI `cgfmm profile perf <name> decor on|off`), independent of F8.

At build time, `DecorSleepAnalysis.Analyze(UndertaleData)` inspects object hierarchies, room placements, external references, and GML event handlers generically so modded decor objects are covered automatically:

* **Eligibility**: Covers `DecorRoots` (`CaveRock`, `Rock`, `Collidable`, `SceneItem`, `AbstractLight`, `GrottoObject`, `NoDrawCodeLit`, `BackgroundLightBeam`, `LandscapeAbs`, `Bubble`) and unparented static placed objects with visible sprites, excluding `NonDecorRoots`, persistent/controller objects, and objects with `Alarm`, `Collision`, `Other`, GUI Draw, or input events.
* **Coverage counts**:
  * `vanilla.win`: 83 objects covered (`28 deactivatable, 55 active-guarded`), 4 Step handlers guarded, 20 sweep roots.
  * `pub5.win`: 169 objects covered (`74 deactivatable, 95 active-guarded`), 4 Step handlers guarded, 57 sweep roots.
  * `all7.win`: 237 objects covered (`103 deactivatable, 134 active-guarded`), 14 Step handlers guarded, 37 sweep roots.
* **Exemptions**: Skips `BreakableRock*`/`Meteor` (alarms + `SaveManager` existence checks), `Coral` (`Fish_Create_0` `instance_nearest`), `CrystalfishLight` (`Step_0` `visible = instance_exists(Lightbulb)`), `MissileSilo`, `CrystalRubyHook`, RNG/particle objects (`Lilypad`, `WaterCurrent`, `Stream`, `Firefly*`, `Minebomb*`), and `jamm_chunk_stream` targets.
* **`[CGF-PERF-DECOR]` log lines**: Emits summary counts, grouped covered families (`Step(CameraCullOnly)`, `Step(PureParallax)`, `Step(TimeSway)`, `Step(AngleAccumPulse)`, `Step(LightSync)`, `Step(AlphaLerpLure)`, `Visible`), and skipped child reasons.

### 2.2 Method Choice (Active-Guarded vs. Bounded Region vs. Per-Instance Deactivation)

We measured three designs on `all7` before locking the final implementation:

1. **Active-guarded only (`Method A`)**: Keeps all instances active, toggles `visible = false`, and prepends early-exit Step guards. Eliminates `SceneItem_Draw_0`, `NoDrawCodeLit_Draw_0`, `Light2_Draw_0`, and `GrottoObjectDraw`, reducing `all7 dock` mean `loop_us` from `53.10 ms` to `47.51 ms` (`p50: 50.93 -> 45.64 ms`). However, GM1 still dispatches `SceneItem_Step_2` (2,880/tick), `Weed_Step_0` (1,197/tick), `Dangler_Step_0` (329/tick), and `GrottoObjectStep` (178/tick) on active instances.
2. **Coarse `instance_activate_region` (`v1` deactivation)**: Deactivating objects by object index and calling `instance_activate_region(...)` around `view[0]` accidentally reactivated vanilla zone-deactivated gameplay objects (`LilypadStep`, `Minebombchainstep`, `SquiddleTentacleendStep`), regressing `all7` deepwater `--profile` loop from `167.8 ms` (`mA`) to `196.2 ms`.
3. **Final choice — Hybrid Per-Instance Deactivation + Active-Guarded Visibility Culling (`Method B`)**:
   * Pure-visual childless `DeactivationSafe` objects (`SceneItem`, `Weed`, `Dangler`, `GrottoObject`, `BackgroundLightBeam`, `NoDrawCodeLit`, `Landscape*`) are slept individually via `instance_deactivate_object(inst)` and woken individually via `instance_activate_object(inst)`. World-space parallax coordinates (`hx`, `hy`, `px`, `py`) and sprite bounds (`sw`, `sh`) are cached in controller arrays so deactivated instances are never touched until woken, and vanilla-deactivated gameplay objects are never disturbed.
   * Gameplay-queried objects (`CaveRock*`, `Rock`, `Collidable`), `AbstractLight`, and light-spawning parents (`GlowPlant`, `BulbPlant*`, `ChainPlant`, `GlowCrystal`, `CrystalAmethyst..Diamond`) remain active (`DeactivationSafe = false`) and cull `visible` off-view plus guard `CameraCullOnly`/`LightSync` Steps cleanly.
   * `gml_Script_light_anchor` is patched so sleeping fish lights stay `visible = false` after `Fish_Step_2` until they re-enter view bounds.
   * `SonarPanel_Draw_0`'s `with (SceneItem)` only reads instances `< 8,000` px from `Camera` when Sonar is active; `jamm_ds_sweep` wakes `SceneItem` within `8,200` px of `Camera` whenever `SonarPanel.active && SonarPanel.shouldDraw`.
   * Camera-jump catch-up runs in `DrawBegin` (`view_current == 0`, controller `Visible = true`) so it executes after all `EndStep` camera updates (`CameraEndStep` and benchmark/mod camera overrides), preventing mid-tick view ping-pong.
   * Controlled `all7 dock` comparison (`ds4-all7-*`): **`Method B` (`45.87 ms` mean, `42.84 ms` p50, `20.37 FPS`) outperformed `Method A` (`47.51 ms` mean, `45.64 ms` p50, `19.59 FPS`)** with 3,156 fewer active instances.

### 2.3 Sweep Parameters & Wake Catch-Up

* Bounded sweep batch: `512` instances/tick (`BatchSize = 512`), expanding to a full sweep on room load, Sonar activation, or camera jump `> 350` px / view size change `> 100` px.
* View hysteresis & extents: `WakeMargin = 1600`, `SleepMargin = 2000` plus per-instance `max(abs(sprite_width), 256)` and `max(abs(sprite_height), 256)`, checked against both `view[0]` and `view[1]` (Minimap when enabled).
* Wake state catch-up: parallax `(x, y)` snapped from `home_x, home_y` before draw; `TimeSway` (`sin((current_time + ...))`) is wall-clock stateless; `AngleAccumPulse` (`CrystalLight1..5`, `Lightstuff*`) catches up via `image_angle += rate * dt`; `AlphaLerpLure` (`GrottoFrontgroundrock`) catches up via `targ + (image_alpha - targ) * power(0.92, dt)`.

---

## 3. Measurements, Visual Check & Verification (Step 3)

### 3.1 `--profile` CPU loop before / after (`DecorSleep=0` vs `DecorSleep=1`, 15 s capture)

All runs below used identical pre-instrumented `--profile` builds controlled solely by `[Perf] DecorSleep=0` vs `1` via `~/jamm-bench/run.sh`:

| Build & Scene | Active instances (0 -> 1) | Sleeping deact | FPS (0 -> 1) | `loop_us` mean (0 -> 1) | `loop_us` p50 (0 -> 1) | Change (mean / p50) |
|---|---:|---:|---:|---:|---:|---:|
| `all7` dock | 12,452 -> 9,235 | 3,156 | 5.65 -> 9.45 | 174.36 ms -> 102.64 ms | 171.17 ms -> 99.89 ms | **-41.1% / -41.6%** |
| `all7` deepwater | 12,402 -> 9,461 | 3,001 | 4.11 -> 7.86 | 240.00 ms -> 123.89 ms | 209.94 ms -> 120.60 ms | **-48.4% / -42.6%** |
| `pub5` dock | 9,404 -> 7,370 | 1,872 | 5.70 -> 6.39 | 172.42 ms -> 151.96 ms | 169.77 ms -> 142.11 ms | **-11.9% / -16.3%** |
| `pub5` deepwater | 9,374 -> 7,508 | 1,717 | 4.87 -> 7.10 | 202.06 ms -> 136.87 ms | 188.48 ms -> 130.31 ms | **-32.3% / -30.9%** |
| `vanilla` dock | 2,271 -> 2,000 | 336 | 28.82 -> 31.01 | 33.93 ms -> 30.80 ms | 33.66 ms -> 30.53 ms | **-9.2% / -9.3%** |
| `vanilla` deepwater | 2,320 -> 1,952 | 338 | 27.66 -> 31.43 | 35.34 ms -> 30.35 ms | 35.05 ms -> 29.93 ms | **-14.1% / -14.6%** |

Per-hotspot `--profile` call/cost reductions (`all7` `dock`, 85 vs 142 ticks):
* `gml_Object_SceneItem_Step_2`: 2,880 calls/tick (`15,455 µs`) -> **211 calls/tick** (`1,184 µs`, **-92%**)
* `gml_Object_SceneItem_Draw_0`: 2,558 calls/tick (`11,688 µs`) -> **<50 calls/tick** (dropped out of top 10, **>98% eliminated**)
* `gml_Object_Weed_Step_0`: 1,197 calls/tick (`5,644 µs`) -> **dropped out of top 10**
* `gml_Object_AbstractLight_Draw_0`: 889 calls/tick (`3,852 µs`) -> **dropped out of top 10** (`~4 calls/tick`)
* `gml_Object_CaveRock_Step_0`: `18,343 µs` -> `16,478 µs` (active-guarded early exit skips `checkTimer` & `point_distance`; remaining profiler cost is empty-return VM dispatch on `Collidable` instances)

### 3.2 Unprofiled loop ms before / after (20 s capture, same pre-instrumented builds)

| Build & Scene | `frame_us` p50 (0 -> 1) | `loop_us` mean (0 -> 1) | `loop_us` p50 (0 -> 1) | FPS (0 -> 1) | Notes |
|---|---:|---:|---:|---:|---|
| `all7` dock (`ds4`) | 53.67 -> 46.09 ms | 53.10 -> 45.87 ms | 50.93 -> 42.84 ms | 17.88 -> 20.37 | **-13.6% mean / -15.9% p50** |
| `all7` dock (`dsf`) | 48.85 -> 45.32 ms | 49.07 -> 46.63 ms | 46.62 -> 42.22 ms | 19.49 -> 20.02 | **-5.0% mean / -9.4% p50** (140 ms outlier spike in `on`) |
| `all7` deepwater (`dsf`) | 48.84 -> 51.76 ms | 49.10 -> 50.17 ms | 46.61 -> 47.75 ms | 19.49 -> 18.53 | Near noise floor; `ds3-mB` hit 42.62 ms p50; `Fish_Step_2` dominates |
| `pub5` dock (`dsf2`) | 77.59 -> 81.96 ms | 79.67 -> 81.53 ms | 75.54 -> 78.20 ms | 12.22 -> 11.74 | Within ~2 ms variance; `pub5` lacks PWB `Fish` optimizations (`Fish_Step_2` ~44-57%) |
| `pub5` deepwater (`dsf`) | 81.42 -> 71.95 ms | 83.35 -> 72.65 ms | 78.82 -> 68.96 ms | 11.60 -> 13.22 | **-12.8% mean / -12.5% p50** |
| `vanilla` dock (`dsf`) | 20.26 -> 19.28 ms | 19.91 -> 18.16 ms | 19.51 -> 17.83 ms | 48.38 -> 50.96 | **-8.8% mean / -8.6% p50** |
| `vanilla` deepwater (`dsf`) | 19.21 -> 20.06 ms | 18.79 -> 18.76 ms | 18.51 -> 18.63 ms | 51.38 -> 49.51 | Parity |

**Full `builtin-perf` build + F8 independence (`dsf-perf-f8off-dock`, 10 s)**: Built `all7.win` + `builtin-perf` via `cgfmm build` (`Semantic reference verification: PASS (3212 code entries)`, `Catalog verification passed: 425 fish...`) and ran with `[Perf] Mode=0, FishGrid=0, DecorSleep=1`: `# perf_mode=0 interval=4 eligible_events=0 skipped_events=0`, `# fish_grid=0`, `# decor_sleep=1 tracked=8928 sleeping_deact=3156`, `instances=9267`, `loop_us mean=45,659 µs (p50=42,494 µs)`. `DecorSleep` operates cleanly and independently of F8.

**Post-merge (`main` + `CGF-PERF-REGRESS-001` / sanitizer split) dock re-bench (20 s capture via `~/jamm-bench/run.sh`, `DecorSleep` not installed vs `--decor-sleep`)**:

Note: During the `all7` and `pub5` runs (`decor3-*`), Xwayland/desktop idle SwapBuffers throttling capped `present_sleep_us_mean` at ~864–934 ms (`1.00 FPS`); `loop_us`, `step_us`, and `draw_us` measure pure in-frame CPU game-loop duration (`BeginStep`–`DrawGUIEnd`, excluding `present_sleep_us`). `vanilla` ran unthrottled (`decor4-*`, 41.28 vs 32.08 FPS, with two OS stall frames #427/#608 of 419 ms and 1,937 ms skewing `decor4-vanilla-on` mean).

| Build & Scene | Active instances (off -> on) | `loop_us` mean (off -> on) | `loop_us` p50 (off -> on) | `loop_us` p95 (off -> on) | `step_us` / `draw_us` p50 (off -> on) | Change (`loop_us` mean / p50) |
|---|---:|---:|---:|---:|---:|---|
| `all7` dock (`decor3`) | 12,393 -> 9,263 (`3,156` deact) | 74.87 -> 66.84 ms | 70.16 -> 61.78 ms | 95.50 -> 80.56 ms | 63.14/6.81 -> 57.33/4.44 ms | **-10.7% / -12.0%** |
| `pub5` dock (`decor3`) | 10,170 -> 8,299 (`1,872` deact) | 136.90 -> 126.67 ms | 138.72 -> 121.81 ms | 148.26 -> 139.30 ms | 110.23/27.99 -> 97.42/24.39 ms | **-7.5% / -12.2%** |
| `vanilla` dock (`decor4`) | 2,285 -> 1,940 (`336` deact) | 23.37 -> 26.79 ms | 22.92 -> 22.75 ms | 27.12 -> 26.11 ms | 20.28/2.62 -> 20.19/2.52 ms | **+14.6% (2-frame stall) / -0.7% p50 (-3.7% p95)** |

* **Plain `builtin-perf` & profile toggle parity**: `cgfmm build --base <vanilla> --order jamm-perf.win --no-repack` and `cgfmm apply` with `profile perf <name> decor off` both produced SHA-256 `dbb2e5c4…9286d2c1` (byte-identical to vanilla); `cgfmm apply` with default `DecorSleep = true` installed `DecorSleep` without `obj_jamm_perf` or `FishGrid`.

### 3.3 Visual Parity Check

Captured in-game GM framebuffer screenshots (`screen_save` via `jamm-modmaker bench --screenshot`) at identical camera positions (`dock` `X:928 Y:1062` and `deepwater` `X:21000 Y:7200`) with `DecorSleep=0` vs `DecorSleep=1`:

| Build | Dock pixel diff (`changed_px` / `mean_abs` per channel) | Deepwater pixel diff (`changed_px` / `mean_abs` per channel) |
|---|---|---|
| `all7` | 0.64% / 0.20 | 1.11% / 0.00 |
| `pub5` | 0.56% / 0.12 | 1.66% / 0.46 |
| `vanilla` | 0.29% / 0.09 | 2.01% / 0.77 |

Visual inspection confirms foreground plants, swaying weeds, background parallax hills/trees/houses, water surface tiles, deepwater lighting mask, center light orb, and HUD are identical (remaining `< 2.1%` / `< 0.8` channel diff comes solely from the live FPS digit and animated water/ambient fish timing).

### 3.4 Automated Tests & Build

* Synthetic .NET/GML tests in `tests/Jamm.Core.Tests/DecorSleepTests.cs` verify pattern-based coverage, exemptions (`Fish`, `Batfin`, `Coral`, `BreakableRock`, `MissileSilo`, `Lilypad`, `CrystalfishLight`, `Camera`, `SaveManager`, `jamm_chunk_stream`), `SonarPanel_Draw_0` `with (SceneItem)` deactivation-safe exception, child-light ownership (`GlowPlant` active-guarded), `light_anchor` patching, parallax/alpha-lerp/angle-pulse catch-up setup, INI toggle persistence, legacy profile JSON `DecorSleep` default (`true`), `ArchiveBuilder` opt-in installation (`DecorSleep` omitted on `perfSettings: null` or `DecorSleep: false`, installed on default `PerfSettings`), and graceful skip on missing legacy room/layout.
* Full build (`dotnet build -c Release`) passes with 0 warnings and 0 errors, and the full test suite passes after merging `main` (`649 passed, 17 skipped` real-file tests in `Jamm.Core.Tests`; `130 passed, 1 skipped` in `Jamm.Gui.Tests`).
