using Jamm.Core.Browsing;

namespace Jamm.Core.Tests;

public sealed class FirstPartyCompatibilityTests
{
    private const string BaseHash = "dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1";
    private static CatalogMod Mod(FirstPartyCompatibility compat) => new("submarine", "Submarine", "JAMM", "", "https://example.com/mod.zip",
        false, [], null, null, Catalog: "first-party", Compat: compat);

    [Fact]
    public void SupportsRequiredBaseAndMinimumJammVersion()
    {
        var compat = new FirstPartyCompatibility(BaseHash, "0.9.1");
        Assert.Equal("Compatible", FirstPartyCompatibilityEvaluator.Evaluate(Mod(compat), BaseHash, jammVersion: "0.9.1").Status);
        Assert.Equal("Incompatible", FirstPartyCompatibilityEvaluator.Evaluate(Mod(compat), "00" + BaseHash[2..]).Status);
        Assert.Equal("Incompatible", FirstPartyCompatibilityEvaluator.Evaluate(Mod(compat), BaseHash, jammVersion: "0.9.0").Status);
        Assert.Equal("Warning", FirstPartyCompatibilityEvaluator.Evaluate(Mod(compat), null).Status);
    }

    [Fact]
    public void ActiveConflictsAndMissingRequirementsAreReported()
    {
        var conflict = new FirstPartyCompatibility(BaseHash, "0.9.1", Conflicts: ["jamm-silo"]);
        var required = new FirstPartyCompatibility(BaseHash, "0.9.1", Requires: ["builtin-console"]);
        Assert.Equal("Incompatible", FirstPartyCompatibilityEvaluator.Evaluate(Mod(conflict), BaseHash, ["jamm-silo"]).Status);
        Assert.Equal("Warning", FirstPartyCompatibilityEvaluator.Evaluate(Mod(required), BaseHash).Status);
        Assert.Equal("Compatible", FirstPartyCompatibilityEvaluator.Evaluate(Mod(required), BaseHash, ["builtin-console"]).Status);
    }

    [Fact]
    public void KnownIssuesRemainVisibleAsWarnings()
    {
        var mod = Mod(new FirstPartyCompatibility(BaseHash, "0.9.1", KnownIssues: ["Sprite remap issue."]));
        var result = FirstPartyCompatibilityEvaluator.Evaluate(mod, BaseHash);
        Assert.Equal("Warning", result.Status);
        Assert.Contains("Sprite remap", result.Reason);
    }
}
