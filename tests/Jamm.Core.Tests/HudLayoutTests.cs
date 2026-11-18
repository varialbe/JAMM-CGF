using Jamm.Core.Building;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

public sealed class HudLayoutTests
{
    private const string Anchors = "draw_x = round(view_xview[view_current] + offset_x); draw_y = round(view_yview[view_current] + offset_y); ";

    [Fact]
    public void AdaptsOnlyAddedProvenCallsAndPreservesBranchesExpressionsAndStrings()
    {
        string original = Anchors + "draw_text(draw_x - 10, draw_y + 10, \"Lvl\");";
        string mod = original + "if (debug) draw_text_shadowed(draw_x + 430, draw_y - 60, \"FPS:\" + string(fps));\n"
            + "// draw_text(draw_x + 430, draw_y - 40, fake);\n"
            + "draw_text(20, 30, \"draw_text(draw_x + 430, draw_y - 40, fake)\");\n"
            + "draw_text(draw_x + 430, draw_y + 10, below);";
        string result = HudTextAdapter.Rewrite(HudRuntime.Experience, mod, original, out int count);
        Assert.Equal(1, count);
        Assert.StartsWith(original, result);
        Assert.Contains("if (debug) jamm_hud_text(1, 1, \"FPS:\" + string(fps), 1, 1, 0)", result);
        Assert.Contains("draw_text(draw_x + 430, draw_y + 10, below)", result);
        Assert.Contains("// draw_text(draw_x + 430, draw_y - 40, fake);", result);
        Assert.Equal(result, HudTextAdapter.Rewrite(HudRuntime.Experience, result, original, out count));
        Assert.Equal(0, count);
    }

    [Fact]
    public void LeavesExistingLinesUnknownAnchorsAndRotatedCallsUntouched()
    {
        string text = Anchors + "draw_text(draw_x + 430, draw_y - 20, value);";
        Assert.Equal(text, HudTextAdapter.Rewrite(HudRuntime.Experience, text, text, out _));
        string unknown = text.Replace("offset_y", "unknown");
        Assert.Equal(unknown, HudTextAdapter.Rewrite(HudRuntime.Experience, unknown, "", out _));
        Assert.Equal(text, HudTextAdapter.Rewrite("gml_Object_Other_Draw_0", text, "", out _));
        string rotated = Anchors + "draw_text_transformed(draw_x + 430, draw_y - 20, value, 1, 1, 45);";
        Assert.Equal(rotated, HudTextAdapter.Rewrite(HudRuntime.Experience, rotated, "", out _));
    }

    [Fact]
    public void AdaptsDirectViewCoordinatesAndAllQuestBranches()
    {
        string text = "draw_text(view_xview[view_current] + view_wview[view_current] - 25, view_yview[view_current] + 412, label);";
        Assert.Contains("jamm_hud_text(1, 0, label, 1, 1, 0)", HudTextAdapter.Rewrite("gml_Object_Debug_Draw_0", text, "", out _));
        string quest = "if (complete) draw_text_transformed(draw_x, draw_y, reward, 0.5, 0.5, 0); else draw_text_transformed(draw_x, draw_y, label, 0.5, 0.5, 0);";
        string adapted = HudTextAdapter.Rewrite(HudRuntime.Quest, quest, quest, out int count);
        Assert.Equal(2, count);
        Assert.Contains("jamm_hud_text(2, 0, reward, 0.5, 0.5, 0)", adapted);
    }
}

public sealed partial class BuildingTests
{
    [Fact]
    public void HudSyntheticArchiveCompilesHooksAndReportsAdaptedCalls()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), modPath = temp.PathFor("mod.win");
        WriteModel(basePath, d => Setup(d, false)); WriteModel(modPath, d => Setup(d, true));
        using var original = GameFileLoader.Load(basePath); using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "Debug HUD");
        string output = temp.PathFor("merged.win");
        var build = ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);
        Assert.NotNull(build.Verification);
        Assert.Contains(build.Plan.Decisions, d => d.Contains("adapted 1 text call(s) in " + HudRuntime.Experience));
        using var merged = GameFileLoader.Load(output);
        string barText = PatchBuilder.ReadCode(merged, HudRuntime.Experience).Text;
        Assert.Contains("jamm_hud_set_font(font_quest)", barText);
        Assert.Contains("jamm_hud_set_halign(fa_right)", barText);
        Assert.Contains("jamm_hud_set_valign(fa_middle)", barText);
        var engine = merged.Data.GameObjects.ByName("QuestEngine");
        Assert.Equal(HudRuntime.Begin, engine.Events[8].Single(e => e.EventSubtype == 72).Actions[0].CodeId.Name.Content);
        Assert.Equal(HudRuntime.End, engine.Events[8].Single(e => e.EventSubtype == 73).Actions[0].CodeId.Name.Content);
        Assert.Equal(1, merged.Data.Code.Count(c => c.Name.Content == HudRuntime.Begin));
        foreach (string script in HudRuntime.Scripts.Keys) Assert.NotNull(merged.Data.Scripts.ByName(script));
        Assert.Contains("jamm_hud_begin", build.Plan.Assets.Single(a => a.Name == HudRuntime.Begin).Gml);
        Assert.DoesNotContain(build.Plan.Assets, a => a.Name.Contains("Step"));
        string beginScript = PatchBuilder.ReadCode(merged, "gml_Script_jamm_hud_begin").Text;
        Assert.Contains("view_hview[view_current]", beginScript);
        Assert.Contains("\"icon_Cancel\"", beginScript);

        static void Setup(UndertaleData data, bool debug)
        {
            foreach (string name in new[] { "ExperienceBar", "Quest", "QuestEngine", "Colors" })
                data.GameObjects.Add(new() { Name = data.Strings.MakeString(name), Visible = true });
            data.Fonts.Add(new() { Name = data.Strings.MakeString("font_first"), DisplayName = data.Strings.MakeString("First") });
            data.Fonts.Add(new() { Name = data.Strings.MakeString("font_quest"), DisplayName = data.Strings.MakeString("Quest") });
            var compiler = new CompileGroup(data);
            var bar = data.GameObjects.ByName("ExperienceBar").EventHandlerFor(EventType.Draw, data);
            compiler.QueueCodeReplace(bar, "draw_set_font(font_quest); draw_set_halign(fa_right); draw_set_valign(fa_middle); draw_x = round(view_xview[view_current] + offset_x); draw_y = round(view_yview[view_current] + offset_y); draw_text(draw_x - 10, draw_y + 10, \"Level\");" + (debug ? "draw_text(draw_x + 430, draw_y - 20, \"Debug\");" : ""));
            var quest = data.GameObjects.ByName("Quest").EventHandlerFor(EventType.Draw, data);
            compiler.QueueCodeReplace(quest, "draw_set_font(font_quest); draw_text_transformed(draw_x, draw_y, label, 0.5, 0.5, 0);");
            var shadow = UndertaleCode.CreateEmptyEntry(data, "gml_Script_draw_text_shadowed");
            data.Scripts.Add(new() { Name = data.Strings.MakeString("draw_text_shadowed"), Code = shadow });
            compiler.QueueCodeReplace(shadow, "draw_text(argument0, argument1, argument2);");
            Assert.True(compiler.Compile().Successful);
        }
    }

    [Fact]
    public void HudOverwriteOrderingAndExplicitChoicesAreHonoredWithCancelButtonPresent()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string aPath = temp.PathFor("hud-a.win");
        string bPath = temp.PathFor("hud-b.win");

        WriteModel(basePath, d => Setup(d, "base"));
        WriteModel(aPath, d => Setup(d, "ModA"));
        WriteModel(bPath, d => Setup(d, "ModB"));

        using var orig = GameFileLoader.Load(basePath);
        using var a = GameFileLoader.Load(aPath);
        using var b = GameFileLoader.Load(bPath);
        var pa = PatchBuilder.Create(orig, a, "ModA");
        var pb = PatchBuilder.Create(orig, b, "ModB");

        string outAB = temp.PathFor("ab.win");
        ArchiveBuilder.Build(basePath, [new(pa, aPath), new(pb, bPath)], outAB);
        using (var merged = GameFileLoader.Load(outAB))
        {
            string quest = PatchBuilder.ReadCode(merged, HudRuntime.Quest).Text;
            Assert.Contains("jamm_hud_text(2, 0, \"ModB\"", quest);
            Assert.DoesNotContain("ModA", quest);
        }

        string outResolved = temp.PathFor("resolved.win");
        ArchiveBuilder.Build(basePath, [new(pa, aPath), new(pb, bPath)], outResolved,
            resolutions: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [$"code/{HudRuntime.Quest}"] = "ModA"
            });
        using (var merged = GameFileLoader.Load(outResolved))
        {
            string quest = PatchBuilder.ReadCode(merged, HudRuntime.Quest).Text;
            Assert.Contains("jamm_hud_text(2, 0, \"ModA\"", quest);
            Assert.DoesNotContain("ModB", quest);
        }

        static void Setup(UndertaleData data, string tag)
        {
            foreach (string name in new[] { "ExperienceBar", "Quest", "QuestEngine", "Colors", "Handler", "icon_Cancel" })
                data.GameObjects.Add(new() { Name = data.Strings.MakeString(name), Visible = true });
            data.Fonts.Add(new() { Name = data.Strings.MakeString("font_quest"), DisplayName = data.Strings.MakeString("Quest") });
            var compiler = new CompileGroup(data);
            var bar = data.GameObjects.ByName("ExperienceBar").EventHandlerFor(EventType.Draw, data);
            compiler.QueueCodeReplace(bar, "draw_set_font(font_quest); draw_x = round(view_xview[view_current] + offset_x); draw_y = round(view_yview[view_current] + offset_y); draw_text(draw_x - 10, draw_y + 10, \"Level\");");
            var quest = data.GameObjects.ByName("Quest").EventHandlerFor(EventType.Draw, data);
            compiler.QueueCodeReplace(quest, $"draw_set_font(font_quest); draw_text_transformed(draw_x, draw_y, \"{tag}\", 0.5, 0.5, 0);");
            var shadow = UndertaleCode.CreateEmptyEntry(data, "gml_Script_draw_text_shadowed");
            data.Scripts.Add(new() { Name = data.Strings.MakeString("draw_text_shadowed"), Code = shadow });
            compiler.QueueCodeReplace(shadow, "draw_text(argument0, argument1, argument2);");
            Assert.True(compiler.Compile().Successful);
        }
    }
}
