// Let fish chase nearby bait, then turn back towards their home water.
if (state == 0 || state == 1 || state == 2 || state == 5)
{
    if (point_distance(x, y, TRUE_START_X, TRUE_START_Y) > 220)
    {
        state = 0;
        direction = point_direction(x, y, TRUE_START_X, TRUE_START_Y);
    }
    if (state == 0)
        fish_avoid_wall(70);

    // Bound movement per frame so a long frame cannot carry a fish through the shaft walls.
    var shaft_limit;
    shaft_limit = state == 1 ? LUNGE_SPEED : max(2, jamm_swim_speed * 2);
    spd = min(spd, shaft_limit);
    speed = min(speed, shaft_limit);
}
