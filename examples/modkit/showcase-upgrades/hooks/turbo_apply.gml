// Mechanism: Rod Other_10 resets Rod.upgrades[1] = 0 before invoking apply hooks,
// and Lure Step_0 computes modified_reel_speed = (Rod.reel_speed - ...) + Rod.upgrades[1].
// Adding (2.0 * stacks) here is therefore idempotent across Rod Other_10 recomputes.
// argument0 = installed stack count on the active rod.
Rod.upgrades[1] += 2.0 * argument0;
