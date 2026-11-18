using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Jamm.Gui;

/// <summary>Opt-in updater restricted to JAMM-CGF's official GitHub release assets.</summary>
internal static class OfficialUpdater
{
    private const string ReleaseApi = "https://api.github.com/repos/varialbe/JAMM-CGF/releases/latest";
    private static readonly HttpClient Http = CreateClient();
    private sealed record Asset(string Name, string Url, string? Digest);

    public static async Task CheckAndOfferAsync(Func<string, Task<bool>> confirm)
    {
        using var response = await Http.GetAsync(ReleaseApi);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = json.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("Release tag is missing.");
        if (!Version.TryParse(tag.TrimStart('v'), out var latest)) return;
        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
        if (latest <= new Version(current.Major, current.Minor, Math.Max(0, current.Build))) return;

        string? expected = OperatingSystem.IsWindows() ? "setup.exe" : OperatingSystem.IsLinux() ? ".deb" : null;
        if (expected is null) return;
        Asset? asset = root.GetProperty("assets").EnumerateArray()
            .Select(a => new Asset(a.GetProperty("name").GetString() ?? "", a.GetProperty("browser_download_url").GetString() ?? "", a.TryGetProperty("digest", out var d) ? d.GetString() : null))
            .FirstOrDefault(a => (OperatingSystem.IsWindows() && a.Name.EndsWith("_win-x64_setup.exe", StringComparison.Ordinal))
                || (OperatingSystem.IsLinux() && a.Name.EndsWith("_amd64.deb", StringComparison.Ordinal)));
        string releasePage = $"https://github.com/varialbe/JAMM-CGF/releases/tag/{Uri.EscapeDataString(tag)}";
        if (asset is null) return;
        var uri = new Uri(asset.Url);
        if (uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com"
            || !uri.AbsolutePath.StartsWith("/varialbe/JAMM-CGF/releases/download/", StringComparison.Ordinal)
            || !uri.AbsolutePath.EndsWith("/" + asset.Name, StringComparison.Ordinal))
            throw new InvalidDataException("The official release contained an unexpected installer URL.");

        if (!await confirm($"JAMM {tag} is available. Download the official {asset.Name} installer from GitHub and open it?\n\nThe installer will not run unless you confirm here. Cancel keeps your current version.\n{releasePage}")) return;
        string temp = Path.Combine(Path.GetTempPath(), "jamm-update-" + Guid.NewGuid().ToString("N") + (OperatingSystem.IsWindows() ? ".exe" : ".deb"));
        using var download = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        download.EnsureSuccessStatusCode();
        await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await download.Content.CopyToAsync(output);
        if (asset.Digest is { } digest && digest.StartsWith("sha256:", StringComparison.Ordinal))
        {
            await using var input = File.OpenRead(temp);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(input)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(digest[7..])))
                throw new InvalidDataException("The downloaded installer did not match GitHub's SHA-256 digest.");
        }
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(temp) { UseShellExecute = true }
            : new ProcessStartInfo("xdg-open") { UseShellExecute = false, ArgumentList = { temp } };
        Process.Start(start);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("JAMM-CGF", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
