namespace Jamm.Core.Building;

/// <summary>Player-selected safeguards applied when assembling a modded archive.</summary>
public sealed record ValueGuardOptions(long FishValueCap = ValueGuardOptions.DefaultFishValueCap, bool FairPlay = true)
{
    /// <summary>Default maximum fish sale value. A configured cap of zero disables the sale limit.</summary>
    public const long DefaultFishValueCap = 100_000_000;

    internal const string Revision = "value-guard-1";
}
