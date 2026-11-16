using System.Net;
using System.Net.Http.Headers;
using Jamm.Core.Browsing;
using Jamm.Core.Management;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

public sealed class Phase9eTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
    private static readonly byte[] Form = [70, 79, 82, 77, 4, 0, 0, 0, 71, 69, 78, 56];
    private static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private sealed class Storage : IDisposable
    {
        public ManagerStore Store { get; } = new(Path.Combine(Path.GetTempPath(), "jamm-9e-" + Guid.NewGuid().ToString("N")));
        public void Dispose() { if (Directory.Exists(Store.Root)) Directory.Delete(Store.Root, true); }
    }
    [Theory]
    [InlineData("https://gamejolt.com/get/build?game=839850&build=1700162", "1700162")]
    [InlineData("https://gamejolt.com/dashboard/games/839850/packages/100", "1700162")]
    public async Task ExactBuildAndPackageLinksFilterPublicOverview(string url, string expected)
    {
        using var http = new ModHttp(new Handler((r, _) =>
        {
            Assert.EndsWith("overview/839850", r.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                {"payload":{"builds":[
                {"id":1700162,"game_package_id":100,"type":"downloadable","is_installer":true,"primary_file":{"filename":"data.win"}},
                {"id":1700163,"game_package_id":101,"type":"downloadable","primary_file":{"filename":"other.win"}}]}}
                """) });
        }));
        var result = await new GameJoltDownloadResolver(http).ResolveAsync(new("4", "Test", "A", "", url, false, [], null, null), default);
        Assert.Equal(expected, Assert.Single(result.Files).Id);
    }
    [Fact]
    public async Task FlakyConnectionRetriesAndCleansStaging()
    {
        using var storage = new Storage(); int requests = 0;
        using var http = new ModHttp(new Handler((_, _) => ++requests == 1 ? throw new HttpRequestException("DNS failed") : Task.FromResult(Response(Form))));
        string path = await new ModDownloadService(storage.Store, http) { RetryDelay = TimeSpan.Zero }.DownloadAsync(new Uri("https://example.com/file"), null, default);
        Assert.Equal(2, requests); Assert.Equal(Form, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(Path.Combine(storage.Store.Root, "downloads"), "*.part"));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TruncatedBodyResumesWithRangeAndValidatorOrRestartsWhenIgnored(bool ignoreRange)
    {
        using var storage = new Storage(); int requests = 0;
        using var http = new ModHttp(new Handler((r, _) =>
        {
            HttpResponseMessage response;
            if (++requests == 1)
            {
                response = Response(Form[..8]); response.Content.Headers.ContentLength = Form.Length;
            }
            else
            {
                Assert.Equal(8, r.Headers.Range!.Ranges.Single().From);
                Assert.Equal("\"stable\"", r.Headers.IfRange!.EntityTag!.Tag);
                response = Response(ignoreRange ? Form : Form[8..]);
                if (!ignoreRange) { response.StatusCode = HttpStatusCode.PartialContent; response.Content.Headers.ContentRange = new(8, 11, 12); }
            }
            response.Headers.AcceptRanges.Add("bytes"); response.Headers.ETag = new("\"stable\"");
            return Task.FromResult(response);
        }));
        var path = await new ModDownloadService(storage.Store, http) { RetryDelay = TimeSpan.Zero }.DownloadAsync(new Uri("https://example.com/file"), null, default);
        Assert.Equal(Form, File.ReadAllBytes(path)); Assert.Equal(2, requests);
    }
    [Fact]
    public async Task ChangedResumeValidatorRestartsWithoutMixingBodies()
    {
        using var storage = new Storage(); int requests = 0;
        using var http = new ModHttp(new Handler((r, _) =>
        {
            int attempt = ++requests;
            var response = Response(attempt == 1 ? Form[..8] : attempt == 2 ? Form[8..] : Form);
            response.Headers.AcceptRanges.Add("bytes"); response.Headers.ETag = new(attempt == 1 ? "\"old\"" : "\"new\"");
            if (attempt == 1) response.Content.Headers.ContentLength = 12;
            if (attempt == 2) { response.StatusCode = HttpStatusCode.PartialContent; response.Content.Headers.ContentRange = new(8, 11, 12); }
            if (attempt == 3) Assert.Null(r.Headers.Range);
            return Task.FromResult(response);
        }));
        var path = await new ModDownloadService(storage.Store, http) { RetryDelay = TimeSpan.Zero }.DownloadAsync(new Uri("https://example.com/file"), null, default);
        Assert.Equal(Form, File.ReadAllBytes(path)); Assert.Equal(3, requests);
    }
    [Fact]
    public async Task ExpiredSignedUrlIsResolvedAgain()
    {
        using var storage = new Storage(); int resolutions = 0;
        using var http = new ModHttp(new Handler((r, _) => Task.FromResult(r.RequestUri!.AbsolutePath == "/expired"
            ? new HttpResponseMessage(HttpStatusCode.Forbidden) : Response(Form))));
        var path = await new ModDownloadService(storage.Store, http) { RetryDelay = TimeSpan.Zero }.DownloadResolvedAsync(
            _ => Task.FromResult(new Uri("https://example.com/" + (++resolutions == 1 ? "expired" : "fresh"))), null, default);
        Assert.Equal(2, resolutions); Assert.Equal(Form, File.ReadAllBytes(path));
    }
    [Fact]
    public async Task WrongLengthFailsAndRemovesPartialFile()
    {
        using var storage = new Storage(); int requests = 0;
        using var http = new ModHttp(new Handler((_, _) => { requests++; var r = Response(Form[..8]); r.Content.Headers.ContentLength = 12; return Task.FromResult(r); }));
        await Assert.ThrowsAsync<IOException>(() => new ModDownloadService(storage.Store, http) { RetryDelay = TimeSpan.Zero }.DownloadAsync(new Uri("https://example.com/file"), null, default));
        Assert.Equal(4, requests); Assert.Empty(Directory.GetFiles(Path.Combine(storage.Store.Root, "downloads")));
    }
    private sealed class StallingStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { await Task.Delay(Timeout.Infinite, token); return 0; }
    }
    [Fact]
    public async Task StalledBodyTimesOutAndRemovesPartialFile()
    {
        using var storage = new Storage();
        using var http = new ModHttp(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) })));
        await Assert.ThrowsAsync<TimeoutException>(() => new ModDownloadService(storage.Store, http) { RetryDelay = TimeSpan.Zero, StallTimeout = TimeSpan.FromMilliseconds(10) }.DownloadAsync(new Uri("https://example.com/file"), null, default));
        Assert.Empty(Directory.GetFiles(Path.Combine(storage.Store.Root, "downloads")));
    }
}

public sealed partial class BuildingTests
{
    [Fact]
    public void BracketedAssetNameRoundTripsAsSymbolInsteadOfArrayLiteral()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), modPath = temp.PathFor("mod.win");
        WriteModel(basePath, _ => { });
        WriteModel(modPath, data =>
        {
            data.GameObjects[0].Name = data.Strings.MakeString("[REDACTED]");
            data.Code[0].Name = data.Strings.MakeString("gml_Object_CatalogData_Create_0");
            var compiler = new CompileGroup(data);
            compiler.QueueCodeReplace(data.Code[0], "object[0, 0] = 0;");
            Assert.True(compiler.Compile().Successful);
        });
        using var original = Jamm.Core.GameFileLoader.Load(basePath);
        using var mod = Jamm.Core.GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod);
        string text = patch.Changes["code"].Added.Single().ModCode!.Text;
        Assert.Contains("__cgfmm_asset_objects_", text);
        Assert.DoesNotContain("= [REDACTED]", text);
        var built = Jamm.Core.Building.ArchiveBuilder.Build(basePath, [new(patch, modPath)], temp.PathFor("output.win"));
        Assert.NotNull(built.Verification);
        // Existing imported patches retain their source text; repair the old decompiler spelling too.
        var code = patch.Changes["code"].Added.Single();
        patch.Changes["code"] = patch.Changes["code"] with { Added = [code with { ModCode = code.ModCode! with { Text = "object[0, 0] = [REDACTED];" } }] };
        Assert.NotNull(Jamm.Core.Building.ArchiveBuilder.Build(basePath, [new(patch, modPath)], temp.PathFor("cached.win")).Verification);
    }
}

public sealed class Phase9eCacheTests
{
    private sealed class Resolver : IModDownloadResolver
    {
        public Task<Uri> GetFileUriAsync(ModDownloadFile file, CancellationToken token) => Task.FromResult(new Uri(file.SourceUrl));
        public Task<ModDownloadResolution> ResolveAsync(CatalogMod mod, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls); await Task.Delay(20, token);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent([70, 79, 82, 77, 4, 0, 0, 0, 71, 69, 78, 56]) };
        }
    }
    [Fact]
    public async Task ConcurrentChecksShareOneTransferAndHashAddressedCache()
    {
        string root = Path.Combine(Path.GetTempPath(), "jamm-9e-cache-" + Guid.NewGuid().ToString("N"));
        var store = new ManagerStore(root);
        try
        {
            var handler = new Handler(); using var http = new ModHttp(handler);
            var file = new ModDownloadFile("https", "id", null, null, "data.win", "Main", null, "https://example.com/data.win");
            var a = new ModDownloadCache(store, http).FetchFileAsync(file, new Resolver(), null, default);
            var b = new ModDownloadCache(store, http).FetchFileAsync(file, new Resolver(), null, default);
            var paths = await Task.WhenAll(a, b);
            Assert.Equal(paths[0], paths[1]); Assert.Equal(1, handler.Calls);
            Assert.Equal(Jamm.Core.Building.BuildFiles.Hash(paths[0]) + ".win", Path.GetFileName(paths[0]));
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "downloads")));
        }
        finally { Directory.Delete(root, true); }
    }
}
