namespace Jamm.ModMaker;

/// <summary>Build-time opt-ins and defaults for JAMM Perf. Existing in-game INI settings take precedence.</summary>
public sealed record PerfSettings(bool StartEnabled = false, bool FishGrid = false, int Interval = 4);
