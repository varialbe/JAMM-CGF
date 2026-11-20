# Task: Endless Ocean v0 – showcase screenshots for the user

The worldgen-v0 screenshots (`~/jamm-night/worldgen/screens/`) prove function but show almost nothing: a close-up of the swimming cat,
a black background, one rock ridge, one fish. The user wants to *see* the generated world. Work on `exp/worldgen-v0`
(`~/jamm-wt/worldgen-v0`), small task.

* Extend the worldgen bench scene (tools/Jamm.ModMaker/WorldgenScene.cs) with a showcase mode: zoomed-out view (enlarge the view/port size
  or `view_wview/view_hview` temporarily in the bench only, never in the mod), a vanilla light source large enough to see terrain, and capture
  * a wide shot at the transition from the vanilla world to the generated ocean (where the old content ends),
  * 3–4 wide shots along the generated floor at different x (different terrain heights, all three fish depth bands visible),
  * a horizontal panorama strip of ~5 consecutive screens stitched with ffmpeg (no Python PIL on the VPS),
  * one surface shot from the boat out east (sea + sky) to show it is a continuous ocean.
* Seeds 1337 and 314159 to show the floor differs per seed (one pair of matching shots).
* Bench-only code; the recipe/mod stays unchanged. Vanilla build first; public-5 only if time allows.
* Save to `~/jamm-night/worldgen/showcase/` and list them with captions in docs/reports/worldgen-v0.md.
* In-game runs one at a time (other agents share the VPS: check `pgrep -f "Cat Goes Fishing.exe"` and wait). Zero warnings, tests green,
  commit on the branch, no push.
