using System.Buffers.Binary;
using System.IO.Compression;
using Jamm.Core.Management;

namespace Jamm.Core.Browsing;

/// <summary>Streams to guarded manager staging, validates content and removes incomplete transfers.</summary>
public sealed class ModDownloadService(ManagerStore store, ModHttp http, long maximumBytes = 512L * 1048576)
{
    /// <summary>Downloads and checks an archive; the caller must delete the returned staging file after import.</summary>
    public async Task<string> DownloadAsync(Uri uri, IProgress<ModDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        string directory = store.Path("downloads"); Directory.CreateDirectory(directory);
        string path = store.Path("downloads", Guid.NewGuid().ToString("N") + ".part");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromMinutes(15));
        try
        {
            using var response = await http.SendAsync(uri, timeout.Token);
            long? total = response.Content.Headers.ContentLength;
            if (total > maximumBytes) throw new InvalidDataException("Download is larger than the archive safety limit. Choose a smaller .win or ZIP.");
            progress?.Report(new(0, total));
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                await ModHttp.CopyBoundedAsync(await response.Content.ReadAsStreamAsync(timeout.Token), output, maximumBytes, total, progress, timeout.Token);
            string extension = await Task.Run(() => Validate(path, maximumBytes, timeout.Token), timeout.Token);
            string complete = store.Path("downloads", Path.GetFileNameWithoutExtension(path) + extension);
            timeout.Token.ThrowIfCancellationRequested(); File.Move(path, complete);
            return complete;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            if (File.Exists(path)) File.Delete(path);
            throw new TimeoutException("Download timed out. Check your connection and Retry; the partial file was removed.", ex);
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
    /// <summary>Checks FORM length or safe ZIP structure and bounded actual decompression, not filename/Content-Type.</summary>
    public static string Validate(string path, long maximumBytes, CancellationToken token = default)
    {
        using var input = File.OpenRead(path);
        Span<byte> header = stackalloc byte[8]; input.ReadExactly(header); input.Position = 0;
        if (header[..4].SequenceEqual("FORM"u8)) { ValidateForm(input, input.Length); return ".win"; }
        if (!header[..2].SequenceEqual("PK"u8)) throw new InvalidDataException("The source returned HTML or another unsupported file instead of a GameMaker archive/ZIP. Open source page and select a .win or ZIP.");
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count > 4096) throw new InvalidDataException("ZIP has too many entries. Choose a ZIP containing one data.win.");
        long expanded = 0; List<ZipArchiveEntry> candidates = [];
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            ValidateZipEntry(entry);
            if (entry.Length > maximumBytes - expanded) throw new InvalidDataException("ZIP expanded data exceeds the archive safety limit.");
            expanded += entry.Length;
            if (entry.FullName.Replace('\\', '/').Split('/').Last().Equals("data.win", StringComparison.OrdinalIgnoreCase)) candidates.Add(entry);
        }
        if (candidates.Count != 1) throw new InvalidDataException("ZIP must contain exactly one data.win; choose a different release/file.");
        using var win = candidates[0].Open(); ValidateForm(win, candidates[0].Length);
        byte[] buffer = new byte[65536]; long count = 8;
        while (true) { token.ThrowIfCancellationRequested(); int read = win.Read(buffer); if (read == 0) break; count += read; if (count > maximumBytes) throw new InvalidDataException("Expanded data.win exceeds the archive safety limit."); }
        if (count != candidates[0].Length) throw new InvalidDataException("ZIP contains an incomplete data.win. Retry the download.");
        return ".zip";
    }
    internal static void ValidateZipEntry(ZipArchiveEntry entry)
    {
        string normalized = entry.FullName.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.Split('/').Any(p => p == "..")
            || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
            throw new InvalidDataException("Unsafe ZIP entry: " + entry.FullName + ". Choose an archive without traversal, absolute paths or symbolic links.");
    }
    private static void ValidateForm(Stream stream, long length)
    {
        Span<byte> header = stackalloc byte[8]; stream.ReadExactly(header);
        if (!header[..4].SequenceEqual("FORM"u8) || length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) != length - 8)
            throw new InvalidDataException("Downloaded data.win has an invalid FORM header/length. Retry or choose a complete legacy VM archive.");
    }
}
