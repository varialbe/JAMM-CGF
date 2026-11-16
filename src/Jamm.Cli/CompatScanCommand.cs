using Jamm.Core.Browsing;
using Jamm.Core.Management;

namespace Jamm.Cli;

internal static class CompatScanCommand
{
    internal static int Run(string[] args, string? dataDirectory)
    {
        Dictionary<string,string> options = [];
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (key is not "--base" and not "--db" and not "--export" || ++i == args.Length || !options.TryAdd(key,args[i]))
                throw new ArgumentException("compat-scan needs --base <data.win> --db <compat.db> --export <directory>, each once.");
        }
        if (options.Count != 3) throw new ArgumentException("compat-scan needs --base, --db and --export.");
        string original = Path.GetFullPath(options["--base"]), database = Path.GetFullPath(options["--db"]), export = Path.GetFullPath(options["--export"]);
        if (original == database || Path.GetExtension(database).Equals(".win",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a new .db path, separate from game archives.");
        var store = new ManagerStore(dataDirectory); store.Set("base",original);
        using var http = new ModHttp();
        int failures = new CompatibilityScan(store,new LitterboxCatalogProvider(store,http),new GameJoltDownloadResolver(http),http)
            .RunAsync(database,export,CancellationToken.None,Console.WriteLine).GetAwaiter().GetResult();
        return failures == 0 ? 0 : 1;
    }
}
