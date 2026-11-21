# Task: Fix Maize nav crash in multi-mod builds ("bei Sonar öffnen")

Branch `exp/maize-nav-fix`, worktree `~/jamm-wt/maize-nav`.

## Report (user, 2026-11-21, local PC, build `~/jamm-night/builds/all7-repack.win`, all 7 mods)
Opening the sonar crashes the game:
```
FATAL ERROR in action number 1 of Other Event: User Defined 0 for object Maize:
Variable Maize.edges(100337, -2147483648) not set before reading it.
at gml_Script_nav_get_random_connected_node
stack: gml_Script_nav_get_random_connected_node / gml_Object_Maize_Other_10 / called from gml_Object_Fish_Step_2
```

## Orchestrator analysis so far
* `nav_get_random_connected_node` and `Maize_Other_10` are **unchanged vanilla code** in the all-7 build.
* `nav_get_random_connected_node(node, prev)` returns **-1** when `node.edges` holds no edge leading to a
  node other than `prev`/`node`. Maize then sets `target_node = -1`; next call reads `(-1).edges` = `self.edges`
  → `Maize.edges` not set → crash. In vanilla the graph has no such dead ends.
* `NavNode_Create_0` does `edges = ds_list_create()`; edges are filled elsewhere (find where: room
  instance creation code / NavEdge instances / room creation code). Hypotheses to verify:
  1. The merged NewWorld (or the room Maize lives in) changes **instance creation order**, so a NavNode's
     Create (which replaces `edges` with a new empty list) runs after edges were registered.
  2. A mod (Cave relocation, Abyss, TGE, PWB, Silo area, Endless Ocean …) moves/removes NavNode/NavEdge
     instances or creation code, leaving dangling or one-sided edges.
  3. Perf/fish scheduling deactivation interacts (instance_nearest on NavNode while some nodes are deactivated).
  "Sonar öffnen" may only be when the Maize area becomes active/visible (deep sonar), not the cause.

## Required
1. **Root cause with evidence**: compare vanilla vs all-7 (and each single mod) for the NavNode/NavEdge
   instances, their creation code and order in every affected room. Name the mod(s) and the build step that
   introduces the dead end. Use `tools/Jamm.ModMaker` (`dump`, `names`, `rooms`) and `cgfmm diff --show`.
2. **Proper fix in the merger** (preferred): preserve vanilla nav graph integrity (instance order / creation
   code) when merging rooms, as a diagnosed decision like the existing `[CGF-INSTANCE-003]` in
   `InstanceCompatibility` (new code e.g. `[CGF-NAV-001]`).
3. **Defensive runtime guard** (in addition, only in multi-mod/modded builds, never in pure vanilla):
   make `nav_get_random_connected_node` return `previousNode` (or the nearest valid NavNode) instead of -1
   when no connection exists, and in Maize Other_10 re-pick `instance_nearest(x, y, NavNode)` when
   `!instance_exists(target_node)`. Behaviour identical to vanilla whenever the graph is intact.
4. Tests: unit test reproducing the dead-end case; vanilla build stays byte-identical; full suite green.
5. In-game on the VPS: build all-7 through the normal profile path (repack on), run `dock` and `deepwater`
   with `~/jamm-night/bin/run.sh`, plus a scene that spawns/activates Maize and opens the sonar if the bench
   can do it (extend the bench scene if needed). Report RECORDED / CODE ERROR.
6. Report `docs/reports/maize-nav-fix.md` (root cause, fix, evidence). Commit on the branch (no push to main).
Private builds TGE/PWB stay on the VPS only (`~/cgf-files/private-vel`).
