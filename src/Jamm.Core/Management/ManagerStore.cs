using System.Text.Json;
using Jamm.Core.Building;

namespace Jamm.Core.Management;

/// <summary>Owns JSON persistence and confines all manager writes to ordinary paths below its root.</summary>
public sealed class ManagerStore
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
        BuildFiles.RefuseLink(Root);
        Directory.CreateDirectory(Root);
        foreach (string name in new[] { "mods", "patches", "backups", "profiles", "builds" }) Directory.CreateDirectory(Path(name));
    }

    internal string Path(params string[] parts)
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(new[] { Root }.Concat(parts).ToArray()));
        if (!path.StartsWith(Root.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("Managed path escapes the data directory. Choose a simple mod or profile name.");
        BuildFiles.RefuseLink(path);
        return path;
    }

    internal T Read<T>(string path)
    {
        BuildFiles.RefuseLink(path);
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? throw new JsonException("Empty document."); }
        catch (JsonException ex) { throw new InvalidDataException($"Cannot read '{System.IO.Path.GetFileName(path)}'. Repair or remove the invalid JSON file and retry.", ex); }
    }

    internal void Write<T>(string path, T value) => WriteText(path, JsonSerializer.Serialize(value, JsonOptions) + "\n");

    internal void WriteText(string path, string content)
    {
        BuildFiles.RefuseLink(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        string temp = Path($".cgfmm-json-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, content);
            BuildFiles.RefuseLink(path);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    /// <summary>Reads configuration, returning defaults before the first configuration change.</summary>
    public ManagerConfig Config()
    {
        var config = File.Exists(Path("config.json")) ? Read<ManagerConfig>(Path("config.json")) : new();
        if (config.Wine is null || config.Proton is null || config.Steam is null
            || config.Wine.Mode is not "wine" and not "proton" || string.IsNullOrWhiteSpace(config.Wine.Binary)
            || !uint.TryParse(config.Steam.AppId, out uint id) || id == 0)
            throw new InvalidDataException("Configuration 'config.json' has invalid runner settings. Repair the JSON or remove it and use config set to configure again.");
        return config;
    }

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
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"Configuration '{key}' needs a nonempty value.");
        var config = Config();
        string Full() => System.IO.Path.GetFullPath(value);
        config = key switch
        {
            "base" => config with { Base = Full() },
            "gameDir" => config with { GameDir = Full() },
            "wine.mode" when value is "wine" or "proton" => config with { Wine = config.Wine with { Mode = value } },
            "wine.prefix" => config with { Wine = config.Wine with { Prefix = Full() } },
            "wine.binary" => config with { Wine = config.Wine with { Binary = value.IndexOfAny([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar]) >= 0 ? Full() : value } },
            "proton.path" => config with { Proton = config.Proton with { Path = Full() } },
            "proton.compatDataPath" => config with { Proton = config.Proton with { CompatDataPath = Full() } },
            "proton.clientInstallPath" => config with { Proton = config.Proton with { ClientInstallPath = Full() } },
            "steam.appId" when uint.TryParse(value, out uint id) && id > 0 => config with { Steam = config.Steam with { AppId = value } },
            "wine.mode" => throw new ArgumentException("wine.mode must be 'wine' or 'proton'."),
            "steam.appId" => throw new ArgumentException("steam.appId must be a positive number."),
            _ => throw new ArgumentException($"Unknown configuration key '{key}'. See config in 'cgfmm --help'.")
        };
        Write(Path("config.json"), config);
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
