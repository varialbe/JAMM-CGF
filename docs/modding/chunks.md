# Streaming chunks in legacy CGF

Endless Ocean ships two reusable GML scripts. They run on the legacy August 2023 VM. You can use them from another controller in an archive that includes Endless Ocean or copy the scripts into your own archive under the same names.

## `jamm_chunk_unit(seed, index, channel)`

Returns a value in `[0, 1)`. Use integer seeds from 0 to 2,147,483,646 and nonnegative integer indices and channels. Keep the index and channel in the same integer range so the intermediate products remain exact. Use distinct channels for decisions such as a rock's scale and a fish's species. Calling the sampler leaves GameMaker's random state alone. The sampler is for layout generation.

## `jamm_chunk_stream(chunks, first, last, create_script, release_script)`

Create a `ds_map` for each controller and pass an inclusive range of whole-number chunk indices. The helper releases chunks outside the range before loading missing chunks within it. A range with `first > last` releases everything. Call it when your desired range changes.

GameMaker runs both callbacks in the controller's context. The creation callback takes `(index, owned)`. Add each room instance ID to `owned` with `ds_list_add(owned, instance_id)`. The release callback receives each tracked ID exactly once when its chunk leaves the range. It should release any resources attached to that instance and destroy it or hand it over to another owner. A callback may see an ID that gameplay has already destroyed. Handle deactivated instances if your mod deactivates the objects it tracks. Leave the map and the lists themselves to the helper.

Before destroying a controller's map, stream an empty range. In a persistent room, you can keep the map with your controller while the room is suspended. Each controller chooses its own coordinate origin, chunk width, range and content.

```gml
// Create
chunks = ds_map_create();

// Step, after finding the view center in world coordinates
var center_chunk = floor((view_xview[0] + view_wview[0] * 0.5 - origin_x) / width_px);
jamm_chunk_stream(chunks, max(0, center_chunk - 2), min(last_chunk, center_chunk + 2),
                  my_chunk_load, my_chunk_release);

// Destroy
jamm_chunk_stream(chunks, 0, -1, my_chunk_load, my_chunk_release);
ds_map_destroy(chunks);
```

The [Endless Ocean recipe](../../examples/modkit/endless-ocean/README.md) uses shared floor samples across chunk seams, checks collision masks before adding fish and keeps caught fish in vanilla gameplay after their chunk unloads. Its [GML source](../../src/Jamm.Core/Builtins/Worldgen/) also shows the callback implementations.
