namespace Jamm.ModMaker;

/// <summary>Build-time opt-ins and defaults for JAMM Perf. Existing in-game INI settings take precedence.</summary>
public sealed record PerfSettings(bool StartEnabled = false, bool FishGrid = false, int Interval = 4, bool DecorSleep = true)
{
    /// <summary>Enables Fish Sleep for distant standard fish outside the player's relevance zone.</summary>
    public bool FishSleep { get; init; } = true;

    /// <summary>Relevance zone margin in pixels around active views, lure, boat, Girl, sonar, and targets.</summary>
    public int FishSleepMargin { get; init; } = Jamm.ModMaker.FishSleep.DefaultMargin;
}
