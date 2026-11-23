using Jamm.Core.Building;

namespace Jamm.Core.Tests;

public sealed class AssetLiteralRemapTests
{
    [Fact]
    public void UntypedSpriteArrayLiteralsAreNamedFromEachModsOwnAssetTable()
    {
        var baseline = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["sprites"] = ["spr_base_0", "spr_base_1"],
            ["objects"] = ["obj_base"]
        };
        var first = new Dictionary<string, string[]>(baseline, StringComparer.Ordinal)
        { ["sprites"] = [.. baseline["sprites"], "jmk_first_sprite"] };
        var second = new Dictionary<string, string[]>(baseline, StringComparer.Ordinal)
        { ["sprites"] = [.. baseline["sprites"], "jmk_second_sprite"] };
        const string code = "harbor_sprite[0] = 2; draw_sprite_ext(harbor_sprite[0], 0, x, y, 1, 1, 0, c_white, 1);";

        string firstNamed = SemanticReferences.NameAddedAssetLiterals(code, "first", baseline, first);
        string secondNamed = SemanticReferences.NameAddedAssetLiterals(code, "second", baseline, second);

        Assert.Contains("harbor_sprite[0] = jmk_first_sprite;", firstNamed);
        Assert.Contains("harbor_sprite[0] = jmk_second_sprite;", secondNamed);
    }

    [Fact]
    public void AmbiguousAssetLikeLiteralIsReportedAndLeftUntouched()
    {
        var baseline = new Dictionary<string, string[]>(StringComparer.Ordinal)
        { ["sprites"] = ["spr_base"], ["sounds"] = ["snd_base"] };
        var mod = new Dictionary<string, string[]>(StringComparer.Ordinal)
        { ["sprites"] = ["spr_base", "jmk_sprite"], ["sounds"] = ["snd_base", "jmk_sound"] };
        List<string> warnings = [];

        string result = SemanticReferences.NameAddedAssetLiterals("asset_id = 1;", "ambiguous", baseline, mod, warnings.Add);

        Assert.Equal("asset_id = 1;", result);
        Assert.Single(warnings);
        Assert.Contains("ambiguous", warnings[0], StringComparison.Ordinal);
    }
}
