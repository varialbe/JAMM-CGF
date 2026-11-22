namespace Jamm.ModMaker;

/// <summary>Build-time opt-ins and defaults for JAMM Perf. Experimental fish micro-optimizations are off by default.</summary>
public sealed record PerfSettings(bool StartEnabled = false, bool FishGrid = false, int Interval = 4,
    bool DecorSleep = true, bool FishMicro = false);
