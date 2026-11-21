using Jamm.Core.Building;

namespace Jamm.Core.Tests;

public sealed class ChunkScriptTypeTests
{
    [Fact]
    public void ChunkStreamScriptArgumentsAreTypedSoTheyRelinkAcrossMods()
    {
        Assert.True(CgfAssetTypes.HasTypedArguments("jamm_chunk_stream"));
        Assert.Equal("scripts", CgfAssetTypes.ArgumentFamily("jamm_chunk_stream", 3));
        Assert.Equal("scripts", CgfAssetTypes.ArgumentFamily("jamm_chunk_stream", 4));
        Assert.Null(CgfAssetTypes.ArgumentFamily("jamm_chunk_stream", 0));
    }
}
