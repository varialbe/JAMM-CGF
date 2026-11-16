using System.Net;
using System.Text.Json;
using Jamm.Core.Browsing;
using Jamm.Core.Building;
using Jamm.Gui.ViewModels;

namespace Jamm.Gui.Tests;

public sealed class SharedBrowseTests
{
    private sealed class Catalog : IModCatalogProvider
    {
        public Task<CatalogSnapshot> LoadAsync(CancellationToken token) => Task.FromResult(new CatalogSnapshot([new("1","Mod","Author","","https://example.com/mod.win",false,[],null,null)],DateTimeOffset.UtcNow));
    }
    private static readonly ModDownloadFile Entry = new("https","file",null,null,"mod.win","Main",null,"https://example.com/mod.win");
    private sealed class Resolver : IModDownloadResolver
    {
        public Task<ModDownloadResolution> ResolveAsync(CatalogMod mod,CancellationToken token) => Task.FromResult(new ModDownloadResolution([Entry]));
        public Task<Uri> GetFileUriAsync(ModDownloadFile file,CancellationToken token) => Task.FromResult(new Uri(file.SourceUrl));
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) => Task.FromResult(send(request)); }
    [Theory]
    [InlineData("Incompatible", "✕")]
    [InlineData("Unsure", "?")]
    [InlineData("Tool limitation", "?")]
    public async Task InfoPreservesFullFactReasonIncludingFailingEntry(string compatibility, string icon)
    {
        using var files = new TestFiles();
        const string reason = "The patch cannot apply: incompatible code.\nFailing entry: gml_Object_Fish_Create_0";
        string baseHash = BuildFiles.Hash(files.Store.Config().Base!);
        var remote = new SharedIndex(1, baseHash, ModFactAnalyzer.Version,
            [new(Entry.Provider, Entry.Id, "Mod", "1", null, null, null, DateTimeOffset.UtcNow,
                new(new string('a', 64), baseHash, ModFactAnalyzer.Version, compatibility, reason, []))]);
        var vm = new MainViewModel(files.Store,
            new ModHttp(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(remote)) })),
            new Catalog(), new Resolver());
        await vm.InitializeAsync(); await vm.Browse.RefreshAsync();
        vm.Browse.Selected = Assert.Single(vm.Browse.Mods); await vm.Browse.SelectAsync();
        Assert.Equal(icon, vm.Browse.Selected.CompatibilityIcon);
        Assert.True(vm.Browse.Selected.HasCompatibilityInfo);
        Assert.Contains(reason, vm.Browse.Selected.CompatibilityReason);
        vm.Browse.Dispose();
    }
    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task ServerHitAvoidsDownloadAndMismatchRechecksLocally(bool hit)
    {
        using var files = new TestFiles(); string source = files.Mod("Source","x = 2;\n");
        string baseHash = BuildFiles.Hash(files.Store.Config().Base!); int downloads = 0;
        // Deliberately claims different archive bytes: it may preview facts, but cannot survive import verification.
        var remote = new SharedIndex(1,baseHash,ModFactAnalyzer.Version,[new(Entry.Provider,Entry.Id,"Mod","1",null,null,null,DateTimeOffset.UtcNow,
            new(new string('a',64),baseHash,ModFactAnalyzer.Version,"Compatible","Remote preview marker",["42 sprites"]))]);
        using var http = new ModHttp(new Handler(request =>
        {
            if (request.RequestUri!.Host == "varialbe.xyz") return hit ? new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(remote)) } : new(HttpStatusCode.NotFound);
            downloads++; return new(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(source)) };
        }));
        var vm = new MainViewModel(files.Store,http,new Catalog(),new Resolver());
        await vm.InitializeAsync(); await vm.Browse.RefreshAsync(); vm.Browse.Selected = vm.Browse.Mods.Single(); await vm.Browse.SelectAsync();
        var row = vm.Browse.Selected!;
        if (hit) { Assert.Equal(0,downloads); Assert.Contains("checked by JAMM server",row.CompatibilityReason); }
        else { Assert.Equal(1,downloads); Assert.Contains("checked on this PC",row.CompatibilityReason); }
        await vm.Browse.DownloadAsync(false,_ => Task.FromResult(false));
        Assert.Equal(1,downloads); Assert.Contains("checked on this PC",row.CompatibilityReason); Assert.DoesNotContain("Remote preview marker",row.CompatibilityReason);
        Assert.Single(vm.Mods); vm.Browse.Dispose();
    }
}
