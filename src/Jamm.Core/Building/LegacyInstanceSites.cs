using System.Text.RegularExpressions;

namespace Jamm.Core.Building;

internal static class LegacyInstanceSites
{
    // These paths need an absent outcome specific to the operation. Match the complete
    // statement so that alternate implementations supplied by mods remain untouched.
    internal static string Rewrite(string code, string text, List<InstanceReference> found)
    {
        string result = text;
        switch (code)
        {
            case "gml_Object_Chook_Other_10":
                Replace("Ancient_Kingfish", "if (point_distance(x, y, Lure.x, Lure.y) <= 51)",
                    $"if (point_distance(x, y, Lure.x, Lure.y) <= 51 && instance_exists({InstanceGuard.Symbol("Ancient_Kingfish")}))");
                break;
            case "gml_Object_Thermo_Other_10":
                Replace("nearby_magmer", "var dist = point_distance(ThermoSchool.x, ThermoSchool.y, nearby_magmer.x, nearby_magmer.y);", """
                    var dist = 500;
                    if (instance_exists(nearby_magmer))
                        dist = point_distance(ThermoSchool.x, ThermoSchool.y, nearby_magmer.x, nearby_magmer.y);
                    """);
                break;
            case "gml_Object_SaveManager_Other_13":
                Replace("Bulbfish", "ind = Bulbfish.object_index;", $"""
                    var jamm_lamp_object = {InstanceGuard.Symbol("Bulbfish")};
                    ind = jamm_lamp_object;
                    """, "object-index");
                break;
            case "gml_Object_LightingSystem_Draw_0":
                Replace("Torby", "draw_sprite_ext(Torby.sprite_index, 0, Girl.x + ox, Girl.y + oy, -1, 1, 0, c_white, 0.3);", $"""
                    var jamm_ghost_sprite = object_get_sprite({InstanceGuard.Symbol("Torby")});
                    if (instance_exists({InstanceGuard.Symbol("Torby")}))
                        jamm_ghost_sprite = Torby.sprite_index;
                    draw_sprite_ext(jamm_ghost_sprite, 0, Girl.x + ox, Girl.y + oy, -1, 1, 0, c_white, 0.3);
                    """);
                break;
            case "gml_Object_Snobfish_Other_10":
                Replace("bro", "if (bro.object_index != Nightfish)", $"""
                    var jamm_bro_is_nightfish = false;
                    if (bro >= 0 && instance_exists(bro))
                        jamm_bro_is_nightfish = bro.object_index == {InstanceGuard.Symbol("Nightfish")};
                    if (!jamm_bro_is_nightfish)
                    """);
                break;
            case "gml_Object_Flash_Other_10":
                if (!text.TrimStart().StartsWith("target_x = target_node.x;", StringComparison.Ordinal)) break;
                Replace("target_node", "target_x = target_node.x;", $$"""
                    if (target_node < 0 || !instance_exists(target_node))
                    {
                        target_node = instance_nearest(x, y, {{InstanceGuard.Symbol("NavNode")}});
                        if (target_node < 0 || !instance_exists(target_node)) exit;
                    }
                    target_x = target_node.x;
                    """);
                break;
        }
        return result;

        void Replace(string target, string anchor, string replacement, string kind = "instance-repair")
        {
            string masked = InstanceGuard.Mask(text);
            var match = Regex.Match(masked, $@"(?m)^([ \t]*){Regex.Escape(anchor)}[ \t]*\r?$");
            if (!match.Success || match.NextMatch().Success) return;
            string indent = match.Groups[1].Value;
            int line = 1 + text.AsSpan(0, match.Index).Count('\n');
            found.Add(new(code, line, kind, target, anchor, true));
            result = text[..match.Index] + indent + replacement.Replace("\n", "\n" + indent, StringComparison.Ordinal)
                + text[(match.Index + match.Length)..];
        }
    }
}
