using Jamm.Core.Building;
using Jamm.Core.Diffing;
using Jamm.Core.Merging;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

public sealed partial class BuildingTests
{
    [Fact]
    public void AuditProtectsDiscoveredArchivesAndInstallationFilesIncludingAliases()
    {
        using var temp = new TemporaryDirectory();
        string baseline = temp.PathFor("base.win"), archive = temp.PathFor("mod.archive"), patchPath = temp.PathFor("mod.patch.json");
        WriteModel(baseline, _ => { }); WriteModel(archive, d => d.GameObjects[0].Depth = 7);
        using var original = GameFileLoader.Load(baseline); using var mod = GameFileLoader.Load(archive);
        var patch = PatchBuilder.Create(original, mod);
        File.WriteAllText(patchPath, PatchJson.Serialize(patch));
        string hash = BuildFiles.Hash(archive);
        string[] arguments = [patchPath, "--asset-literals", "--base", baseline, "--json"];
        Assert.Throws<ArgumentException>(() => Jamm.Cli.CheckCommand.Run([.. arguments, archive]));
        foreach (string suffix in new[] { ".cgfmm-original", ".cgfmm-build.json" })
        {
            string protectedPath = temp.PathFor("data.win" + suffix);
            File.Copy(archive, protectedPath);
            Assert.Throws<ArgumentException>(() => Jamm.Cli.CheckCommand.Run([.. arguments, protectedPath]));
            Assert.Equal(hash, BuildFiles.Hash(protectedPath));
            // The backup can itself be the patch's discovered payload.
            File.WriteAllText(patchPath, PatchJson.Serialize(patch with { Mod = patch.Mod with { FileName = Path.GetFileName(protectedPath) } }));
            Assert.Throws<ArgumentException>(() => Jamm.Cli.CheckCommand.Run([.. arguments, protectedPath]));
            File.WriteAllText(patchPath, PatchJson.Serialize(patch));
            if (!OperatingSystem.IsWindows())
            {
                string alias = temp.PathFor("alias" + suffix + ".report");
                File.CreateSymbolicLink(alias, protectedPath);
                Assert.Throws<ArgumentException>(() => Jamm.Cli.CheckCommand.Run([.. arguments, alias]));
            }
        }
        if (!OperatingSystem.IsWindows())
        {
            string alias = temp.PathFor("archive-alias.report"); File.CreateSymbolicLink(alias, archive);
            Assert.Throws<ArgumentException>(() => Jamm.Cli.CheckCommand.Run([.. arguments, alias]));
        }
        Assert.Equal(0, Jamm.Cli.CheckCommand.Run([.. arguments, temp.PathFor("audit.json")]));
        Assert.Equal(hash, BuildFiles.Hash(archive));
    }

    [Theory]
    [InlineData("objects")]
    [InlineData("scripts")]
    [InlineData("timelines")]
    public void RetainedBaseAssetsKeepTheirRemovedCodeEvenWhenEveryModDropsTheReference(string family)
    {
        using var temp = new TemporaryDirectory();
        string baseline = temp.PathFor("base.win"), deleted = temp.PathFor("deleted.win"), edited = temp.PathFor("edited.win"), output = temp.PathFor("merged.win");
        WriteModel(baseline, Setup); WriteModel(deleted, d =>
        {
            Setup(d);
            if (family == "objects") { d.Rooms[0].GameObjects.Clear(); d.GameObjects.Clear(); }
            if (family == "scripts") d.Scripts.Clear();
            if (family == "timelines") d.Timelines.Clear();
            d.Code.Remove(d.Code.Single(c => c.Name.Content == "asset_code"));
            foreach (var local in d.CodeLocals.Where(l => l.Name.Content == "asset_code").ToArray()) d.CodeLocals.Remove(local);
        });
        WriteModel(edited, d =>
        {
            Setup(d);
            if (family == "objects") d.GameObjects[0].Depth = 7;
            if (family == "scripts") d.Scripts[0].IsConstructor = true;
            if (family == "timelines") d.Timelines[0].Moments[0].Step = 1;
            var compiler = new CompileGroup(d); compiler.QueueCodeReplace(d.Code.Single(c => c.Name.Content == "asset_code"), "value = 18;"); Assert.True(compiler.Compile().Successful);
        });
        using var original = GameFileLoader.Load(baseline); using var mod = GameFileLoader.Load(deleted);
        using var editedMod = GameFileLoader.Load(edited);
        var patch = PatchBuilder.Create(original, mod, "deleted");
        var edit = PatchBuilder.Create(original, editedMod, "edited");
        Assert.Contains("asset_code", patch.Changes["code"].Removed);
        Assert.DoesNotContain(patch.References!, r => r.Target == "asset_code");
        foreach (var resolutions in new IReadOnlyDictionary<string, string>?[] { null, new Dictionary<string, string> { ["code/asset_code"] = "deleted" } })
        {
            var build = ArchiveBuilder.Build(baseline, [new(patch, deleted)], output, resolutions);
            Assert.NotNull(build.Verification);
            Assert.DoesNotContain(build.Plan.Assets, a => a.Family == "code" && a.Name == "asset_code" && a.Patch is null);
            using var merged = GameFileLoader.Load(output);
            Assert.Equal(PatchBuilder.ReadCode(original, "asset_code").Text, PatchBuilder.ReadCode(merged, "asset_code").Text);
        }
        Assert.NotNull(ArchiveBuilder.Build(baseline, [new(edit, edited), new(patch, deleted)], output).Verification);
        using (var merged = GameFileLoader.Load(output))
            Assert.Equal(PatchBuilder.ReadCode(editedMod, "asset_code").Text, PatchBuilder.ReadCode(merged, "asset_code").Text);

        void Setup(UndertaleData data)
        {
            var code = UndertaleCode.CreateEmptyEntry(data, "asset_code");
            var compiler = new CompileGroup(data); compiler.QueueCodeReplace(code, "value = 17;"); Assert.True(compiler.Compile().Successful);
            if (family == "objects") data.GameObjects[0].Events[0].Add(new() { Actions = [new() { CodeId = code, ActionName = data.Strings.MakeString("") }] });
            if (family == "scripts") data.Scripts.Add(new() { Name = data.Strings.MakeString("retained_script"), Code = code });
            if (family == "timelines") data.Timelines.Add(new() { Name = data.Strings.MakeString("retained_timeline"), Moments = [new() { Step = 0, Event = [new() { CodeId = code, ActionName = data.Strings.MakeString("") }] }] });
        }
    }

    [Theory]
    [InlineData("instance_create(0, 0, 999999);", "objects", "instance_create")]
    [InlineData("instance_create(max(0, 1), a[1, 2], (999999));", "objects", "instance_create")]
    [InlineData("instance_create_depth(0, 0, 0, 999999);", "objects", "instance_create_depth")]
    [InlineData("instance_create(0, 0, $F423F);", "objects", "instance_create")]
    [InlineData("instance_create(0, 0, +0xF423F);", "objects", "instance_create")]
    [InlineData("instance_create(0, 0, 1e100);", "objects", "instance_create")]
    [InlineData("sprite_assign(0, 999999);", "sprites", "sprite_assign")]
    [InlineData("draw_sprite(999999, 0, 0, 0);", "sprites", "draw_sprite")]
    [InlineData("audio_play_sound(999999, 1, false);", "sounds", "audio_play_sound")]
    [InlineData("room_goto(999999);", "rooms", "room_goto")]
    [InlineData("path_start(999999, 1, 0, false);", "paths", "path_start")]
    [InlineData("draw_set_font(999999);", "fonts", "draw_set_font")]
    [InlineData("draw_background(999999, 0, 0);", "backgrounds", "draw_background")]
    [InlineData("script_get_name(999999);", "scripts", "script_get_name")]
    [InlineData("script_execute(999999, 0, 0);", "scripts", "script_execute")]
    [InlineData("timeline_exists(999999);", "timelines", "timeline_exists")]
    [InlineData("shader_set(999999);", "shaders", "shader_set")]
    [InlineData("event_perform_object(0, 4, 999999);", "objects", "event_perform_object")]
    [InlineData("event_perform_object(0, ev_collision, 999999);", "objects", "event_perform_object")]
    [InlineData("event_perform(ev_collision, 999999);", "objects", "event_perform")]
    public void SemanticChecksRejectUnresolvedBuiltinAssetLiterals(string text, string family, string function)
    {
        var tables = new Dictionary<string, string[]> { [family] = ["first"] };
        var error = Assert.Throws<InvalidDataException>(() => SemanticReferences.Resolve(text, "invalid_code", tables));
        Assert.Contains("invalid_code", error.Message); Assert.Contains(function, error.Message);
        Assert.Contains($"invalid {family} index", error.Message);
    }

    [Fact]
    public void BuiltinChecksPreserveNegativeSentinelsExpressionsAndUntypedArguments()
    {
        var tables = new Dictionary<string, string[]> { ["objects"] = ["fish"], ["sprites"] = ["sprite"] };
        const string text = "instance_create(999999, max(999999, 0), -1); draw_set_font(-1); instance_create(0, 0, offset + 999999); cash = 999999; label = \"instance_create(0, 0, 999999)\"; // sprite_assign(0, 999999)";
        Assert.Equal(text, SemanticReferences.NameTypedLiterals(text, "valid_code", tables));
        Assert.Equal("sprite_assign(sprite, sprite);", SemanticReferences.NameTypedLiterals("sprite_assign(0, 0);", "valid_code", tables));
        Assert.Equal("event_perform_object(fish, 3, 999999);", SemanticReferences.NameTypedLiterals("event_perform_object(0, 3, 999999);", "valid_code", tables));
        Assert.Equal("instance_create(string_length(\"),[\"), 0, fish);", SemanticReferences.NameTypedLiterals("instance_create(string_length(\"),[\"), 0, 0);", "valid_code", tables));
    }

    [Fact]
    public void BuildRejectsInvalidBuiltinIndicesFromLegacyPatchesWithoutPublishing()
    {
        using var temp = new TemporaryDirectory();
        string baseline = temp.PathFor("base.win"), invalid = temp.PathFor("invalid.win"), output = temp.PathFor("merged.win");
        WriteModel(baseline, _ => { }); WriteModel(invalid, d =>
        {
            var compiler = new CompileGroup(d); compiler.QueueCodeReplace(d.Code[0], "instance_create(0, 0, 999999);"); Assert.True(compiler.Compile().Successful);
        });
        using var original = GameFileLoader.Load(baseline); using var mod = GameFileLoader.Load(invalid);
        var patch = PatchBuilder.Create(original, original);
        patch = patch with { Mod = patch.Mod with { FileName = Path.GetFileName(invalid), Sha256 = mod.Inspection.Sha256 }, AssetTypesVersion = 0 };
        File.WriteAllText(output, "previous output");
        string baseHash = BuildFiles.Hash(baseline), modHash = BuildFiles.Hash(invalid), outputHash = BuildFiles.Hash(output);
        var error = Assert.Throws<InvalidDataException>(() => ArchiveBuilder.Build(baseline, [new(patch, invalid)], output));
        Assert.Contains("test_code", error.Message); Assert.Contains("instance_create", error.Message); Assert.Contains("999999", error.Message);
        Assert.Equal(outputHash, BuildFiles.Hash(output)); Assert.Equal(baseHash, BuildFiles.Hash(baseline)); Assert.Equal(modHash, BuildFiles.Hash(invalid));
    }

    [Fact]
    public void CheckReportsSemanticOnlyCodeConflictsAndBuildUsesTheSameWinner()
    {
        using var temp = new TemporaryDirectory();
        string baseline = temp.PathFor("base.win"), aPath = temp.PathFor("a.win"), bPath = temp.PathFor("b.win");
        WriteModel(baseline, d => Compile(d, "object[0] = 0;"));
        WriteModel(aPath, d => { d.GameObjects.Add(new() { Name = d.Strings.MakeString("added_fish") }); Compile(d, "object[0] = 1;"); });
        WriteModel(bPath, d => { d.Rooms[0].GameObjects.Clear(); d.GameObjects.Clear(); d.GameObjects.Add(new() { Name = d.Strings.MakeString("replacement_fish") }); Compile(d, "object[0] = 0;"); });
        using var original = GameFileLoader.Load(baseline); using var a = GameFileLoader.Load(aPath); using var b = GameFileLoader.Load(bPath);
        Assert.Equal(ContentFingerprints.Code(original.Data.Code[0]), ContentFingerprints.Code(b.Data.Code[0]));
        var pa = PatchBuilder.Create(original, a, "A"); var pb = PatchBuilder.Create(original, b, "B");
        var semantic = Assert.Single(pb.Changes["code"].Changed);
        Assert.Contains("fish", semantic.BaseCode!.Text); Assert.Contains("replacement_fish", semantic.ModCode!.Text);
        Assert.Contains(ConflictAnalyzer.Analyze([pa, pb]).Findings, f => f.Severity == "CONFLICT" && f.Family == "code" && f.Asset == "test_code");
        string patchA = temp.PathFor("a.patch.json"), patchB = temp.PathFor("b.patch.json");
        File.WriteAllText(patchA, PatchJson.Serialize(pa)); File.WriteAllText(patchB, PatchJson.Serialize(pb));
        Assert.Equal(3, Jamm.Cli.CheckCommand.Run([patchA, patchB, "--base", baseline, "--json", temp.PathFor("report.json")]));
        foreach (var inputs in new[] { new[] { new BuildInput(pa, aPath), new BuildInput(pb, bPath) }, new[] { new BuildInput(pb, bPath), new BuildInput(pa, aPath) } })
        {
            var expected = BuildPlan.Create(inputs.Select(i => i.Patch).ToArray());
            var built = ArchiveBuilder.Build(baseline, inputs, temp.PathFor("merged.win"));
            Assert.Equal(expected.Assets.Single(a => a.Family == "code").Gml, built.Plan.Assets.Single(a => a.Family == "code").Gml);
            Assert.Equal(expected.Decisions, built.Plan.Decisions);
        }

        static void Compile(UndertaleData data, string text)
        {
            var compiler = new CompileGroup(data); compiler.QueueCodeReplace(data.Code[0], text); Assert.True(compiler.Compile().Successful);
        }
    }
}
