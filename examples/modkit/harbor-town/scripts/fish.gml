/// Refresh one display tank from the caught rows of today's live Fishalog.
harbor_fish_count = 0;
if (harbor_level[1] <= 0 || !instance_exists(CatalogData)) exit;
var rows = array_height_2d(CatalogData.object);
for (var row = 0; row < rows; row += 1)
{
    var cols = array_length_2d(CatalogData.object, row);
    for (var col = 0; col < cols; col += 1)
    {
        if (CatalogData.count[row, col] <= 0) continue;
        var fish = CatalogData.object[row, col];
        if (!is_real(fish) || fish < 0 || !object_exists(fish)) continue;
        var sprite = object_get_sprite(fish);
        if (sprite < 0 || !sprite_exists(sprite)) continue;
        harbor_fish_sprite[harbor_fish_count] = sprite;
        harbor_fish_count += 1;
        if (harbor_fish_count == 6) exit;
    }
}
