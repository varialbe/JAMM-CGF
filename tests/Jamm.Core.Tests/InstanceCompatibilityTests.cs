using Jamm.Core.Building;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

public sealed class InstanceCompatibilityTests
{
    private const string Event = "gml_Object_Electron_Other_17";
    private const string Body = "if (Lure.fish > 0 && Lure.fish.object_index == Flash.object_index)\n{\n    fish_lunge();\n}\n";
    private static readonly PatchFile Source = new("mod.win", "test", 0, "1", 16, "Test mod");
    private static readonly ModPatch Patch = new(2, "test", DateTimeOffset.UnixEpoch, Source, Source, [], []);

    [Fact]
    public void RepairsKnownElectronEventAndPreservesDifferentBodies()
    {
        var known = Plan(Body);
        var other = Plan(Body + "Flash.speed = 0;");

        var repaired = InstanceCompatibility.Repair(known, [Patch]);

        Assert.Contains("== Flash)", Assert.Single(repaired.Assets).Gml);
        Assert.Contains("CGF-INSTANCE-001", Assert.Single(repaired.Decisions));
        Assert.Same(other, InstanceCompatibility.Repair(other, [Patch]));
    }

    [Fact]
    public void WarnsAboutCodeAccessWhenTheLastPlacedInstanceDisappears()
    {
        var original = WithRoom(placed: true);
        var merged = WithRoom(placed: false);
        var plan = Plan("// Flash.y\ntext = \"Flash.sprite_index\";\nx = Flash.x;\ny = holder.Flash.y;");

        string warning = Assert.Single(InstanceCompatibility.Diagnose(plan, [Patch], original, merged).Decisions);

        Assert.Contains("CGF-INSTANCE-002", warning);
        Assert.Contains("line 3", warning);
        Assert.Contains("Flash.x", warning);
        Assert.Contains("NewWorld", warning);
        Assert.Empty(InstanceCompatibility.Diagnose(plan, [Patch], original, original).Decisions);
    }

    private static BuildPlan Plan(string gml) => new([new("code", Event, 0, null, null, gml)], []);

    private static UndertaleData WithRoom(bool placed)
    {
        var data = UndertaleData.CreateNew();
        var flash = new UndertaleGameObject { Name = data.Strings.MakeString("Flash") };
        var room = new UndertaleRoom { Name = data.Strings.MakeString("NewWorld") };
        data.GameObjects.Add(flash);
        data.Rooms.Add(room);
        if (placed) room.GameObjects.Add(new() { ObjectDefinition = flash });
        return data;
    }
}
