using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Checks the fish grid's geometric and ordering rules and prepares its opt-in runner fixture.</summary>
public sealed class FishGridTests
{
    [Fact]
    public void PaddedCellUnionPreservesBruteForceOrderAtBoundariesAndAfterBoundedMotion()
    {
        const int size = FishGridScripts.CellSize;
        const int drift = FishGridScripts.Drift;
        var random = new Random(4531);
        var snapshot = new (double X, double Y)[] { (-size, -size), (0, 0), (size, size), (1_200_000, -1_200_000) }
            .Concat(Enumerable.Range(0, 500).Select(i => (X: random.NextDouble() * 4096 - 2048, Y: random.NextDouble() * 4096 - 2048)))
            .ToArray();
        var cells = snapshot.Select((fish, index) => (fish, index)).ToLookup(item => Cell(item.fish.X, item.fish.Y));
        var moved = snapshot.Select(fish => (X: fish.X + random.Next(-drift, drift + 1),
            Y: fish.Y + random.Next(-drift, drift + 1))).ToArray();

        foreach (var center in snapshot.Take(100).SelectMany(fish => new[] { fish, (X: fish.X - size, Y: fish.Y + size) }))
        foreach (int radius in new[] { 0, 1, size - 1, size })
        {
            var start = Cell(center.X - radius - drift - 1, center.Y - radius - drift - 1);
            var end = Cell(center.X + radius + drift + 1, center.Y + radius + drift + 1);
            var candidates = new List<int>();
            for (int y = start.Y; y <= end.Y; y++)
            for (int x = start.X; x <= end.X; x++)
                candidates.AddRange(cells[(x, y)].Select(item => item.index));
            var expected = Enumerable.Range(0, moved.Length).Where(InRange);
            Assert.Equal(expected, candidates.Order().Where(InRange));

            bool InRange(int index)
            {
                double dx = moved[index].X - center.X;
                double dy = moved[index].Y - center.Y;
                return dx * dx + dy * dy < radius * radius;
            }
        }

        static (int X, int Y) Cell(double x, double y)
            => ((int)Math.Floor(x / size), (int)Math.Floor(y / size));
    }

    [Fact]
    public void UnknownAvoidanceDoesNotInstallAnyRuntimeCode()
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;
        data.Rooms.Add(new UndertaleRoom { Name = data.Strings.MakeString("NewWorld") });
        var fish = ModObjects.Create(data, "Fish", false);
        ModObjects.Create(data, "FishLarge", false).ParentId = fish;
        fish.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.EndStep, data);
        UndertaleCode.CreateEmptyEntry(data, "gml_Script_fish_avoid_fish");
        int count = data.Code.Count;
        Assert.Equal("[CGF-PERF-GRID] fish_avoid_fish: skipped (avoidance loop differs from the audited pattern)",
            FishGrid.Apply(data));
        Assert.Equal(count, data.Code.Count);
        Assert.Null(data.GameObjects.ByName(FishGrid.Controller));
    }

    [Fact]
    public void BuildRunnerFixtureWhenOutputIsRequested()
    {
        string? output = Environment.GetEnvironmentVariable("JAMM_FISH_GRID_FIXTURE");
        if (string.IsNullOrEmpty(output)) return;
        string original = Environment.GetEnvironmentVariable("JAMM_ORIGINAL")
            ?? throw new InvalidOperationException("Set JAMM_ORIGINAL to the untouched legacy archive.");
        using var input = File.OpenRead(original);
        using var data = UndertaleIO.Read(input);
        Assert.Contains(": enabled", FishGrid.Apply(data));
        var fixture = ModObjects.Create(data, "obj_jamm_fg_fixture", false);
        fixture.ParentId = data.GameObjects.ByName("FishLarge");
        var controller = data.GameObjects.ByName(FishGrid.Controller)!;
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(fixture.EventHandlerFor(EventType.Create, 0u, data), "fixture = true;");
        compiler.QueueCodeReplace(fixture.EventHandlerFor(EventType.Destroy, 0u, data), "fixture = false;");
        using var resource = typeof(FishGridTests).Assembly.GetManifestResourceStream("Jamm.Core.Tests.Gml.fish_grid.gml")!;
        using var source = new StreamReader(resource);
        compiler.QueueCodeReplace(controller.EventHandlerFor(EventType.Other, (uint)EventSubtypeOther.RoomStart, data),
            FishGridScripts.Reset + "\nif (room == NewWorld) {\n" + source.ReadToEnd() + "\n}");
        ModObjects.Compile(compiler);
        BenchmarkMod.Apply(data, new BenchmarkOptions(WarmupSeconds: 1, CaptureSeconds: 5));
        using var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
        UndertaleIO.Write(destination, data);
    }
}
