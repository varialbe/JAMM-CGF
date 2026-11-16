using System.Globalization;
using System.Text.Json;
using Jamm.Core.Building;
using Jamm.Core.Management;
using Microsoft.Data.Sqlite;

namespace Jamm.Core.Browsing;

/// <summary>Server-only scan, SQLite persistence and deterministic static export.</summary>
public sealed class CompatibilityScan(ManagerStore store, IModCatalogProvider catalog, IModDownloadResolver resolver, ModHttp http)
{
    /// <summary>Scans all released catalog files. Individual failures are reported and cause a nonzero result.</summary>
    public async Task<int> RunAsync(string database, string export, CancellationToken token, Action<string>? log = null,
        Func<string, CancellationToken, ModFacts>? analyze = null)
    {
        string basePath = store.Config().Base ?? throw new InvalidOperationException("Configure the read-only base archive before scanning.");
        BuildFiles.ValidateNewOutput(database, [basePath]);
        BuildFiles.RefuseStorageLink(database);
        if (Path.GetExtension(database).Equals(".win", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a .db database path separate from game archives.");
        string baseHash = BuildFiles.Hash(basePath);
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(database) }.ToString()); db.Open();
        using (var schema = db.CreateCommand()) { schema.CommandText = Schema; schema.ExecuteNonQuery(); }
        int failures = 0;
        var snapshot = await catalog.LoadAsync(token);
        foreach (var mod in snapshot.Mods.Where(m => m.HasDownload).OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            try
            {
                var resolution = await resolver.ResolveAsync(mod, token);
                if (resolution.Files.Length == 0) throw new InvalidDataException(resolution.Notice ?? "No releasable files resolved.");
                foreach (var file in resolution.Files.OrderBy(f => f.Provider, StringComparer.Ordinal).ThenBy(f => f.Id, StringComparer.Ordinal))
                {
                    string archive = await new ModDownloadCache(store, http).FetchFileAsync(file, resolver, null, token);
                    string hash = BuildFiles.Hash(archive);
                    using (var identity = db.CreateCommand())
                    {
                        identity.CommandText = "INSERT INTO source_files VALUES ($b,$m,$a,$p,$f,$n,$e,$v,$s) ON CONFLICT(base_sha256,provider,file_id,analyzer_version) DO UPDATE SET mod_sha256=excluded.mod_sha256,mod_name=excluded.mod_name,external_id=excluded.external_id,version=excluded.version,size_bytes=excluded.size_bytes";
                        foreach (var (key,value) in new (string,object?)[] { ("b",baseHash),("m",hash),("a",ModFactAnalyzer.Version),("p",file.Provider),("f",file.Id),("n",mod.Name),("e",mod.Id),("v",file.Version),("s",file.Size) }) identity.Parameters.AddWithValue("$"+key,value ?? DBNull.Value);
                        identity.ExecuteNonQuery();
                    }
                    using var exists = db.CreateCommand(); exists.CommandText = "SELECT COUNT(*) FROM checks WHERE base_sha256=$b AND mod_sha256=$m AND analyzer_version=$a";
                    exists.Parameters.AddWithValue("$b", baseHash); exists.Parameters.AddWithValue("$m", hash); exists.Parameters.AddWithValue("$a", ModFactAnalyzer.Version);
                    if ((long)exists.ExecuteScalar()! != 0) { log?.Invoke($"Already checked: {mod.Name} / {file.Id}"); continue; }
                    var facts = analyze is null ? new ModFactAnalyzer(store).Analyze(archive, token) : analyze(archive, token);
                    if (facts.BaseHash != baseHash || facts.FileHash != hash || facts.AnalyzerVersion != ModFactAnalyzer.Version) throw new InvalidDataException("Analyzer returned facts for different bytes.");
                    string? source;
                    using (var loaded = GameFileLoader.Load(archive, cancellationToken: token))
                        source = loaded.Inspection.Timestamp <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds((long)loaded.Inspection.Timestamp).ToString("O") : null;
                    using var insert = db.CreateCommand();
                    insert.CommandText = "INSERT INTO checks VALUES ($b,$m,$a,$c,$r,$adds,$source,$name,$provider,$external,$file,$version,$size,$checked) ON CONFLICT(base_sha256,mod_sha256,analyzer_version) DO NOTHING";
                    foreach (var (key, value) in new (string, object?)[] { ("b",baseHash),("m",hash),("a",facts.AnalyzerVersion),("c",facts.Compatibility),("r",facts.Reason),("adds",JsonSerializer.Serialize(facts.Adds)),("source",source),("name",mod.Name),("provider",file.Provider),("external",mod.Id),("file",file.Id),("version",file.Version),("size",file.Size),("checked",DateTimeOffset.UtcNow.ToString("O")) })
                        insert.Parameters.AddWithValue("$" + key, value ?? DBNull.Value);
                    insert.ExecuteNonQuery(); log?.Invoke($"Checked: {mod.Name} / {file.Id}: {facts.Compatibility}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { failures++; log?.Invoke($"Failed '{mod.Name}': {ex.Message} Retry the scan after correcting the source."); }
        }
        Export(db, baseHash, export); return failures;
    }
    private static void Export(SqliteConnection db, string baseHash, string export)
    {
        using var query = db.CreateCommand(); query.CommandText = "SELECT c.base_sha256,c.mod_sha256,c.analyzer_version,c.compatibility,c.reason,c.adds_json,c.source_build_utc,COALESCE(s.mod_name,c.mod_name),COALESCE(s.provider,c.provider),COALESCE(s.external_id,c.external_id),COALESCE(s.file_id,c.file_id),COALESCE(s.version,c.version),COALESCE(s.size_bytes,c.size_bytes),c.checked_utc FROM checks c JOIN source_files s ON c.base_sha256=s.base_sha256 AND c.mod_sha256=s.mod_sha256 AND c.analyzer_version=s.analyzer_version WHERE c.base_sha256=$b AND c.analyzer_version=$a ORDER BY 9 COLLATE BINARY,11 COLLATE BINARY,2 COLLATE BINARY";
        query.Parameters.AddWithValue("$b",baseHash); query.Parameters.AddWithValue("$a",ModFactAnalyzer.Version);
        using var rows = query.ExecuteReader(); List<SharedCheck> checks = [];
        string? Nullable(int i) => rows.IsDBNull(i) ? null : rows.GetString(i);
        while (rows.Read()) checks.Add(new(rows.GetString(8),rows.GetString(10),rows.GetString(7),Nullable(9),Nullable(11),rows.IsDBNull(12) ? null : rows.GetInt64(12),Nullable(6),DateTimeOffset.Parse(rows.GetString(13), CultureInfo.InvariantCulture),
            new(rows.GetString(1),baseHash,rows.GetInt32(2),rows.GetString(3),rows.GetString(4),JsonSerializer.Deserialize<string[]>(rows.GetString(5))!)));
        string dir = Path.Combine(Path.GetFullPath(export),"v1",baseHash); BuildFiles.RefuseStorageLink(dir); Directory.CreateDirectory(dir);
        void Write(string name, SharedCheck[] entries)
        {
            string target = Path.Combine(dir,name + ".json"), temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            BuildFiles.RefuseStorageLink(target);
            try { File.WriteAllText(temp,JsonSerializer.Serialize(new SharedIndex(1,baseHash,ModFactAnalyzer.Version,entries)) + "\n"); File.Move(temp,target,true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        foreach (var group in checks.GroupBy(c => c.Facts.FileHash)) Write(group.Key,group.ToArray());
        Write("index",checks.ToArray());
    }
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS checks (
        base_sha256 TEXT NOT NULL, mod_sha256 TEXT NOT NULL, analyzer_version INTEGER NOT NULL,
        compatibility TEXT NOT NULL, reason TEXT NOT NULL, adds_json TEXT NOT NULL,
        source_build_utc TEXT, mod_name TEXT NOT NULL, provider TEXT NOT NULL, external_id TEXT,
        file_id TEXT NOT NULL, version TEXT, size_bytes INTEGER, checked_utc TEXT NOT NULL,
        PRIMARY KEY(base_sha256,mod_sha256,analyzer_version));
        CREATE INDEX IF NOT EXISTS checks_source ON checks(base_sha256,provider,file_id,analyzer_version);
        CREATE TABLE IF NOT EXISTS source_files (
        base_sha256 TEXT NOT NULL,mod_sha256 TEXT NOT NULL,analyzer_version INTEGER NOT NULL,
        provider TEXT NOT NULL,file_id TEXT NOT NULL,mod_name TEXT NOT NULL,external_id TEXT,version TEXT,size_bytes INTEGER,
        PRIMARY KEY(base_sha256,provider,file_id,analyzer_version));
        """;
}
