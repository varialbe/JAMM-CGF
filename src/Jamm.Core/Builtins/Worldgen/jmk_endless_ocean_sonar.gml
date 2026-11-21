var swimmer = argument0;
with (swimmer)
{
    if (SonarPanelAdvanced.advancedDrawEnabled || SonarPanel.cave_sonar)
    {
        s1 = sprite_index;
        if (object_index == 132) s1 = 238;
        if (object_index == 135) s1 = 242;
        if (object_index == 225) s1 = 334;
        s2 = image_index;
        s3 = image_xscale;
        s4 = image_yscale;
        s5 = 1;
    }
    else
    {
        s1 = 424;
        s2 = display_size - 1;
        s3 = 10 + (5 * (display_size - 1));
        s4 = 10 + (3 * (display_size - 1));
        s5 = (size + 0.5) / 3.5;
    }
}
