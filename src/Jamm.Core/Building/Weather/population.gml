if (room != NewWorld || !instance_exists(Girl)) return 0;
with (Fish)
    if (variable_instance_exists(id, "jamm_weather_owner")) jamm_weather_gate();
if (weather_live < 0) return 0;
if (!instance_exists(CatalogData) || !variable_instance_exists(CatalogData.id, "jamm_fish_names")) return 0;
var w = weather_live;
for (var j = 0; j < weather_species[w]; j += 1)
{
    var index = j * 6;
    var species = weather_fish[w, index];
    if (!ds_map_exists(CatalogData.jamm_fish_names, species)) continue;
    var count = weather_fish[w, index + 1];
    var left = weather_fish[w, index + 2];
    var right = weather_fish[w, index + 3];
    var top = weather_fish[w, index + 4];
    var bottom = weather_fish[w, index + 5];
    var anchor = max(930, Girl.x);
    var present = instance_number(species);
    for (var i = present; i < count; i += 1)
    {
        var u = (i + 0.5) / count;
        var sx = clamp(anchor + lerp(left, right, u), 740, room_width - 100);
        var sy = lerp(top, bottom, frac((i + 1) * 0.61803398875));
        if (collision_point(sx, sy, Collidable, true, true) != noone) continue;
        var fish = instance_create(sx, sy, species);
        if (instance_exists(fish))
        {
            fish.jamm_weather_owner = w;
            fish.jamm_weather_school_anchor = anchor;
            fish.stayOutAtNight = (weather_rule[w, 1] & 8) != 0;
        }
    }
}
return 0;
