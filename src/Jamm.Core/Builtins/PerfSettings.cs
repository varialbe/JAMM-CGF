namespace Jamm.ModMaker;

/// <summary>Build-time opt-ins and defaults for JAMM Perf. Existing in-game INI settings take precedence.</summary>
public sealed record PerfSettings(bool StartEnabled = false, bool FishGrid = false, int Interval = 4, bool DecorSleep = true)
{
    /// <summary>Enables Fish Sleep for distant standard fish outside the player's relevance zone.</summary>
    public bool FishSleep { get; init; } = true;

    /// <summary>Enables the experimental coarse collision masks for fish and visual-only decor.</summary>
    public bool MasksRect { get; init; }

    /// <summary>Enables experimental half-rate AI dispatch for awake fish beyond 1.5 screens.</summary>
    public bool FishLod { get; init; }

    /// <summary>Enables experimental native/cheaper decor animation updates.</summary>
    public bool DecorAnimNative { get; init; }

    /// <summary>Enables experimental per-event hot-value caching.</summary>
    public bool CacheHot { get; init; } = false; // no measurable win (docs/reports/next-hotspots.md); keep opt-in

    /// <summary>Enables eligible visual-effect object conversion to particle effects.</summary>
    public bool ParticlesFx { get; init; }

    /// <summary>Relevance zone margin in pixels around active views, lure, boat, Girl, sonar, and targets.</summary>
    public int FishSleepMargin { get; init; } = Jamm.ModMaker.FishSleep.DefaultMargin;
}
