var swimmer = argument0;
if (!instance_exists(swimmer)) return 0;
var advanced = false;
var adv_obj = asset_get_index("SonarPanelAdvanced");
if (adv_obj >= 0 && instance_exists(adv_obj))
{
    var adv_panel = instance_find(adv_obj, 0);
    if (adv_panel.advancedDrawEnabled) advanced = true;
}
var panel_obj = asset_get_index("SonarPanel");
if (panel_obj >= 0 && instance_exists(panel_obj))
{
    var panel = instance_find(panel_obj, 0);
    if (panel.cave_sonar) advanced = true;
}
with (swimmer)
{
    if (advanced)
    {
        s1 = sprite_index;
        if (object_index == asset_get_index("Abyssal"))
        {
            var head = asset_get_index("spr_abyssal_head");
            if (head >= 0) s1 = head;
        }
        if (object_index == asset_get_index("Dragon"))
        {
            var head = asset_get_index("spr_dragon_head");
            if (head >= 0) s1 = head;
        }
        if (object_index == asset_get_index("Problematicus"))
        {
            var head = asset_get_index("spr_problem_head");
            if (head >= 0) s1 = head;
        }
        s2 = image_index;
        s3 = image_xscale;
        s4 = image_yscale;
        s5 = 1;
    }
    else
    {
        s1 = asset_get_index("spr_sonarblip");
        s2 = display_size - 1;
        s3 = 10 + (5 * (display_size - 1));
        s4 = 10 + (3 * (display_size - 1));
        s5 = (size + 0.5) / 3.5;
    }
}
return 0;
