using Jamm.Core.Building;
using Jamm.ModMaker;
using UndertaleModLib;

namespace Jamm.Core.Management;

/// <summary>A bundled generator and its player-facing controls.</summary>
public sealed record BuiltinMod(string Id, string Name, string Description);

/// <summary>Generates bundled mods from the configured read-only base and imports them normally.</summary>
public sealed class BuiltinMods(ManagerStore store)
{
    /// <summary>Increment whenever bundled generator behavior changes.</summary>
    public const string GeneratorVersion = "1";
    /// <summary>Bundled mods available without downloads or an SDK.</summary>
    public static IReadOnlyList<BuiltinMod> Catalog { get; } = Array.AsReadOnly(new[]
    {
        new BuiltinMod("perf", "JAMM Perf", "Performance optimizations and optional fish scheduling. F8 switches modes."),
        new BuiltinMod("smooth", "JAMM Smooth", "Smoother rendering. F7 cycles 60/120/off."),
        new BuiltinMod("hd-lite", "JAMM HD Lite", "Fullscreen rendering with original textures. F11 toggles fullscreen."),
        new BuiltinMod("console", "JAMM Console", "F12 or backtick opens the console. Type unlockall to unlock equipment; unlocks persist on the next save.")
    });

    /// <summary>Generates or reuses a cache entry, then imports it with a stable library ID.</summary>
    public AddModResult Add(string id, Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var definition = Catalog.SingleOrDefault(m => m.Id == id)
            ?? throw new ArgumentException($"Unknown built-in mod '{id}'. Choose perf, smooth, hd-lite or console.");
        string original = store.Config().Base ?? throw new ArgumentException("Choose the untouched legacy base data.win in Settings or with 'cgfmm config set base' first.");
        cancellationToken.ThrowIfCancellationRequested();
        string sha = BuildFiles.Hash(original);
        string directory = store.Path("builtin");
        Directory.CreateDirectory(directory);
        string output = store.Path("builtin", $"{id}-{GeneratorVersion}-{sha}.win");
        string receipt = store.Path("builtin", Path.GetFileName(output) + ".sha256");
        bool cached = File.Exists(output) && File.Exists(receipt) && File.ReadAllText(receipt) == BuildFiles.Hash(output);
        if (!cached)
        {
            progress?.Invoke($"Generating {definition.Name} from '{original}'…");
            string temp = store.Path("builtin", $".{Guid.NewGuid():N}.tmp");
            try
            {
                using var input = new CancellableGameStream(original, cancellationToken);
                using var data = UndertaleIO.Read(input, (_, _) => cancellationToken.ThrowIfCancellationRequested(), _ => cancellationToken.ThrowIfCancellationRequested());
                cancellationToken.ThrowIfCancellationRequested();
                switch (id)
                {
                    case "perf": PerfMod.Apply(data); break;
                    case "smooth": SmoothMod.Apply(data); break;
                    case "hd-lite": HdMod.Apply(data, null); break;
                    case "console": ConsoleMod.Apply(data); break;
                }
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke($"Writing {definition.Name}…");
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write)) UndertaleIO.Write(stream, data);
                cancellationToken.ThrowIfCancellationRequested();
                if (BuildFiles.Hash(original) != sha) throw new IOException($"Base '{original}' changed during generation. Choose a stable untouched base and retry.");
                using (var verified = GameFileLoader.Load(temp, progress, cancellationToken)) { }
                File.Move(temp, output, true);
                store.WriteText(receipt, BuildFiles.Hash(output));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { throw new InvalidDataException($"Could not generate {definition.Name} from '{original}': {ex.Message} Choose the untouched legacy 2023 base and retry.", ex); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        else progress?.Invoke($"Using cached {definition.Name}.");
        cancellationToken.ThrowIfCancellationRequested();
        if (BuildFiles.Hash(original) != sha) throw new IOException($"Base '{original}' changed. Retry with a stable untouched base.");
        return new ModLibrary(store).Add(output, definition.Name, progress, cancellationToken,
            stableId: "builtin-" + id, description: definition.Description);
    }
}
