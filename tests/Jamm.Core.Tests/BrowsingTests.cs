using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Jamm.Core.Browsing;
using Jamm.Core.Management;

namespace Jamm.Core.Tests;

public sealed class BrowsingTests
{
    private static CatalogMod Mod(string? url = "https://gamejolt.com/games/example/123", bool preview = false) => new("40", "Example", "Author", "Description", url, preview, [], null, null);
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    private static byte[] Form() => [70, 79, 82, 77, 4, 0, 0, 0, 71, 69, 78, 56];
    private sealed class Storage : IDisposable
    {
        public ManagerStore Store { get; } = new(Path.Combine(Path.GetTempPath(), "jamm-browse-" + Guid.NewGuid().ToString("N")));
        public void Dispose() => Directory.Delete(Store.Root, true);
    }
    [Fact]
    public void CatalogMappingToleratesOptionalFieldsAndPreservesPreview()
    {
        var mods = LitterboxCatalogProvider.Parse(Encoding.UTF8.GetBytes("""
          [{"id":40,"name":"Example","is_preview":true},
           {"id":41,"name":"Other","author":"A","file_url":"https://example.com/data.win","rating":4.5,
            "mod_screenshots":[{"url":"https://example.com/image.png","sort_order":1}],
            "mod_credits":[{"display_name":"Helper","comment":"Art"}],
            "profiles":{"is_verified":true},"mod_categories":[{"categories":{"name":"Fish"}},{"categories":null}]}]
          """));
        Assert.Equal("40", mods[0].Id); Assert.Equal("Unknown author", mods[0].Author);
        Assert.False(mods[0].HasDownload); Assert.Empty(mods[0].Categories);
        Assert.True(mods[1].HasDownload); Assert.True(mods[1].VerifiedCreator); Assert.Equal(4.5, mods[1].Rating);
        Assert.Equal(["https://example.com/image.png"], Assert.IsType<string[]>(mods[1].Screenshots)); Assert.Contains("Helper: Art", mods[1].Credits);
        Assert.Equal(["Fish"], mods[1].Categories); Assert.Null(mods[1].CreatedUtc);
    }
    [Fact]
    public async Task FirstPartyCatalogUsesCatalogModArrayAndRetainsPublisherProvenance()
    {
        var mod = Assert.Single(FirstPartyCatalogProvider.Parse(Encoding.UTF8.GetBytes("""
            [{"id":"abyss","name":"The Abyss","author":"varialbe","description":"Official","fileUrl":"https://gamejolt.com/games/ringdingdingringring/842483","isPreview":false,"categories":["Content"],"iconPngUrl":"https://varialbe.xyz/cgf-mods/assets/abyss.png","compat":{"requiresBase":"dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1","jammMin":"0.9.1","conflicts":["jamm-silo"],"knownIssues":["known"]}}]
            """)));
        Assert.Equal("first-party", mod.Catalog);
        Assert.Equal("https://varialbe.xyz/cgf-mods/", mod.SourcePage);
        var metadata = Assert.IsType<FirstPartyCompatibility>(mod.Compat);
        Assert.Equal("0.9.1", metadata.JammMin);
        Assert.Equal("https://varialbe.xyz/cgf-mods/assets/abyss.png", mod.IconPngUrl);
        Assert.Equal("dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1", metadata.RequiresBase);
        Assert.True(GameJoltDownloadResolver.SupportsSource(mod));
        var official = new OfficialAbyssCatalogProvider();
        var abyss = Assert.Single((await official.LoadAsync(default)).Mods);
        Assert.Equal("gamejolt", abyss.Catalog);
        Assert.Equal("https://gamejolt.com/games/ringdingdingringring/842483", abyss.SourcePage);
        Assert.True(GameJoltDownloadResolver.SupportsSource(abyss));
    }
    [Fact]
    public async Task PreviewAndUnsupportedSourcesNeverMasqueradeAsFiles()
    {
        using var http = new ModHttp(new Handler((_, _) => throw new Exception("No network expected")));
        var resolver = new GameJoltDownloadResolver(http);
        Assert.Empty((await resolver.ResolveAsync(Mod(preview: true), default)).Files);
        Assert.Empty((await resolver.ResolveAsync(Mod("https://example.com/project"), default)).Files);
        var direct = Assert.Single((await resolver.ResolveAsync(Mod("https://example.com/data.win"), default)).Files);
        Assert.Equal("https", direct.Provider); Assert.Null(direct.Version); Assert.Null(direct.Size);
    }
    [Fact]
    public async Task ProjectPackagesReleasesAndAllFilesAreMappedAndSignedUrlIsRefreshed()
    {
        int lookups = 0;
        using var http = new ModHttp(new Handler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("overview")) return Json("""
              {"payload":{"packages":[{"id":10,"title":"Main"},{"id":11,"title":"Extras"}],
              "releases":[{"id":20,"version_number":"5.0.0"},{"id":21}],
              "builds":[{"id":30,"type":"downloadable","game_package_id":10,"game_release_id":20,"primary_file":{"filename":"data.win","filesize":"120"}},
                        {"id":31,"type":"downloadable","game_package_id":11,"game_release_id":21,"primary_file":{"filename":"mod.zip"}},
                        {"id":32,"type":"downloadable","primary_file":{"filename":"installer.exe"}}]}}
              """);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("forceDownload", await request.Content!.ReadAsStringAsync(token));
            return Json("{\"payload\":{\"url\":\"https://download.gamejolt.net/file" + ++lookups + ".win\"}}");
        }));
        var resolver = new GameJoltDownloadResolver(http);
        var files = (await resolver.ResolveAsync(Mod(), default)).Files;
        Assert.Equal(2, files.Length); Assert.Equal("Main", files[0].Package); Assert.Equal("20", files[0].ReleaseId);
        Assert.Equal("5.0.0", files[0].Version); Assert.Equal(120, files[0].Size); Assert.Null(files[1].Version);
        Assert.NotEqual(await resolver.GetFileUriAsync(files[0], default), await resolver.GetFileUriAsync(files[0], default));
    }
    [Theory]
    [InlineData("https://127.0.0.1/data.win")]
    [InlineData("http://example.com/data.win")]
    [InlineData("https://user:password@example.com/data.win")]
    [InlineData("https://192.168.2.3/data.win")]
    [InlineData("https://[::ffff:127.0.0.1]/data.win")]
    [InlineData("https://host.local/data.win")]
    [InlineData("https://example.com:8443/data.win")]
    public void RejectsNonPublicUrls(string url) => Assert.Throws<HttpRequestException>(() => ModHttp.PublicUri(url));
    [Theory]
    [InlineData("10.0.0.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("2002:7f00:1::", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void ConnectAddressPolicy(string ip, bool expected) => Assert.Equal(expected, ModHttp.IsPublicAddress(IPAddress.Parse(ip)));
    [Theory]
    [InlineData("https://127.0.0.1/data.win")]
    [InlineData("http://example.com/data.win")]
    public async Task RedirectTargetsAreCheckedBeforeSending(string destination)
    {
        int requests = 0;
        using var http = new ModHttp(new Handler((_, _) =>
        {
            requests++; var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri(destination); return Task.FromResult(response);
        }));
        await Assert.ThrowsAsync<HttpRequestException>(() => http.ReadAsync("https://example.com/data.win", 1024, default));
        Assert.Equal(1, requests);
    }
    [Fact]
    public async Task PublicRedirectWorksAndCatalogCredentialsDoNotLeak()
    {
        int requests = 0;
        using var http = new ModHttp(new Handler((request, _) =>
        {
            if (++requests == 1) { var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri("https://other.example/file"); return Task.FromResult(response); }
            Assert.False(request.Headers.Contains("apikey")); return Task.FromResult(Json("ok"));
        }));
        Assert.Equal("ok", Encoding.UTF8.GetString(await http.ReadAsync("https://example.com/file", 10, default, "public-key")));
    }
    [Theory]
    [InlineData("html")]
    [InlineData("size")]
    [InlineData("stream-size")]
    [InlineData("network")]
    [InlineData("truncated")]
    public async Task FailedTransfersCleanStaging(string failure)
    {
        using var storage = new Storage();
        using var http = new ModHttp(new Handler((_, _) =>
        {
            if (failure == "network") throw new HttpRequestException("offline");
            HttpContent content = failure == "html" ? new StringContent("<html>error page</html>") : new ByteArrayContent(Form());
            if (failure == "size") content.Headers.ContentLength = 10000;
            if (failure == "stream-size") { content = new StreamContent(new MemoryStream(new byte[1000])); content.Headers.ContentLength = null; }
            if (failure == "truncated") content.Headers.ContentLength = 15;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }));
        await Assert.ThrowsAnyAsync<Exception>(() => new ModDownloadService(storage.Store, http, 100).DownloadAsync(new("https://example.com/mod.win"), null, default));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(storage.Store.Root, "downloads")));
    }
    [Fact]
    public async Task TransferCancellationRemovesPartialFile()
    {
        using var storage = new Storage(); using var cancel = new CancellationTokenSource();
        using var http = new ModHttp(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new CancellingStream(cancel)) })));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ModDownloadService(storage.Store, http).DownloadAsync(new("https://example.com/mod.win"), null, cancel.Token));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(storage.Store.Root, "downloads")));
    }
    private sealed class CancellingStream(CancellationTokenSource cancel) : MemoryStream(Form())
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { cancel.Cancel(); return ValueTask.FromCanceled<int>(token); }
    }
    [Fact]
    public async Task ContentDeterminesExtensionAndUnknownSizeProgressIsIndeterminate()
    {
        using var storage = new Storage();
        using var http = new ModHttp(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnknownLengthStream(Form())) })));
        var reports = new List<ModDownloadProgress>();
        string path = await new ModDownloadService(storage.Store, http).DownloadAsync(new("https://example.com/anything"), new InlineProgress(reports), default);
        Assert.EndsWith(".win", path); Assert.Equal(Form(), File.ReadAllBytes(path)); Assert.NotEmpty(reports);
        Assert.All(reports, p => Assert.Null(p.Total)); File.Delete(path);
    }
    private sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
    private sealed class InlineProgress(List<ModDownloadProgress> reports) : IProgress<ModDownloadProgress> { public void Report(ModDownloadProgress value) => reports.Add(value); }
    [Theory]
    [InlineData("safe", true)]
    [InlineData("traversal", false)]
    [InlineData("backslash", false)]
    [InlineData("absolute", false)]
    [InlineData("symlink", false)]
    [InlineData("multiple", false)]
    [InlineData("missing", false)]
    [InlineData("expanded", false)]
    [InlineData("bad-form", false)]
    public void ValidatesZipStructureAndExpandedLimits(string kind, bool success)
    {
        using var storage = new Storage(); string path = Path.Combine(storage.Store.Root, "test.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            string name = kind switch { "traversal" => "../data.win", "backslash" => "..\\data.win", "absolute" => "/data.win", "missing" => "other.win", _ => "nested/data.win" };
            var entry = zip.CreateEntry(name); if (kind == "symlink") entry.ExternalAttributes = 0xA000 << 16;
            using (var stream = entry.Open()) stream.Write(kind == "bad-form" ? new byte[12] : Form());
            if (kind == "multiple") { using var stream = zip.CreateEntry("data.win").Open(); stream.Write(Form()); }
            if (kind == "expanded") { using var stream = zip.CreateEntry("unused.txt").Open(); stream.Write(new byte[200]); }
        }
        if (success) Assert.Equal(".zip", ModDownloadService.Validate(path, 100));
        else Assert.Throws<InvalidDataException>(() => ModDownloadService.Validate(path, 100));
    }
    [Fact]
    public async Task CatalogCachesOnlySuccessfulFetchAndFallsBackOffline()
    {
        using var storage = new Storage(); bool offline = false;
        using var http = new ModHttp(new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method); Assert.True(request.Headers.Contains("apikey"));
            if (offline) throw new HttpRequestException("offline");
            return Task.FromResult(Json("[{\"id\":40,\"name\":\"Example\"}]"));
        }));
        var catalog = new LitterboxCatalogProvider(storage.Store, http);
        var first = await catalog.LoadAsync(default); offline = true;
        var cached = await catalog.LoadAsync(default);
        Assert.Equal(first.FetchedUtc, cached.FetchedUtc); Assert.Single(cached.Mods); Assert.Contains("offline", cached.OfflineReason);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.LoadAsync(cancel.Token));
    }
    [Fact]
    public async Task TimeoutIsDifferentFromUserCancellationAndCleansStaging()
    {
        using var storage = new Storage();
        using var http = new ModHttp(new Handler((_, _) => throw new TaskCanceledException("simulated transport timeout")));
        var error = await Assert.ThrowsAsync<TimeoutException>(() => new ModDownloadService(storage.Store, http).DownloadAsync(new("https://example.com/data.win"), null, default));
        Assert.Contains("timed out", error.Message); Assert.Empty(Directory.GetFiles(Path.Combine(storage.Store.Root, "downloads")));
        await Assert.ThrowsAsync<TimeoutException>(() => http.ReadAsync("https://example.com/metadata", 100, default));
    }
    [Fact]
    public void OlderMetadataWithoutOriginsStillDeserializes()
    {
        var mod = JsonSerializer.Deserialize<ManagedMod>("""{"Id":"hash","Name":"Manual","SourceFileName":"data.win","Sha256":"hash","AddedUtc":"2026-01-01T00:00:00Z","BaseSha256":"base","Gen8Timestamp":1,"GmVersion":"1","Bytecode":16}""");
        Assert.NotNull(mod); Assert.Empty(mod.Origins);
    }
}
