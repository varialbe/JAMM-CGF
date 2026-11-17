// Mechanism: Rod Other_10 resets Rod.upgrades[13] = 0 before invoking apply hooks,
// and Lure Step_0 computes MAX_TENSION = 240 + (100 * Rod.rod_power) + (120 * Rod.upgrades[13]).
// Adding (2 * stacks) here is therefore idempotent across Rod Other_10 recomputes.
// argument0 = installed stack count on the active rod.
Rod.upgrades[13] += 2 * argument0;
