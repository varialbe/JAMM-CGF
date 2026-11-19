# Review of the original Silo mod

Reviewed the legacy 2023-08-16 archive and the seven local pack inputs on the VPS. The review covers the implementation at `1210aa4`.

## Vanilla silo

`NewWorld` contains one `MissileSilo`, instance 101252 at (33984, 11570). Its Create event stops sprite animation and sets `canOpen` to zero. Its normal Step is empty. It has no other own events. It inherits from `CaveRockBig` through `CaveRock` and `Collidable`; the runner draws frame 0 of `spr_silo` by default and uses `spr_silomask` as its separate collision mask. Noxius uses the silo as a distance reference, and Magnalav uses it as a home-position reference. The base contains no scripted hatch transition.

The old recipe replaces both events and adds Draw to the original object. It animates the mask sprite as visible art and also changes its collision frame. Cave Expansion changes the same Create entry by switching `canOpen` to 1. Direct generation on the existing all-seven archive writes an archive but unconditionally replaces the input's Create, Step and Draw handlers. That success does not establish that the original behavior survived.

## Seven-input placement scan

The scan uses an inclusive axis-aligned 1,500 px window around (33984, 11570). An instance moving across the boundary appears on the side where it was in range. The vanilla neighbors are two CaveRockBig instances, three Geigers, Powerfin and Noxius. No input changes either original silo sprite or the MissileSilo object definition. Cave Expansion changes the Create code and adds a separate sprite named `spr_siloes`.

| Input | NewWorld changes | Nearby instance changes |
| --- | --- | --- |
| Antiquity | Dimensions and instances | Moves silo to (33776, 11440) and CaveRockBig to (33408, 11472), moves two Geigers and takes one Geiger and Powerfin out of the window. Adds five Greg and three BigRockRadianite instances. |
| Abyss | Dimensions, persistence and instances | Moves Powerfin from (33940, 11411) to (33952, 11408). |
| Colour | Dimensions and instances | Removes or moves the CaveRockBig at (33758, 11659) out of range. Adds Shadowcat, Meteor, CaveRockSig and CaveRockTee. |
| Verdant | Instances | Moves the silo to (33984, 11568) and brings Lampis into the window at (33696, 12848). |
| Cave Expansion | Dimensions and instances | Moves silo to (35472, 11984) and Geiger to (33184, 10944). |
| TGE | Dimensions, speed and instances | No positional differences within the window. |
| PWB | Dimensions, speed and instances | No positional differences within the window. |

## Runtime review

The old transition enables room persistence, deactivates NavBrain, and deactivates the persistent SaveManager in the destination. On return it hardcodes persistence to false, even on a base such as Abyss that declares it true, and resets the cat's position and breath. Esc immediately exits the area. The vanilla pause menu opens from Girl's Esc handler when the cat is aboard and the lure is inactive. Girl and her boat are room-local, so the default pause menu cannot save the player's position inside the old room. The old visit has no breath limit, usable collectibles, or area save checkpoint. Vanilla saves serialize the player's surface position, active boat and rod, catch and quests. They do not serialize CatSwim's position.

Game data and screenshots from loaded archives stay on the VPS.

The raw all-seven check found 104 conflicts overall, including one on `gml_Object_MissileSilo_Create_0` between Cave Expansion and the old Silo. With Silo last, the builder verified the output and recorded 156 decisions. The overlapping Create hunk selected Silo. Logs are in `~/jamm-night/silo-area/phase1-check.log` and `phase1-build.log`.

On the legacy game, an instrumented retry opened the hatch, entered twice and used Esc to return both times. Money (123456), fish count (0), rod (0), boat (1) and daily quest accepts (0) matched. The vanilla pause handler was absent during the visit and after the return. SaveManager was absent both in the area and ten frames after each return. That contradicts the old implementation's save-preservation claim even though the live world survived the trip. The first probe timed out after the unpinned cat drifted away from the interaction point. The successful probe kept the cat near the hatch. There was no save-and-quit control in the room and no breath timer.

VPS screenshots are in `~/jamm-night/silo-area/phase1-screens/` as `audit-closed.png`, `audit-open.png`, `audit-area.png`, and `audit-back.png`. The open hatch reads as a bright square and the world lighting cuts off both ends of the prompt. The entrance view in the area is mostly open blue water with a wide text banner, a small diver and a few decorative fish.

There was no in-area Save and quit action to exercise in the old build. The pre-transition save is the last snapshot SaveManager can write during the visit, and vanilla reload has no hatch position marker. The old probe tested Esc and repeat entry. The replacement has a separately exercised save-and-reload scene.
