using System.Text.Json;
using System.Text.RegularExpressions;
using Jamm.Core.Building;

namespace Jamm.Core.Management;

/// <summary>Owns JSON persistence and confines all manager writes to ordinary paths below its root.</summary>
public sealed partial class ManagerStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    /// <summary>The absolute data root chosen by option, environment or XDG default, in that order.</summary>
    public string Root { get; }

    /// <summary>Creates a data directory on first use. Symbolic links are refused for managed storage.</summary>
    public ManagerStore(string? dataDirectory = null)
    {
        string? configured = dataDirectory ?? Environment.GetEnvironmentVariable("CGFMM_DATA_DIR");
        string xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        Root = System.IO.Path.GetFullPath(configured ?? System.IO.Path.Combine(xdg, "cgfmm"));
        BuildFiles.RefuseStorageLink(Root);
        Directory.CreateDirectory(Root);
        foreach (string name in new[] { "mods", "patches", "backups", "profiles", "builds" }) Directory.CreateDirectory(Path(name));
    }

    internal string Path(params string[] parts)
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(new[] { Root }.Concat(parts).ToArray()));
        if (!path.StartsWith(Root.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("Managed path escapes the data directory. Choose a simple mod or profile name.");
        BuildFiles.RefuseStorageLink(path);
        return path;
    }

    internal T Read<T>(string path)
    {
        BuildFiles.RefuseStorageLink(path);
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? throw new JsonException("Empty document."); }
        catch (JsonException ex) { throw new InvalidDataException($"Cannot read '{System.IO.Path.GetFileName(path)}'. Repair or remove the invalid JSON file and retry.", ex); }
    }

    internal void Write<T>(string path, T value) => WriteText(path, JsonSerializer.Serialize(value, JsonOptions) + "\n");

    internal void WriteText(string path, string content)
    {
        BuildFiles.RefuseStorageLink(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        string temp = Path($".cgfmm-json-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, content);
            BuildFiles.RefuseStorageLink(path);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    /// <summary>Reads configuration, returning defaults before the first configuration change.</summary>
    public ManagerConfig Config()
    {
        var config = File.Exists(Path("config.json")) ? Read<ManagerConfig>(Path("config.json")) : new();
        if (config.Wine is null || config.Proton is null || config.Steam is null
            || config.Wine.Mode is not "wine" and not "proton" and not "proton-flatpak" and not "direct" || string.IsNullOrWhiteSpace(config.Wine.Binary)
            || !uint.TryParse(config.Steam.AppId, out uint id) || id == 0)
            throw new InvalidDataException("Configuration 'config.json' has invalid runner settings. Repair the JSON or remove it and use config set to configure again.");
        return config with { Proton = config.Proton with { CompatDataPath = NormalizeCompatDataPath(config.Proton.CompatDataPath) } };
    }

    internal static string? NormalizeCompatDataPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string candidate = value.Trim().Replace('\\', '/');
        if (candidate.Length >= 2 && char.IsAsciiLetter(candidate[0]) && candidate[1] == ':')
            candidate = candidate[2..];
        candidate = candidate.TrimEnd('/');
        if (candidate.Equals("/path/to/...", StringComparison.OrdinalIgnoreCase)
            || candidate.Equals("path/to/...", StringComparison.OrdinalIgnoreCase)
            || candidate.Equals("<data-dir>/proton/<app-id>", StringComparison.OrdinalIgnoreCase)
            || candidate.Equals("<Flatpak Steam>/steamapps/compatdata/343780", StringComparison.OrdinalIgnoreCase)
            || StockCompatPlaceholder().IsMatch(candidate))
            return null;
        return value;
    }

    [GeneratedRegex("^/?path/to/compatdata/[0-9]+$", RegexOptions.IgnoreCase)]
    private static partial Regex StockCompatPlaceholder();

    /// <summary>Creates the default Proton compatibility directory within guarded manager storage.</summary>
    public string EnsureProtonDataDirectory()
    {
        string path = Path("proton", Config().Steam.AppId);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Sets a documented configuration key, validating enum and numeric values.</summary>
    public ManagerConfig Set(string key, string value)
    {
        var config = Config();
        var result = key == "proton.compatDataPath" && NormalizeCompatDataPath(value) is null
            ? config with { Proton = config.Proton with { CompatDataPath = null } }
            : ApplySetting(config, key, value);
        SaveConfig(result);
        return result;
    }

    /// <summary>Validates and saves a settings form atomically; blank optional paths clear their overrides.</summary>
    public ManagerConfig SetMany(IReadOnlyDictionary<string, string> values)
    {
        var config = Config();
        foreach (var (key, value) in values)
        {
            if (key == "proton.compatDataPath" && NormalizeCompatDataPath(value) is null)
                config = config with { Proton = config.Proton with { CompatDataPath = null } };
            else if (!string.IsNullOrWhiteSpace(value)) config = ApplySetting(config, key, value);
            else config = key switch
            {
                "wine.prefix" => config with { Wine = config.Wine with { Prefix = null } },
                "proton.path" => config with { Proton = config.Proton with { Path = null } },
                "proton.clientInstallPath" => config with { Proton = config.Proton with { ClientInstallPath = null } },
                _ => throw new ArgumentException($"Configuration '{key}' needs a nonempty value.")
            };
        }
        SaveConfig(config);
        return config;
    }

    private static ManagerConfig ApplySetting(ManagerConfig config, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"Configuration '{key}' needs a nonempty value.");
        string Full() => System.IO.Path.GetFullPath(value);
        config = key switch
        {
            "useSharedCompatibilityResults" when bool.TryParse(value, out _) => config with { UseSharedCompatibilityResults = bool.Parse(value) },
            "checkForUpdates" when bool.TryParse(value, out _) => config with { CheckForUpdates = bool.Parse(value) },
            "showModsWithoutDownload" when bool.TryParse(value, out _) => config with { ShowModsWithoutDownload = bool.Parse(value) },
            "showLitterboxCatalog" when bool.TryParse(value, out _) => config with { ShowLitterboxCatalog = bool.Parse(value) },
            "showFirstPartyCatalog" when bool.TryParse(value, out _) => config with { ShowFirstPartyCatalog = bool.Parse(value) },
            "showGameJoltCatalog" when bool.TryParse(value, out _) => config with { ShowGameJoltCatalog = bool.Parse(value) },
            "advancedMode" when bool.TryParse(value, out _) => config with { AdvancedMode = bool.Parse(value) },
            "base" => config with { Base = Full() },
            "confirmedInstallDirectory" => config with { ConfirmedInstallDirectory = Full() },
            "gameDir" => config with { GameDir = Full() },
            "wine.mode" when value is "wine" or "proton" or "proton-flatpak" or "direct" => config with { Wine = config.Wine with { Mode = value } },
            "wine.prefix" => config with { Wine = config.Wine with { Prefix = Full() } },
            "wine.binary" => config with { Wine = config.Wine with { Binary = value.IndexOfAny([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar]) >= 0 ? Full() : value } },
            "proton.path" => config with { Proton = config.Proton with { Path = Full() } },
            "proton.compatDataPath" => config with { Proton = config.Proton with { CompatDataPath = Full() } },
            "proton.clientInstallPath" => config with { Proton = config.Proton with { ClientInstallPath = Full() } },
            "steam.appId" when uint.TryParse(value, out uint id) && id > 0 => config with { Steam = config.Steam with { AppId = value } },
            "wine.mode" => throw new ArgumentException("wine.mode must be 'wine', 'proton', 'proton-flatpak' or 'direct'."),
            "steam.appId" => throw new ArgumentException("steam.appId must be a positive number."),
            _ => throw new ArgumentException($"Unknown configuration key '{key}'. See config in 'cgfmm --help'.")
        };
        return config;
    }

    internal void SaveConfig(ManagerConfig config) => Write(Path("config.json"), config);

    internal static string Name(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name is "." or ".."
            || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
            throw new ArgumentException($"Invalid name '{name}'. Use up to 100 letters, digits, dots, hyphens or underscores.");
        return name;
    }
}
