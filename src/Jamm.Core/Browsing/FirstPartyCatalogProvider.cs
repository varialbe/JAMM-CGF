using System.Text.Json;
using Jamm.Core.Management;

namespace Jamm.Core.Browsing;

/// <summary>Loads Varialbe's public first-party mod catalog with an independent offline snapshot.</summary>
public sealed class FirstPartyCatalogProvider(ManagerStore store, ModHttp http) : IModCatalogProvider
{
    private const string Endpoint = "https://varialbe.xyz/cgf-mods/catalog.json";

    /// <inheritdoc />
    public async Task<CatalogSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        string path = store.Path("catalog", "first-party.json");
        try
        {
            byte[] bytes = await http.ReadAsync(Endpoint, 2 * 1048576, cancellationToken);
            CatalogMod[] mods = Parse(bytes);
            var snapshot = new CatalogSnapshot(mods, DateTimeOffset.UtcNow);
            store.Write(path, snapshot);
            return snapshot;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is HttpRequestException or IOException or JsonException or OperationCanceledException or TimeoutException)
        {
            if (File.Exists(path)) return store.Read<CatalogSnapshot>(path) with { OfflineReason = "First-party catalog unavailable: " + ex.Message };
            throw new IOException("Could not load the first-party catalog at varialbe.xyz/cgf-mods/catalog.json. Check your connection and Retry.", ex);
        }
    }

    /// <summary>Parses the shared catalog contract: a JSON array of CatalogMod objects.</summary>
    public static CatalogMod[] Parse(byte[] json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var mods = JsonSerializer.Deserialize<CatalogMod[]>(json, options) ?? throw new JsonException("First-party catalog must be a JSON array of CatalogMod objects.");
        return mods.Where(m => !string.IsNullOrWhiteSpace(m.Id) && !string.IsNullOrWhiteSpace(m.Name))
            .Select(m => m with
            {
                Author = string.IsNullOrWhiteSpace(m.Author) ? "Unknown author" : m.Author,
                Description = m.Description ?? "",
                Categories = m.Categories ?? [],
                Credits = m.Credits ?? "",
                Catalog = "first-party",
                CatalogPage = m.CatalogPage ?? "https://varialbe.xyz/cgf-mods/"
            }).ToArray();
    }
}
