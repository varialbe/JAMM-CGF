using System.Text.Json;
using Jamm.Core.Building;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace Jamm.Core.Tests;

public sealed partial class BuildingTests
{
    [Fact]
    public void LaterFieldAndCodeEditsMergeAfterDeletion()
    {
        const string ancestor = "x = 1;\ny = 2;\n";
        var deleted = Patch("deleted", ancestor, ancestor);
        deleted.Changes["code"] = new([], [], ["test_code"]);
        deleted.Changes["objects"] = new([], [], ["fish"]);
        var a = Patch("A", ancestor, "x = 10;\ny = 2;\n");
        var b = Patch("B", ancestor, "x = 1;\ny = 20;\n");
        var baseline = JsonSerializer.SerializeToElement(new { depth = 0, visible = true });
        AddChange(a, "objects", "fish", baseline, JsonSerializer.SerializeToElement(new { depth = 7, visible = true }));
        AddChange(b, "objects", "fish", baseline, JsonSerializer.SerializeToElement(new { depth = 0, visible = false }));
        var plan = BuildPlan.Create([deleted, a, b]);
        Assert.Equal("x = 10;\ny = 20;\n", plan.Assets.Single(x => x.Family == "code").Gml);
        var snapshot = plan.Assets.Single(x => x.Family == "objects").Snapshot!.Value;
        Assert.Equal(7, snapshot.GetProperty("depth").GetInt32());
        Assert.False(snapshot.GetProperty("visible").GetBoolean());
    }

    [Fact]
    public void ResolutionsAreQualifiedAndBareChoicesOnlyAffectConflictingFamilies()
    {
        var a = Patch("A", "start\nend\n", "start\na = 1;\nend\n");
        var b = Patch("B", "start\nend\n", "start\nb = 2;\nend\n");
        var script = JsonSerializer.SerializeToElement(new { code = "test_code", isConstructor = false });
        var function = JsonSerializer.SerializeToElement(new { classification = 0 });
        AddChange(a, "scripts", "test_code", null, script);
        AddChange(a, "functions", "test_code", null, function);
        AddChange(a, "strings", "test_code", null, JsonSerializer.SerializeToElement(new { value = "test_code" }));
        var plan = BuildPlan.Create([a, b], new Dictionary<string, string> { ["test_code"] = "keep-both" });
        Assert.Equal("start\na = 1;\nb = 2;\nend\n", plan.Assets.Single(x => x.Family == "code").Gml);
        Assert.Contains(plan.Assets, x => x.Family == "scripts");
        plan = BuildPlan.Create([a, b], new Dictionary<string, string> { ["test_code"] = "B" });
        Assert.Contains(plan.Assets, x => x.Family == "scripts" && x.Source == 0);
        plan = BuildPlan.Create([a, b], new Dictionary<string, string> { ["scripts/test_code"] = "A", ["code/test_code"] = "B" });
        Assert.Contains(plan.Assets, x => x.Family == "scripts" && x.Source == 0);
        Assert.Equal("start\nb = 2;\nend\n", plan.Assets.Single(x => x.Family == "code").Gml);
        Assert.Throws<ArgumentException>(() => BuildPlan.Create([a, b], new Dictionary<string, string> { ["scripts/test_code"] = "keep-both" }));
    }

    [Fact]
    public void SelectedCodeModPreservesOtherDisjointEditsAndLoadOrderForOtherConflicts()
    {
        const string ancestor = "x = 1;\ny = 2;\nz = 3;\n";
        var a = Patch("A", ancestor, "x = 10;\ny = 2;\nz = 3;\n");
        var b = Patch("B", ancestor, "x = 1;\ny = 20;\nz = 3;\n");
        var c = Patch("C", ancestor, "x = 30;\ny = 2;\nz = 40;\n");
        var plan = BuildPlan.Create([a, b, c], new Dictionary<string, string> { ["test_code"] = "B" });
        Assert.Equal("x = 30;\ny = 20;\nz = 40;\n", Assert.Single(plan.Assets).Gml);
    }

    [Fact]
    public void IndexedBaseRetentionPreservesEarlierEditsEvenWhenDeletionWins()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), editedPath = temp.PathFor("edited.win"), deletedPath = temp.PathFor("deleted.win"), referencePath = temp.PathFor("reference.win");
        WriteModel(basePath, _ => { });
        WriteModel(editedPath, d => d.GameObjects[0].Depth = 15);
        WriteModel(deletedPath, d => { d.Rooms[0].GameObjects.Clear(); d.GameObjects.Clear(); });
        WriteModel(referencePath, d =>
        {
            var compiler = new CompileGroup(d); compiler.QueueCodeReplace(d.Code[0], "instance_create(0, 0, fish);\n"); Assert.True(compiler.Compile().Successful);
        });
        using var original = GameFileLoader.Load(basePath); using var edited = GameFileLoader.Load(editedPath);
        using var deleted = GameFileLoader.Load(deletedPath); using var reference = GameFileLoader.Load(referencePath);
        var e = PatchBuilder.Create(original, edited, "edited"); var d = PatchBuilder.Create(original, deleted, "deleted");
        var r = PatchBuilder.Create(original, reference, "reference");
        Assert.Contains("fish", Assert.Single(r.Changes["code"].Changed).ModCode!.Text);
        var plan = BuildPlan.Create([e, d, r]);
        Assert.Equal(15, plan.Assets.Single(a => a.Family == "objects" && a.Name == "fish").Snapshot!.Value.GetProperty("depth").GetInt32());
        foreach (var retained in new[] { BuildPlan.Create([r, d]), BuildPlan.Create([d, r], new Dictionary<string, string> { ["objects/fish"] = "deleted" }) })
        {
            Assert.DoesNotContain(retained.Assets, a => a.Family == "objects" && a.Patch is null);
            Assert.Contains(retained.Decisions, message => message.Contains("retained objects/fish", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ReferenceCheckUsesBaseSnapshotForUnchangedResolution()
    {
        var a = Patch("A", "x = 1;\n", "x = 1;\n");
        var b = Patch("B", "x = 1;\n", "x = 1;\n");
        var baseline = JsonSerializer.SerializeToElement(new { sprite = (string?)null });
        AddChange(a, "objects", "fish", baseline, JsonSerializer.SerializeToElement(new { sprite = "removed_sprite" }));
        a.Changes["sprites"] = new([], [], ["removed_sprite"]);
        b.BaseAssets!["objects"] = ["fish"];
        var plan = BuildPlan.Create([a, b], new Dictionary<string, string> { ["objects/fish"] = "B" });
        Assert.DoesNotContain(plan.Assets, x => x.Family == "sprites" && x.Patch is null);
        Assert.Contains(plan.Decisions, x => x.Contains("retained sprites/removed_sprite", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Assets, x => x.Family == "objects");
    }

    [Fact]
    public void EventAndTimelineMetadataAndRoomOrderComeFromSelectedMod()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), modPath = temp.PathFor("mod.win");
        WriteModel(basePath, d => Setup(d, false)); WriteModel(modPath, d => Setup(d, true));
        using var original = GameFileLoader.Load(basePath); using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "mod");
        string output = temp.PathFor("result.win");
        ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);
        using var result = GameFileLoader.Load(output);
        var action = result.Data.GameObjects[0].Events[0][0].Actions[0];
        Assert.True(action.Relative); Assert.False(action.UseApplyTo); Assert.Equal(-2, action.Who);
        action = result.Data.Timelines[0].Moments[0].Event[0];
        Assert.True(action.Relative); Assert.False(action.UseApplyTo); Assert.Equal(-2, action.Who);
        Assert.Equal(new[] { 30, 10, 20 }, result.Data.Rooms[0].GameObjects.Select(i => i.X));
        Assert.Equal(new[] { 30, 10, 20 }, result.Data.Rooms[0].Tiles.Select(t => t.X));
        static void Setup(UndertaleData data, bool changed)
        {
            data.GameObjects[0].Events[0].Add(new() { Actions = [Action(data, changed)] });
            data.Timelines.Add(new() { Name = data.Strings.MakeString("timeline"), Moments = [new() { Step = 0, Event = [Action(data, changed)] }] });
            data.Backgrounds.Add(new() { Name = data.Strings.MakeString("background") });
            data.Rooms[0].GameObjects.Add(new() { ObjectDefinition = data.GameObjects[0], X = 30, Y = 20, InstanceID = 43 });
            foreach (uint x in new uint[] { 10, 30 }) data.Rooms[0].Tiles.Add(new() { BackgroundDefinition = data.Backgrounds[0], X = (int)x, Width = 1, Height = 1, InstanceID = x });
            if (!changed) return;
            data.GameObjects[0].Depth = 7; data.Timelines[0].Moments.Add(new() { Step = 1, Event = [] });
            var firstInstance = data.Rooms[0].GameObjects[0]; data.Rooms[0].GameObjects.RemoveAt(0); data.Rooms[0].GameObjects.Add(firstInstance);
            data.Rooms[0].GameObjects.Add(new() { ObjectDefinition = data.GameObjects[0], X = 20, Y = 20, InstanceID = 44 });
            var firstTile = data.Rooms[0].Tiles[0]; data.Rooms[0].Tiles.RemoveAt(0); data.Rooms[0].Tiles.Add(firstTile);
            data.Rooms[0].Tiles.Add(new() { BackgroundDefinition = data.Backgrounds[0], X = 20, Width = 1, Height = 1, InstanceID = 20 });
        }
        static UndertaleGameObject.EventAction Action(UndertaleData data, bool changed) => new()
        {
            CodeId = data.Code[0], ActionName = data.Strings.MakeString(""), Relative = changed, Who = changed ? -2 : -1, UseApplyTo = !changed
        };
    }

    [Fact]
    public void AddedAssetsKeepFirstIntroductionOrderAndLocalsKeepCompilerScopes()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), aPath = temp.PathFor("a.win"), bPath = temp.PathFor("b.win");
        WriteModel(basePath, _ => { });
        WriteModel(aPath, d =>
        {
            d.GameObjects.Add(new() { Name = d.Strings.MakeString("z_first") });
            d.GameObjects.Add(new() { Name = d.Strings.MakeString("a_second") });
            Compile(d, "scope_a", "var shared; shared = 5; x = shared;");
        });
        WriteModel(bPath, d =>
        {
            d.GameObjects.Add(new() { Name = d.Strings.MakeString("z_first"), Depth = 8 });
            Compile(d, "scope_b", "var paddingLocal, shared; paddingLocal = 3; shared = 7; x = shared + paddingLocal;");
        });
        using var original = GameFileLoader.Load(basePath); using var a = GameFileLoader.Load(aPath); using var b = GameFileLoader.Load(bPath);
        var pa = PatchBuilder.Create(original, a, "A"); var pb = PatchBuilder.Create(original, b, "B");
        string output = temp.PathFor("result.win");
        ArchiveBuilder.Build(basePath, [new(pa, aPath), new(pb, bPath)], output);
        using var result = GameFileLoader.Load(output);
        Assert.Equal(new[] { "fish", "z_first", "a_second" }, result.Data.GameObjects.Select(o => o.Name.Content));
        Assert.Equal(8, result.Data.GameObjects[1].Depth);
        Assert.NotEqual(a.Data.Code.Single(c => c.Name.Content == "scope_a").FindReferencedLocalVars().Single(v => v.Name.Content == "shared").VarID,
            b.Data.Code.Single(c => c.Name.Content == "scope_b").FindReferencedLocalVars().Single(v => v.Name.Content == "shared").VarID);
        foreach (var source in new[] { a, b })
        {
            var code = source.Data.Code.Single(c => c.Name.Content.StartsWith("scope_", StringComparison.Ordinal));
            var expected = code.FindReferencedLocalVars().Single(v => v.Name.Content == "shared");
            var actual = result.Data.Code.Single(c => c.Name.Content == code.Name.Content).FindReferencedLocalVars().Single(v => v.Name.Content == "shared");
            Assert.Equal(expected.VarID, actual.VarID);
        }
        static void Compile(UndertaleData data, string name, string gml)
        {
            var code = UndertaleCode.CreateEmptyEntry(data, name); var compiler = new CompileGroup(data);
            compiler.QueueCodeReplace(code, gml); Assert.True(compiler.Compile().Successful);
        }
    }

    [Fact]
    public void NestedLinkTargetsAndLinkCyclesAreHandledWithoutTouchingInputs()
    {
        if (OperatingSystem.IsWindows()) return;
        using var temp = new TemporaryDirectory();
        string directory = temp.PathFor("real"); Directory.CreateDirectory(directory);
        string original = System.IO.Path.Combine(directory, "original.win"); File.WriteAllText(original, "original");
        string parent = temp.PathFor("linked-parent"); Directory.CreateSymbolicLink(parent, directory);
        string alias = temp.PathFor("alias.win"); File.CreateSymbolicLink(alias, System.IO.Path.Combine(parent, "original.win"));
        Assert.Equal(original, BuildFiles.Resolve(alias));
        Assert.Throws<IOException>(() => BuildFiles.ValidateNewOutput(alias, [original]));
        Assert.Throws<IOException>(() => BuildFiles.ValidateNewOutput(original, [alias]));
        string cycle = temp.PathFor("cycle"); File.CreateSymbolicLink(cycle, cycle);
        Assert.Contains("loop", Assert.Throws<IOException>(() => BuildFiles.Resolve(cycle)).Message);
        Assert.Equal("original", File.ReadAllText(original));
    }

    [Fact]
    public void CliCanRepeatBuildDryRunAndInstallIntoBaseDirectoryWithVerifiedBackup()
    {
        using var temp = new TemporaryDirectory();
        string game = temp.PathFor("game"); Directory.CreateDirectory(game);
        string original = System.IO.Path.Combine(game, "data.win"), mod = temp.PathFor("mod.win"), output = temp.PathFor("build/data.win");
        WriteModel(original, _ => { }); WriteModel(mod, d => d.GameObjects[0].Depth = 8);
        string baseHash = BuildFiles.Hash(original), modHash = BuildFiles.Hash(mod);
        string[] arguments = ["--base", original, "--order", mod, "--out", output];
        Assert.Equal(0, Jamm.Cli.BuildCommand.Run("build", arguments));
        string buildHash = BuildFiles.Hash(output);
        Assert.Equal(0, Jamm.Cli.BuildCommand.Run("build", [.. arguments, "--dry-run"]));
        Assert.Equal(buildHash, BuildFiles.Hash(output));
        Assert.Equal(0, Jamm.Cli.BuildCommand.Run("build", arguments));
        Assert.Equal(0, Jamm.Cli.BuildCommand.Run("build", [.. arguments, "--install", game]));
        Assert.Equal(baseHash, BuildFiles.Hash(original + ".cgfmm-original"));
        Assert.Equal(BuildFiles.Hash(output), BuildFiles.Hash(original));
        Assert.Throws<IOException>(() => BuildFiles.ValidateNewOutput(original, []));
        Assert.Throws<IOException>(() => GameInstaller.Install(output, game, baseHash, [original], original));
        Assert.Equal(baseHash, GameInstaller.Restore(game));
        Assert.Equal(modHash, BuildFiles.Hash(mod));
        string empty = temp.PathFor("empty"); Directory.CreateDirectory(empty);
        var error = Assert.Throws<FileNotFoundException>(() => GameInstaller.Validate(empty, baseHash));
        Assert.Contains("data.win", error.Message); Assert.Contains("Choose", error.Message);
    }

    [Fact]
    public void MissingStructuredDependenciesNameTheAssetAndRemedy()
    {
        using var temp = new TemporaryDirectory();
        string original = temp.PathFor("base.win"), modPath = temp.PathFor("mod.win");
        WriteModel(original, _ => { }); WriteModel(modPath, d => d.GameObjects[0].Depth = 1);
        using var baseline = GameFileLoader.Load(original); using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(baseline, mod);
        var value = JsonSerializer.SerializeToElement(new { events = new Dictionary<string, object> { ["0:0"] = new { code = new[] { "missing_code" } } } });
        AddChange(patch, "objects", "fish", JsonSerializer.SerializeToElement(new { events = new { } }), value);
        var error = Assert.Throws<InvalidDataException>(() => ArchiveBuilder.Build(original, [new(patch, modPath)], temp.PathFor("output.win")));
        Assert.Contains("fish", error.Message); Assert.Contains("missing_code", error.Message); Assert.Contains("--resolve", error.Message);
    }

    [Fact]
    public void MergedFontImportsOnlyItsSelectedTexture()
    {
        using var temp = new TemporaryDirectory();
        string baseline = temp.PathFor("base.win"), aPath = temp.PathFor("a.win"), bPath = temp.PathFor("b.win");
        WriteModel(baseline, d => Font(d, 10, "display"));
        WriteModel(aPath, d => Font(d, 20, "display"));
        WriteModel(bPath, d => Font(d, 10, "new display"));
        using var original = GameFileLoader.Load(baseline); using var a = GameFileLoader.Load(aPath); using var b = GameFileLoader.Load(bPath);
        var pa = PatchBuilder.Create(original, a, "A"); var pb = PatchBuilder.Create(original, b, "B");
        string output = temp.PathFor("result.win");
        ArchiveBuilder.Build(baseline, [new(pa, aPath), new(pb, bPath)], output);
        using var result = GameFileLoader.Load(output);
        Assert.Equal("new display", result.Data.Fonts[0].DisplayName.Content);
        Assert.Equal(new ContentFingerprints().Region(a.Data.Fonts[0].Texture), new ContentFingerprints().Region(result.Data.Fonts[0].Texture));
        Assert.Equal(2, result.Data.EmbeddedTextures.Count);
        Assert.Equal(2, result.Data.TexturePageItems.Count);
        static void Font(UndertaleData data, byte colour, string displayName)
        {
            var image = new GMImage(1, 1); image.GetRawImageData()[0] = colour;
            var page = new UndertaleEmbeddedTexture { Name = new("page"), TextureData = new() { Image = image.ConvertToPng() } };
            var item = new UndertaleTexturePageItem { Name = new("item"), TexturePage = page,
                SourceWidth = 1, SourceHeight = 1, TargetWidth = 1, TargetHeight = 1, BoundingWidth = 1, BoundingHeight = 1 };
            data.EmbeddedTextures.Add(page); data.TexturePageItems.Add(item);
            data.Fonts.Add(new() { Name = data.Strings.MakeString("font"), DisplayName = data.Strings.MakeString(displayName), Texture = item });
        }
    }

    private static void AddChange(ModPatch patch, string family, string name, JsonElement? baseline, JsonElement value)
    {
        var asset = new AssetPatch(name, baseline, value, FieldDiff.Compare(baseline, value));
        patch.Changes[family] = baseline is null ? new([asset], [], []) : new([], [asset], []);
    }
}
