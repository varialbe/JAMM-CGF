// Mechanism: Rod Other_10 resets Rod.upgrades[0] = 0 before invoking apply hooks,
// and Lure Step_0 uses (SINK_SPEED - rise_speed) + Rod.upgrades[0] as sink rate.
// Adding (1.5 * stacks) here is therefore idempotent across Rod Other_10 recomputes.
// argument0 = installed stack count on the active rod.
Rod.upgrades[0] += 1.5 * argument0;
