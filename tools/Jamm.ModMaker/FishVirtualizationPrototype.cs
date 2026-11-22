namespace Jamm.ModMaker;

/// <summary>Audited, engine-handle-free state for the Garder record-store prototype.</summary>
public readonly record struct FishVirtualizationRecord(
    int RecordId,
    double X,
    double Y,
    double XPrevious,
    double YPrevious,
    double Direction,
    double Speed,
    double HSpeed,
    double VSpeed,
    double Depth,
    double ImageIndex,
    double ImageSpeed,
    double ImageXScale,
    double ImageYScale,
    double ImageAngle,
    double ImageAlpha,
    int State,
    double Life,
    double RespawnTime,
    int CatalogIndex);

/// <summary>Primitive instance fields copied by the single-species bench prototype.</summary>
public readonly record struct FishVirtualizationState(
    double X,
    double Y,
    double XPrevious,
    double YPrevious,
    double Direction,
    double Speed,
    double HSpeed,
    double VSpeed,
    double Depth,
    double ImageIndex,
    double ImageSpeed,
    double ImageXScale,
    double ImageYScale,
    double ImageAngle,
    double ImageAlpha,
    int State,
    double Life,
    double RespawnTime,
    int CatalogIndex);

/// <summary>Deterministic record helpers and the isolated transition microbenchmark.</summary>
public static class FishVirtualizationPrototype
{
    internal const string VirtualizeGarderGml = """
        jamm_b_virtual_limit = max(0, round(argument0));
        jamm_b_virtualized_count = 0;
        jamm_b_virtual_records = ds_list_create();
        var start = get_timer();
        with (Garder)
        {
            if (other.jamm_b_virtualized_count >= other.jamm_b_virtual_limit) continue;
            var alarms_clear = true;
            for (var alarm_index = 0; alarm_index < 12; alarm_index += 1)
                if (alarm[alarm_index] >= 0) alarms_clear = false;
            var eligible = state == 0 && onBoat == 0 && !isDragging && alarms_clear
                && (!instance_exists(Lure) || (Lure.fish != id && point_distance(x, y, Lure.x, Lure.y) > 1000))
                && (!instance_exists(CatSwim) || CatSwim.holding != id)
                && (!view_visible[0] || point_distance(x, y, view_xview[0] + view_wview[0] / 2, view_yview[0] + view_hview[0] / 2) > 1000);
            if (eligible)
            {
                var rec = ds_list_create();
                ds_list_add(rec, object_index);
                ds_list_add(rec, x); ds_list_add(rec, y);
                ds_list_add(rec, xprevious); ds_list_add(rec, yprevious);
                ds_list_add(rec, direction); ds_list_add(rec, speed);
                ds_list_add(rec, hspeed); ds_list_add(rec, vspeed); ds_list_add(rec, depth);
                ds_list_add(rec, image_index); ds_list_add(rec, image_speed);
                ds_list_add(rec, image_xscale); ds_list_add(rec, image_yscale);
                ds_list_add(rec, image_angle); ds_list_add(rec, image_alpha);
                ds_list_add(rec, state); ds_list_add(rec, life);
                ds_list_add(rec, respawn_time); ds_list_add(rec, catalog_index);
                ds_list_add(other.jamm_b_virtual_records, rec);
                instance_destroy();
                other.jamm_b_virtualized_count += 1;
            }
        }
        jamm_b_virtualize_us = get_timer() - start;
        return jamm_b_virtualized_count;
        """;

    internal const string MaterializeGarderGml = """
        if (!variable_instance_exists(id, "jamm_b_virtual_records")) return 0;
        var restored = 0;
        var start = get_timer();
        var rng = random_get_seed();
        for (var i = 0; i < ds_list_size(jamm_b_virtual_records); i += 1)
        {
            var rec = ds_list_find_value(jamm_b_virtual_records, i);
            jamm_b_virtual_current_record = rec;
            var obj = ds_list_find_value(rec, 0);
            var fish = instance_create(ds_list_find_value(rec, 1), ds_list_find_value(rec, 2), obj);
            with (fish)
            {
                xprevious = ds_list_find_value(other.jamm_b_virtual_current_record, 3);
                yprevious = ds_list_find_value(other.jamm_b_virtual_current_record, 4);
                direction = ds_list_find_value(other.jamm_b_virtual_current_record, 5);
                speed = ds_list_find_value(other.jamm_b_virtual_current_record, 6);
                hspeed = ds_list_find_value(other.jamm_b_virtual_current_record, 7);
                vspeed = ds_list_find_value(other.jamm_b_virtual_current_record, 8);
                depth = ds_list_find_value(other.jamm_b_virtual_current_record, 9);
                image_index = ds_list_find_value(other.jamm_b_virtual_current_record, 10);
                image_speed = ds_list_find_value(other.jamm_b_virtual_current_record, 11);
                image_xscale = ds_list_find_value(other.jamm_b_virtual_current_record, 12);
                image_yscale = ds_list_find_value(other.jamm_b_virtual_current_record, 13);
                image_angle = ds_list_find_value(other.jamm_b_virtual_current_record, 14);
                image_alpha = ds_list_find_value(other.jamm_b_virtual_current_record, 15);
                state = ds_list_find_value(other.jamm_b_virtual_current_record, 16);
                life = ds_list_find_value(other.jamm_b_virtual_current_record, 17);
                respawn_time = ds_list_find_value(other.jamm_b_virtual_current_record, 18);
                catalog_index = ds_list_find_value(other.jamm_b_virtual_current_record, 19);
            }
            ds_list_destroy(rec);
            restored += 1;
        }
        ds_list_destroy(jamm_b_virtual_records);
        jamm_b_virtual_records = ds_list_create();
        jamm_b_virtualized_count = 0;
        random_set_seed(rng);
        jamm_b_virtual_roundtrip_us = get_timer() - start;
        return restored;
        """;

    internal const string CreateDestroyGarderGml = """
        if (!variable_instance_exists(id, "jamm_b_virtual_records")) return 0;
        var rng = random_get_seed();
        var start = get_timer();
        for (var i = 0; i < ds_list_size(jamm_b_virtual_records); i += 1)
        {
            var rec = ds_list_find_value(jamm_b_virtual_records, i);
            var obj = ds_list_find_value(rec, 0);
            var fish = instance_create(ds_list_find_value(rec, 1), ds_list_find_value(rec, 2), obj);
            instance_destroy(fish);
        }
        random_set_seed(rng);
        jamm_b_virtual_baseline_us = get_timer() - start;
        return ds_list_size(jamm_b_virtual_records);
        """;

    /// <summary>Copies the fixed Garder schema into a record with no engine handles or RNG calls.</summary>
    public static FishVirtualizationRecord Snapshot(int recordId, FishVirtualizationState state)
    {
        if (recordId < 0) throw new ArgumentOutOfRangeException(nameof(recordId));
        return new FishVirtualizationRecord(recordId, state.X, state.Y, state.XPrevious, state.YPrevious,
            state.Direction, state.Speed, state.HSpeed, state.VSpeed, state.Depth, state.ImageIndex,
            state.ImageSpeed, state.ImageXScale, state.ImageYScale, state.ImageAngle, state.ImageAlpha,
            state.State, state.Life, state.RespawnTime, state.CatalogIndex);
    }

    /// <summary>Restores the fixed Garder schema without invoking species creation or random functions.</summary>
    public static FishVirtualizationState Restore(FishVirtualizationRecord record)
        => new(record.X, record.Y, record.XPrevious, record.YPrevious, record.Direction, record.Speed,
            record.HSpeed, record.VSpeed, record.Depth, record.ImageIndex, record.ImageSpeed,
            record.ImageXScale, record.ImageYScale, record.ImageAngle, record.ImageAlpha, record.State,
            record.Life, record.RespawnTime, record.CatalogIndex);

}
