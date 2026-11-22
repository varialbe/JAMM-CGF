namespace Jamm.ModMaker;

/// <summary>Profile defaults for JAMM Perf. Existing in-game INI settings take precedence.</summary>
public sealed record PerfSettings(bool StartEnabled = false, bool FishGrid = true, int Interval = 4)
{
    /// <summary>Enables Fish Sleep for distant standard fish outside the player's relevance zone.</summary>
    public bool FishSleep { get; init; } = true;

    /// <summary>Relevance zone margin in pixels around active views, lure, boat, Girl, sonar, and targets.</summary>
    public int FishSleepMargin { get; init; } = 2000;
}
