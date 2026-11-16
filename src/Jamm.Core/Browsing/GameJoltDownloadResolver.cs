using System.Text.Json;
using System.Text.RegularExpressions;
using static Jamm.Core.Browsing.LitterboxCatalogProvider;

namespace Jamm.Core.Browsing;

/// <summary>Resolves Game Jolt's public project overview and build URL lookup, plus explicit HTTPS .win/.zip links.</summary>
public sealed partial class GameJoltDownloadResolver(ModHttp http) : IModDownloadResolver
{
    [GeneratedRegex(@"^/games/[^/]+/(\d+)/?$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectPath();
    /// <summary>Lists supported files, preserving package/release/build identities instead of guessing a first file.</summary>
    public async Task<ModDownloadResolution> ResolveAsync(CatalogMod mod, CancellationToken cancellationToken)
    {
        if (!mod.HasDownload) return new([], mod.IsPreview ? "Preview: no released download is available." : "No download is published. Open source page for author information.");
        var source = ModHttp.PublicUri(mod.FileUrl!);
        if (source.Host is "gamejolt.com" or "www.gamejolt.com")
        {
            var match = ProjectPath().Match(source.AbsolutePath);
            if (!match.Success) return new([], "This Game Jolt URL is not a supported project page. Open source page.");
            using var document = JsonDocument.Parse(await http.ReadAsync("https://gamejolt.com/site-api/web/discover/games/overview/" + match.Groups[1].Value, 4 * 1048576, cancellationToken));
            var payload = document.RootElement.GetProperty("payload");
            var releases = Array(payload, "releases"); var packages = Array(payload, "packages");
            List<ModDownloadFile> files = [];
            foreach (var build in Array(payload, "builds"))
            {
                if (Text(build, "type") != "downloadable" || Bool(build, "is_installer") || !build.TryGetProperty("primary_file", out var file)) continue;
                string? name = Text(file, "filename"), id = Text(build, "id");
                if (name is null || id is null || !SupportedName(name)) continue;
                string? releaseId = Text(build, "game_release_id"), packageId = Text(build, "game_package_id");
                var release = releases.FirstOrDefault(r => Text(r, "id") == releaseId);
                var package = packages.FirstOrDefault(p => Text(p, "id") == packageId);
                long? size = long.TryParse(Text(file, "filesize"), out long n) && n >= 0 ? n : null;
                files.Add(new("gamejolt", id, releaseId, Text(release, "version_number"), name,
                    Text(package, "title") ?? "Package " + packageId, size, source.AbsoluteUri));
            }
            return new(files.ToArray(), files.Count == 0 ? "Game Jolt did not publish a supported .win or ZIP here. Paid/private files and installers require Open source page." : null);
        }
        if (SupportedName(source.AbsolutePath)) return new([new("https", source.AbsoluteUri, null, null, Path.GetFileName(source.AbsolutePath), "Direct HTTPS file", null, source.AbsoluteUri)]);
        return new([], "This file host is not supported for direct downloads. Use Open source page, then Add mods for a local .win or ZIP.");
    }
    /// <summary>Refreshes expiring Game Jolt links at download time; no signed link is persisted.</summary>
    public async Task<Uri> GetFileUriAsync(ModDownloadFile file, CancellationToken cancellationToken)
    {
        if (file.Provider == "https") return ModHttp.PublicUri(file.SourceUrl);
        if (file.Provider != "gamejolt" || !ulong.TryParse(file.Id, out _)) throw new InvalidDataException("Unsupported download identity. Resolve the source again.");
        using var document = JsonDocument.Parse(await http.ReadAsync("https://gamejolt.com/site-api/web/discover/games/builds/get-download-url/" + file.Id,
            65536, cancellationToken, lookupPost: true));
        var payload = document.RootElement.GetProperty("payload");
        string? url = Text(payload, "url");
        if (url is null) throw new IOException("Game Jolt did not provide a public file URL. Retry or Open source page; a browser/login may be required.");
        return ModHttp.PublicUri(url);
    }
    private static bool SupportedName(string name) => name.EndsWith(".win", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
}
