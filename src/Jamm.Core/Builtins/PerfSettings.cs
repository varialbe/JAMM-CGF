namespace Jamm.ModMaker;

/// <summary>Profile defaults for JAMM Perf. Existing in-game INI settings take precedence.</summary>
public sealed record PerfSettings(bool StartEnabled = false, bool FishGrid = true, int Interval = 4);
