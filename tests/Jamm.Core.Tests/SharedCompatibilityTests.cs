using System.Net;
using System.Text.Json;
using Jamm.Core.Browsing;
using Jamm.Core.Building;
using Jamm.Core.Management;
using Microsoft.Data.Sqlite;
using UndertaleModLib;

namespace Jamm.Core.Tests;

public sealed class SharedCompatibilityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jamm-shared-" + Guid.NewGuid().ToString("N"));
    private static readonly string BaseHash = new('a',64), ModHash = new('b',64);
    private static readonly ModDownloadFile FileEntry = new("test","42",null,null,"mod.win","Main",null,"https://example.com/mod.win");
    private ManagerStore Store => new(Path.Combine(root,"state"));
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) => send(request,token); }
    private static SharedIndex Document() => new(1,BaseHash,ModFactAnalyzer.Version,[new("test","42","Mod","catalog",null,null,null,DateTimeOffset.Parse("2026-01-01T00:00:00Z"),new(ModHash,BaseHash,ModFactAnalyzer.Version,"Compatible","Verified",["2 sprites"]))]);
    private static HttpResponseMessage Response(SharedIndex d) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(d)) };
    [Fact]
    public async Task HitRevalidatesETagAndRejectsDownloadedHashMismatch()
    {
        int calls = 0;
        using var http = new ModHttp(new Handler((request,_) =>
        {
            Assert.Empty(request.RequestUri!.Query); Assert.False(request.Headers.Contains("Cookie"));
            calls++;
            if (calls == 2) { Assert.Equal("\"v1\"",request.Headers.IfNoneMatch.Single().ToString()); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)); }
            var response = Response(Document()); response.Headers.ETag = new("\"v1\""); return Task.FromResult(response);
        }));
        var shared = new SharedCompatibility(Store,http);
        Assert.NotNull(await shared.FindAsync(BaseHash,FileEntry,null,default));
        Assert.NotNull(await shared.FindAsync(BaseHash,FileEntry,null,default));
        Assert.Null(await shared.FindAsync(BaseHash,FileEntry,new string('c',64),default));
        Assert.NotNull(await shared.FindAsync(BaseHash,FileEntry,ModHash,default));
    }
    [Theory]
    [InlineData("miss")][InlineData("offline")][InlineData("json")][InlineData("version")][InlineData("schema")][InlineData("null")][InlineData("timeout")][InlineData("oversized")][InlineData("facts-version")]
    public async Task InvalidOrUnavailableResultsReturnLocalFallback(string failure)
    {
        using var http = new ModHttp(new Handler(async (_,token) =>
        {
            if (failure == "offline") throw new HttpRequestException("Offline");
            if (failure == "timeout") { await Task.Delay(Timeout.Infinite,token); }
            if (failure == "miss") return new(HttpStatusCode.NotFound);
            if (failure == "json") return new(HttpStatusCode.OK) { Content = new StringContent("invalid") };
            if (failure == "null") return new(HttpStatusCode.OK) { Content = new StringContent("{\"SchemaVersion\":1,\"BaseHash\":\""+ BaseHash +"\",\"AnalyzerVersion\":4,\"Checks\":null}") };
            if (failure == "oversized") return new(HttpStatusCode.OK) { Content = new StringContent(new string(' ', 2 * 1048576 + 1)) };
            var d = Document();
            if (failure == "facts-version") return Response(d with { Checks = [d.Checks[0] with { Facts = d.Checks[0].Facts with { AnalyzerVersion = -1 } }] });
            return Response(failure == "version" ? d with { AnalyzerVersion = -1 } : d with { SchemaVersion = -1 });
        }));
        Assert.Null(await new SharedCompatibility(Store,http).FindAsync(BaseHash,FileEntry,null,default));
    }
    [Fact]
    public async Task OptOutPersistsAndSendsNoRequest()
    {
        Assert.True(Store.Config().UseSharedCompatibilityResults);
        Store.Set("useSharedCompatibilityResults","false");
        using var http = new ModHttp(new Handler((_,_) => throw new Exception("No request allowed")));
        Assert.Null(await new SharedCompatibility(Store,http).FindAsync(BaseHash,FileEntry,null,default));
        Assert.False(Store.Config().UseSharedCompatibilityResults);
    }
    [Fact]
    public async Task LastModifiedIsSentForConditionalRequests()
    {
        int calls = 0;
        var modified = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        using var http = new ModHttp(new Handler((request,_) =>
        {
            calls++;
            if (calls == 2) { Assert.Equal(modified,request.Headers.IfModifiedSince); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)); }
            var response = Response(Document()); response.Content.Headers.LastModified = modified; return Task.FromResult(response);
        }));
        var shared = new SharedCompatibility(Store,http);
        Assert.NotNull(await shared.FindAsync(BaseHash,FileEntry,null,default));
        Assert.NotNull(await shared.FindAsync(BaseHash,FileEntry,null,default));
    }
    private sealed class Catalog : IModCatalogProvider
    {
        public Task<CatalogSnapshot> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new CatalogSnapshot([
            new("catalog","Synthetic","Tester","",FileEntry.SourceUrl,false,[],null,null),
            new("preview","Preview","Tester","",null,true,[],null,null)],DateTimeOffset.UtcNow));
    }
    private sealed class Resolver(bool aliases = false) : IModDownloadResolver
    {
        public Task<ModDownloadResolution> ResolveAsync(CatalogMod mod,CancellationToken cancellationToken) => Task.FromResult(new ModDownloadResolution(aliases ? [FileEntry, FileEntry with { Id = "43" }] : [FileEntry]));
        public Task<Uri> GetFileUriAsync(ModDownloadFile file,CancellationToken cancellationToken) => Task.FromResult(new Uri(file.SourceUrl));
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task ScanUsesSyntheticArchiveAndExportsIdempotently(bool aliases)
    {
        Directory.CreateDirectory(root);
        string original = Path.Combine(root,"base.win");
        using (var data = UndertaleData.CreateNew()) { data.GeneralInfo.Timestamp = 1692207312; using var stream = System.IO.File.Create(original); UndertaleIO.Write(stream,data); }
        var bytes = System.IO.File.ReadAllBytes(original); string hash = BuildFiles.Hash(original);
        Store.Set("base",original);
        int downloads = 0, analyses = 0;
        using var http = new ModHttp(new Handler((_,_) => { downloads++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }); }));
        var scan = new CompatibilityScan(Store,new Catalog(),new Resolver(aliases),http);
        string database = Path.Combine(root,"compat.db"), export = Path.Combine(root,"public");
        ModFacts Analyze(string path,CancellationToken token) { analyses++; return new(BuildFiles.Hash(path),hash,ModFactAnalyzer.Version,"Compatible","Synthetic",[]); }
        Assert.Equal(0,await scan.RunAsync(database,export,default,analyze:Analyze));
        string index = Path.Combine(export,"v1",hash,"index.json"); string first = System.IO.File.ReadAllText(index);
        Assert.Equal(0,await scan.RunAsync(database,export,default,analyze:Analyze));
        Assert.Equal(first,System.IO.File.ReadAllText(index)); Assert.Equal(1,analyses); Assert.Equal(aliases ? 2 : 1,downloads);
        var document = JsonSerializer.Deserialize<SharedIndex>(first)!;
        Assert.Equal(aliases ? 2 : 1,document.Checks.Length); Assert.All(document.Checks,c => Assert.Equal(hash,c.Facts.FileHash));
        using var db = new SqliteConnection($"Data Source={database}"); db.Open(); using var count = db.CreateCommand(); count.CommandText = "SELECT COUNT(*) FROM checks";
        Assert.Equal(1L,count.ExecuteScalar());
        Assert.Equal(first,System.IO.File.ReadAllText(Path.Combine(export,"v1",hash,hash+".json")));
        Assert.Equal(hash,BuildFiles.Hash(original));
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root,true); }
}
