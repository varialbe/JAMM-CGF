# Task: Bulbfish render glitch (fix/bulbfish-light)
User (2026-11-23, playing personal pack = perf FishSleep+DecorSleep, hd-lite, console, smooth, silo, weather-pack, endless-ocean):
"bulbfishe werden bisschen weird rendered". Build decisions show: `[CGF-PERF-DECOR] covered (Visible, active-guarded) [... AbstractLight,
Light, Light2, BulbfishLight ...]` and `[CGF-PERF-SLEEP] Bulbfish: covered`.
Vanilla: Bulbfish_Create creates `light = instance_create(x,y,BulbfishLight)`; Bulbfish_Draw_72 (Draw Begin) calls script
`light_anchor()` which does `with(light){x=other.x; y=other.y; image_xscale/yscale/angle/index; depth; visible = other.visible;}` and
moves `light2` (a `Light`) the same way. DecorSleep (src/Jamm.Core/Builtins/DecorSleep.cs ~l.590-620) stores `inst.visible` in
global.jamm_ds_bvis and sets visible=false for off-view instances, later restores the STORED value → fights light_anchor (stale
visibility, light hidden by its own stale position while fish is on screen, ghost light when fish invisible). Also check FishSleep's
Draw-subtype guards on Bulbfish (Draw_72 skipped while asleep) and Smooth interpolation (light vs fish drawn at different positions on
render-only frames: Smooth interpolates instances near camera — make sure anchored lights follow the interpolated owner or are
interpolated identically).
## Required
1. Generic rule in DecorSleepAnalysis: never cover objects whose x/y/visible are written by other objects' code (`with (<var/obj>) { x = ... }`,
   `inst.visible = ...`, light_anchor-style scripts, lights created via instance_create and stored in an owner variable). Exclude
   BulbfishLight, Light, Light2, CrystalfishLight-style anchored lights; decision line explaining why.
2. Verify all other fish-attached lights/objects (grep vanilla + public mods for `light_anchor`, `with (light`, `instance_create(...Light`).
3. Verify smooth: render-only frames draw light at the same interpolated position as its fish.
4. Local visual check: bench scene with bulbfish (deep cave area, see tools/Jamm.ModMaker bench scenes / showcase) before/after
   screenshots using ~/jamm-bench/run.sh (serialized lock; never CGF-Personal/cgf-personal/343780; no xdotool). Tests; dotnet test; commit; no push.
