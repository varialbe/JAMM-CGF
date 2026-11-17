// Mechanism: Rod Other_10 resets Fish.base_lunge_mod = 1 and Fish.base_lunge_offset = 0
// across all Fish instances (and applies Attract/Repel/Glitterbait/Stealthbait/Gear) before
// invoking custom apply hooks. Multiplying base_lunge_mod and adding to base_lunge_offset
// here is therefore idempotent across Rod Other_10 recomputes.
// argument0 = installed stack count on the active rod.
var jamm_stacks;
jamm_stacks = argument0;
with (Fish)
{
    base_lunge_mod *= power(1.25, jamm_stacks);
    base_lunge_offset += 25 * jamm_stacks;
}
