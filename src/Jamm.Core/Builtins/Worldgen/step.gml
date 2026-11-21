// Gate the extension behind the final vanilla boat. Boat input itself stays vanilla.
with (Boat7)
{
    MAX_X = max(MAX_X, other.ocean_end - 400);
}

if (!instance_exists(Camera) || !instance_exists(Rod) || !instance_exists(SaveManager) || !SaveManager.loaded) exit;
// Fish can outlive their chunk while caught. Release them once vanilla is done.
for (var i = ds_list_size(ocean_catch) - 1; i >= 0; i -= 1)
{
    if (jmk_endless_ocean_dispose(ds_list_find_value(ocean_catch, i))) ds_list_delete(ocean_catch, i);
}
var center = clamp(Camera.x, 0, room_width - 1);
var index = floor((center - ocean_start) / 2048);
var first = max(0, index - 2);
var last = min(ocean_chunk_count - 1, index + 2);
if (first > last)
{
    first = 0;
    last = -1;
}
if (first != ocean_first || last != ocean_last)
{
    var loaded_new_chunks = first <= last && (first < ocean_first || last > ocean_last);
    jamm_chunk_stream(ocean_chunks, first, last, jmk_endless_ocean_load, jmk_endless_ocean_release);
    // Late-spawned fish need the same lure modifiers as fish present at room start.
    if (loaded_new_chunks) with (Rod) event_user(0);
    ocean_first = first;
    ocean_last = last;
}
