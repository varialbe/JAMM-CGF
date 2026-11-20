// map, inclusive first/last indices, create script, release script
var chunks = argument0;
var first = argument1;
var last = argument2;
var create_script = argument3;
var release_script = argument4;

var index = ds_map_find_first(chunks);
while (!is_undefined(index))
{
    var next_index = ds_map_find_next(chunks, index);
    if (index < first || index > last)
    {
        var owned = ds_map_find_value(chunks, index);
        for (var i = 0; i < ds_list_size(owned); i += 1)
        {
            script_execute(release_script, ds_list_find_value(owned, i));
        }
        ds_list_destroy(owned);
        ds_map_delete(chunks, index);
    }
    index = next_index;
}

for (index = first; index <= last; index += 1)
{
    if (!ds_map_exists(chunks, index))
    {
        var owned = ds_list_create();
        ds_map_add(chunks, index, owned);
        script_execute(create_script, index, owned);
    }
}
