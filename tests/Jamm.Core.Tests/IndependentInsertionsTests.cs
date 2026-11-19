using Jamm.Core.Merging;

namespace Jamm.Core.Tests;

public sealed class IndependentInsertionsTests
{
    private static MergeHunk Hunk(params string[] texts) => new(3, 0, "CONFLICT", "list", "",
        texts.Select((t, i) => new MergeVersion($"M{i}", t)).ToArray(), null, [], string.Concat(texts));

    [Fact]
    public void DisjointParticleBlocksAreUnioned()
    {
        var hunk = Hunk("water_drip = part_type_create();\npart_type_shape(water_drip, 3);\n",
            "global.pt_whirl = part_type_create();\npart_type_shape(global.pt_whirl, 6);\n");
        Assert.True(IndependentInsertions.CanUnion(hunk));
    }

    [Fact]
    public void SameVariableOrCrossReadIsNotUnioned()
    {
        Assert.False(IndependentInsertions.CanUnion(Hunk("speed = 1;\n", "speed = 2;\n")));
        Assert.False(IndependentInsertions.CanUnion(Hunk("a = 1;\n", "b = a + 1;\n")));
        Assert.False(IndependentInsertions.CanUnion(Hunk("global.x = 1;\n", "global.x += 1;\n")));
    }

    [Fact]
    public void DifferentGlobalsDoNotCollide()
    {
        Assert.True(IndependentInsertions.CanUnion(Hunk("global.a = 1;\n", "global.b = 2;\n")));
    }
}
