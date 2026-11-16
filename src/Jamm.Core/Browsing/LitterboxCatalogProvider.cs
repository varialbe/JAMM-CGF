using System.Text.Json;
using Jamm.Core.Management;

namespace Jamm.Core.Browsing;

/// <summary>Adapter for Litterbox's changeable public Supabase catalog, inspected November 2026.</summary>
public sealed class LitterboxCatalogProvider(ManagerStore store, ModHttp http) : IModCatalogProvider
{
    // Public publishable key from litterbox.fyi/assets/app.js, never a service/admin credential.
    private const string PublicKey = "sb_publishable_nNsroxCkvj2QPBgy50MVFQ_HBElE4_b";
    private const string Endpoint = "https://psmwriziynkxzerdazqm.supabase.co/rest/v1/mods?select=*,profiles!mods_profiles_fkey(is_verified),mod_categories(categories(id,slug,name,icon)),mod_screenshots(url,sort_order),mod_credits(display_name,comment,sort_order,profiles(username))&order=created_at.desc&limit=500";
    /// <summary>Fetches a bounded public snapshot, or returns the last successful snapshot with an offline warning.</summary>
    public async Task<CatalogSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        string cache = store.Path("catalog", "litterbox.json");
        try
        {
            var bytes = await http.ReadAsync(Endpoint, 4 * 1048576, cancellationToken, PublicKey);
            var snapshot = new CatalogSnapshot(Parse(bytes), DateTimeOffset.UtcNow);
            store.Write(cache, snapshot);
            return snapshot;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is HttpRequestException or IOException or JsonException or OperationCanceledException or TimeoutException)
        {
            if (File.Exists(cache)) return store.Read<CatalogSnapshot>(cache) with { OfflineReason = "Offline/catalog unavailable: " + ex.Message };
            throw new IOException("Could not load Litterbox. Check your connection and Retry. Local mod management remains available. " + ex.Message, ex);
        }
    }
    /// <summary>Maps only public metadata, tolerating absent optional fields and relations.</summary>
    public static CatalogMod[] Parse(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Litterbox catalog format changed. Retry later or open litterbox.fyi.");
        return document.RootElement.EnumerateArray().Where(m => Text(m, "id") is not null && Text(m, "name") is not null).Select(m =>
        {
            var categories = Array(m, "mod_categories").Select(c => c.TryGetProperty("categories", out var category) ? Text(category, "name") : null).OfType<string>().Distinct().ToArray();
            DateTimeOffset? created = DateTimeOffset.TryParse(Text(m, "created_at"), out var date) ? date : null;
            double? rating = double.TryParse(Text(m, "rating"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : null;
            return new CatalogMod(Text(m, "id")!, Text(m, "name")!, Text(m, "author") ?? "Unknown author", Text(m, "description") ?? "",
                Text(m, "file_url"), Bool(m, "is_preview"), categories, created, rating, Text(m, "icon_url"), Text(m, "banner_url"),
                Array(m, "mod_screenshots").OrderBy(s => int.TryParse(Text(s, "sort_order"), out var order) ? order : 0).Select(s => Text(s, "url")).OfType<string>()
                    .Concat(Array(m, "screenshots").Where(s => s.ValueKind == JsonValueKind.String).Select(s => s.GetString()!)).Distinct().ToArray(),
                m.TryGetProperty("profiles", out var profile) && Bool(profile, "is_verified"),
                string.Join("\n", Array(m, "mod_credits").OrderBy(c => int.TryParse(Text(c, "sort_order"), out var order) ? order : 0).Select(c =>
                    (Text(c, "display_name") ?? (c.TryGetProperty("profiles", out var person) ? Text(person, "username") : null) ?? "Contributor") + ": " + (Text(c, "comment") ?? ""))));
        }).ToArray();
    }
    internal static string? Text(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number ? value.ToString() : null;
    internal static bool Bool(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
    internal static JsonElement[] Array(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
}
