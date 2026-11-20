using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jamm.Core.Management;

/// <summary>An explicit vanilla, base, or addon role, with optional exact requirements on the addon ancestor.</summary>
public sealed record ModRole(string Role, string? Base = null, string? BaseVersion = null, string? BaseSha256 = null)
{
    internal static ModRole Vanilla { get; } = new("none");

    /// <summary>Short label for the effective role.</summary>
    [JsonIgnore]
    public string Label => Role switch { "base" => "Base", "addon" => $"Addon of {Base}", _ => "Vanilla" };

    internal bool IsValid() => Role switch
    {
        "addon" => Base is { Length: > 0 and <= 100 } && Base is not "." and not ".."
            && Base.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            && (BaseVersion is null || !string.IsNullOrWhiteSpace(BaseVersion))
            && (BaseSha256 is null || BaseSha256.Length == 64 && BaseSha256.All(char.IsAsciiHexDigit)),
        "base" or "none" => Base is null && BaseVersion is null && BaseSha256 is null,
        _ => false
    };

    internal bool Targets(ManagedMod mod) => Base == mod.Id || Base == mod.PackageId;

    internal string? Mismatch(ManagedMod mod)
    {
        List<string> differences = [];
        if (BaseVersion is not null && BaseVersion != mod.Version)
            differences.Add($"expected version {BaseVersion}, found {mod.Version ?? "an unknown version"}");
        if (BaseSha256 is not null && !BaseSha256.Equals(mod.Sha256, StringComparison.OrdinalIgnoreCase))
            differences.Add($"expected SHA-256 {BaseSha256}, found {mod.Sha256}");
        return differences.Count == 0 ? null : string.Join("; ", differences);
    }

    internal static (string? Id, string? Version, ModRole? Role) ReadManifest(JsonElement document, string file)
    {
        if (document.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"'{file}' must contain a JSON object.");
        string? Read(string key)
        {
            foreach (var property in document.EnumerateObject())
            {
                if (!key.Equals(property.Name, StringComparison.OrdinalIgnoreCase)) continue;
                if (property.Value.ValueKind == JsonValueKind.Null) return null;
                if (property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                    return property.Value.GetString();
                throw new InvalidDataException($"'{file}' needs a nonempty string for '{key}'. Fix the manifest and retry.");
            }
            return null;
        }
        string? id = Read("id"), version = Read("version"), role = Read("role"), ancestor = Read("base");
        string? baseVersion = Read("baseVersion"), baseSha256 = Read("baseSha256");
        if (id is not null && !new ModRole("addon", id).IsValid())
            throw new InvalidDataException($"'{file}' has an invalid 'id'. Use letters, digits, dashes or underscores.");
        if (role is null && ancestor is null && baseVersion is null && baseSha256 is null) return (id, version, null);
        var result = new ModRole(role ?? "addon", ancestor, baseVersion, baseSha256);
        if (!result.IsValid())
            throw new InvalidDataException($"'{file}' has an invalid mod role. Use role 'base' alone, or set 'base' to a mod ID with an optional 'baseVersion' or 64-digit 'baseSha256'.");
        return (id, version, result);
    }
}
