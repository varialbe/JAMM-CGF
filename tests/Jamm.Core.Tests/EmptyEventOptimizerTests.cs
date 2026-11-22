using Jamm.Core.Building;
using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;
using System.Security.Cryptography;

namespace Jamm.Core.Tests;

public class EmptyEventOptimizerTests
{
    [Fact]
    public void RemovesEmptyHandlerWhenItCannotExposeParentBehaviorAndIsIdempotent()
    {
        using var data = UndertaleData.CreateNew();
        var obj = AddObject(data, "Rock");
        AddEmptyStep(data, obj);

        var result = EmptyEventOptimizer.Apply(data);
        Assert.Equal(["Rock_Step_2"], result.Removed);
        Assert.Empty(result.Skipped);
        Assert.Empty(obj.Events[(int)EventType.Step]);
        Assert.Empty(EmptyEventOptimizer.Apply(data).Removed);
    }

    [Fact]
    public void KeepsEmptyOverrideWhenItWouldExposeAnExecutableParent()
    {
        using var data = UndertaleData.CreateNew();
        var parent = AddObject(data, "Parent");
        AddExecutableStep(data, parent);
        var child = AddObject(data, "Child", parent);
        AddEmptyStep(data, child);

        var result = EmptyEventOptimizer.Apply(data);
        Assert.Empty(result.Removed);
        Assert.Equal(["Child_Step_2"], result.Skipped);
        Assert.Single(child.Events[(int)EventType.Step]);
    }

    [Fact]
    public void KeepsModAddedHandlersAndEmptyDescendantOverridesThatWouldExposeThem()
    {
        using var data = UndertaleData.CreateNew();
        var root = AddObject(data, "Root");
        AddExecutableStep(data, root);
        var added = AddObject(data, "ModAdded", root);
        AddExecutableStep(data, added);
        var descendant = AddObject(data, "Descendant", added);
        AddEmptyStep(data, descendant);

        var result = EmptyEventOptimizer.Apply(data);
        Assert.Empty(result.Removed);
        Assert.Equal(["Descendant_Step_2"], result.Skipped);
        Assert.Single(added.Events[(int)EventType.Step]);
        Assert.Single(descendant.Events[(int)EventType.Step]);
    }

    [Fact]
    public void RemovesInheritedOnlyNoOpWhenParentChainHasNoHandler()
    {
        using var data = UndertaleData.CreateNew();
        var parent = AddObject(data, "Parent");
        var child = AddObject(data, "Child", parent);
        var code = UndertaleCode.CreateEmptyEntry(data, "gml_Object_Child_3_2");
        data.Code.Add(code);
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(code, "action_inherited();");
        ModObjects.Compile(compiler);
        child.Events[(int)EventType.Step].Add(new()
        {
            EventSubtype = (uint)EventSubtypeStep.EndStep,
            Actions = [new() { CodeId = code }]
        });

        Assert.Equal(["Child_Step_2"], EmptyEventOptimizer.Apply(data).Removed);
        Assert.Empty(child.Events[(int)EventType.Step]);
    }

    [Fact]
    public void NoModBuildRunsTheFinalMergePassWithoutChangingItsInput()
    {
        using var temp = new TemporaryDirectory();
        string input = temp.PathFor("input.win");
        string output = temp.PathFor("output.win");
        using (var data = UndertaleData.CreateNew())
        {
            data.GeneralInfo.Major = 1;
            data.GeneralInfo.Build = 1773;
            var code = UndertaleCode.CreateEmptyEntry(data, "test_code");
            var compiler = new CompileGroup(data);
            compiler.QueueCodeReplace(code, "x = 1;");
            ModObjects.Compile(compiler);
            var rock = AddObject(data, "Rock");
            AddEmptyStep(data, rock);
            data.Rooms[0].GameObjects.Add(new() { ObjectDefinition = rock, X = 10, Y = 20, InstanceID = 42 });
            Assert.Single(rock.Events[(int)EventType.Step]);
            using var stream = File.Create(input);
            UndertaleIO.Write(stream, data);
        }
        using (var loadedInput = GameFileLoader.Load(input))
            Assert.Single(loadedInput.Data.GameObjects.ByName("Rock")!.Events[(int)EventType.Step]);
        byte[] originalHash = SHA256.HashData(File.ReadAllBytes(input));

        var result = ArchiveBuilder.Build(input, [], output);

        Assert.Contains(result.Plan.Decisions, decision => decision.Contains("Pruned verified no-op event Rock_Step_2", StringComparison.Ordinal));
        using var merged = GameFileLoader.Load(output);
        Assert.Empty(merged.Data.GameObjects.ByName("Rock")!.Events[(int)EventType.Step]);
        Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(input)));
    }

    private static UndertaleGameObject AddObject(UndertaleData data, string name, UndertaleGameObject? parent = null)
    {
        var obj = new UndertaleGameObject { Name = data.Strings.MakeString(name), ParentId = parent };
        data.GameObjects.Add(obj);
        return obj;
    }

    private static void AddEmptyStep(UndertaleData data, UndertaleGameObject obj)
    {
        var code = UndertaleCode.CreateEmptyEntry(data, $"gml_Object_{obj.Name.Content}_3_2");
        obj.Events[(int)EventType.Step].Add(new()
        {
            EventSubtype = (uint)EventSubtypeStep.EndStep,
            Actions = [new() { CodeId = code }]
        });
    }

    private static void AddExecutableStep(UndertaleData data, UndertaleGameObject obj)
    {
        var code = UndertaleCode.CreateEmptyEntry(data, $"gml_Object_{obj.Name.Content}_3_2");
        code.Instructions.Add(new() { Kind = UndertaleInstruction.Opcode.Exit, Type1 = UndertaleInstruction.DataType.Int32 });
        code.UpdateLength();
        obj.Events[(int)EventType.Step].Add(new()
        {
            EventSubtype = (uint)EventSubtypeStep.EndStep,
            Actions = [new() { CodeId = code }]
        });
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "jamm-empty-event-" + Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory() => Directory.CreateDirectory(directory);
        internal string PathFor(string name) => Path.Combine(directory, name);
        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
