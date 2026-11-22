using Jamm.ModMaker;
using System.Reflection;

namespace Jamm.Core.Tests;

public sealed class FishVirtualizationPrototypeTests
{
    [Fact]
    public void GarderRecordRestoresExactAuditedFieldsForTenThousandCycles()
    {
        var source = new FishVirtualizationState(
            1234.5, 678.25, 1233.5, 677.25, 271, 3.5, -2.25, 1.75, 4,
            12.75, 0.35, 1.2, 0.8, 180, 0.7, 0, 42.5, 17, 9);
        var ids = new HashSet<int>();

        for (int cycle = 0; cycle < 10_000; cycle++)
        {
            var record = FishVirtualizationPrototype.Snapshot(cycle, source);
            Assert.Equal(source, FishVirtualizationPrototype.Restore(record));
            Assert.True(ids.Add(record.RecordId));
        }

        Assert.Equal(10_000, ids.Count);
    }

    [Fact]
    public void GarderRecordSchemaContainsNoEngineHandleFields()
    {
        var fieldTypes = typeof(FishVirtualizationRecord).GetProperties()
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.All(fieldTypes, type => Assert.True(type == typeof(int) || type == typeof(double)));
        Assert.DoesNotContain(typeof(FishVirtualizationRecord).GetProperties(), property =>
            property.Name.Contains("IdHandle", StringComparison.OrdinalIgnoreCase)
            || property.PropertyType == typeof(nint)
            || property.PropertyType == typeof(IntPtr));
    }

    [Fact]
    public void FishVirtualizationIsOffUnlessExplicitlyRequested()
    {
        Assert.Equal(0, Jamm.ModMaker.Program.ParseBenchOptions([]).FishVirtualizationCount);
        Assert.Equal(100, Jamm.ModMaker.Program.ParseBenchOptions(["--fish-virtualization-count", "100"]).FishVirtualizationCount);
        Assert.Equal(0, Jamm.ModMaker.Program.ParseBenchOptions([]).FishVirtualizationFixtureCount);
        Assert.Equal(300, Jamm.ModMaker.Program.ParseBenchOptions(["--fish-virtualization-fixture-count", "300"]).FishVirtualizationFixtureCount);
    }

    [Fact]
    public void ControlledGarderFixtureUsesASeedAndRestoresGlobalRandomState()
    {
        var script = typeof(FishVirtualizationPrototype)
            .GetField("SpawnGarderFixtureGml", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetRawConstantValue() as string;

        Assert.NotNull(script);
        Assert.Contains("random_set_seed(jamm_b_seed + 2027)", script);
        Assert.Contains("random_set_seed(rng)", script);
        Assert.Contains("with (Rod) event_user(0)", script);
        Assert.Contains("100000 + (i * 64)", script);
    }

    [Fact]
    public void GarderVirtualizationUsesTheControllerRecordListFromInsideWith()
    {
        var script = typeof(FishVirtualizationPrototype)
            .GetField("VirtualizeGarderGml", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetRawConstantValue() as string;

        Assert.NotNull(script);
        Assert.Contains("ds_list_add(other.jamm_b_virtual_records, rec)", script);
    }
}
