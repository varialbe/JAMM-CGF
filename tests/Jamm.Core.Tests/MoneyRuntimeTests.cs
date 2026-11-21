using Jamm.Core.Building;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

public sealed class MoneyRuntimeTests
{
    [Theory]
    [InlineData(0, "$0")]
    [InlineData(950, "$950")]
    [InlineData(1234, "$1,234")]
    [InlineData(9999.9, "$9,999")]
    [InlineData(10000, "$10.0K")]
    [InlineData(25000, "$25.0K")]
    [InlineData(999950, "$999.9K")]
    [InlineData(999999, "$999.9K")]
    [InlineData(1000000, "$1.0M")]
    [InlineData(3400000, "$3.4M")]
    [InlineData(999999999, "$999.9M")]
    [InlineData(1234567890, "$1.2B")]
    [InlineData(9500000000, "$9.5B")]
    [InlineData(3400000000000, "$3.4T")]
    [InlineData(9500000000000, "$9.5T")]
    [InlineData(1e15, "$1.0Qa")]
    [InlineData(-5000, "$-5,000")]
    [InlineData(-25000, "$-25.0K")]
    public void FormatsCompactlyWithTruncatedTenths(double value, string expected) =>
        Assert.Equal(expected, MoneyRuntime.Format(value));

    [Fact]
    public void GmlFormatterUsesTheSameRules()
    {
        string gml = MoneyRuntime.FormatterGml;
        Assert.Contains("if (jamm_v < 10000)", gml);
        Assert.Contains("floor((jamm_v * 10) / jamm_d)", gml);
        foreach (string unit in new[] { "\"K\"", "\"M\"", "\"B\"", "\"T\"", "\"Qa\"" }) Assert.Contains(unit, gml);
        // The legacy compiler must not see 64-bit literals; large divisors are products of 32-bit values.
        Assert.DoesNotContain("1000000000000", gml);
    }
}

public sealed partial class BuildingTests
{
    private const string VanillaFormatter = """
        dstr = string(floor(argument0));
        i = string_length(dstr) - 2;
        while (i > 1)
        {
            dstr = string_insert(",", dstr, i);
            i -= 3;
        }
        dstr = "$" + dstr;
        return dstr;
        """;

    private const string VanillaWallet = """
        if (view_current == 0)
        {
            dollars_string = number_to_dollar_string(min(floor(dollars), 999999999));
            draw_text(x, y, dollars_string);
        }
        """;

    [Fact]
    public void BigMoneyPatchesFormatterAndWalletOnlyInModdedBuildsAndCanBeTurnedOff()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), modPath = temp.PathFor("mod.win");
        WriteModel(basePath, d => Setup(d, false));
        WriteModel(modPath, d => Setup(d, true));
        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "Unrelated mod");

        string output = temp.PathFor("merged.win");
        var build = ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);
        Assert.NotNull(build.Verification);
        Assert.Contains(build.Plan.Decisions, d => d.StartsWith("[CGF-MONEY-001]", StringComparison.Ordinal));
        using (var merged = GameFileLoader.Load(output))
        {
            string formatter = PatchBuilder.ReadCode(merged, MoneyRuntime.Formatter).Text;
            Assert.Contains("\"Qa\"", formatter);
            string wallet = PatchBuilder.ReadCode(merged, MoneyRuntime.Wallet).Text;
            Assert.DoesNotContain("999999999", wallet);
            Assert.Contains("number_to_dollar_string(floor(dollars))", wallet);
            Assert.Contains("Girl.dollars", wallet);
        }

        string off = temp.PathFor("off.win");
        var plain = ArchiveBuilder.Build(basePath, [new(patch, modPath)], off, bigMoney: false);
        Assert.DoesNotContain(plain.Plan.Decisions, d => d.Contains("CGF-MONEY", StringComparison.Ordinal));
        using (var merged = GameFileLoader.Load(off))
            Assert.Contains("999999999", PatchBuilder.ReadCode(merged, MoneyRuntime.Wallet).Text);

        string vanilla = temp.PathFor("vanilla.win");
        ArchiveBuilder.Build(basePath, [], vanilla);
        Assert.Equal(File.ReadAllBytes(basePath), File.ReadAllBytes(vanilla));

        static void Setup(UndertaleData data, bool modded)
        {
            foreach (string name in new[] { "Wallet", "Girl", "Marker" })
                data.GameObjects.Add(new() { Name = data.Strings.MakeString(name), Visible = true });
            var compiler = new CompileGroup(data);
            var formatter = UndertaleCode.CreateEmptyEntry(data, MoneyRuntime.Formatter);
            data.Scripts.Add(new() { Name = data.Strings.MakeString("number_to_dollar_string"), Code = formatter });
            compiler.QueueCodeReplace(formatter, VanillaFormatter);
            compiler.QueueCodeReplace(data.GameObjects.ByName("Wallet").EventHandlerFor(EventType.Draw, data), VanillaWallet);
            compiler.QueueCodeReplace(data.GameObjects.ByName("Marker").EventHandlerFor(EventType.Create, data), modded ? "value = 2;" : "value = 1;");
            Assert.True(compiler.Compile().Successful);
        }
    }
}
