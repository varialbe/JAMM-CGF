using System.Buffers.Binary;
using System.IO.Compression;
using Jamm.Core.Management;

namespace Jamm.Core.Browsing;

/// <summary>Streams to guarded manager staging, validates content and removes incomplete transfers.</summary>
public sealed class ModDownloadService(ManagerStore store, ModHttp http, long maximumBytes = 512L * 1048576)
{
    /// <summary>Maximum silence allowed between body reads; configurable for deterministic tests.</summary>
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Delay before retrying; subsequent retries use exponential backoff.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    /// <summary>Downloads a fixed public URL with bounded retries.</summary>
    public Task<string> DownloadAsync(Uri uri, IProgress<ModDownloadProgress>? progress, CancellationToken cancellationToken)
        => DownloadResolvedAsync(_ => Task.FromResult(uri), progress, cancellationToken);

    /// <summary>Resolves fresh signed URLs on each attempt, resuming only validated HTTP ranges.</summary>
    public async Task<string> DownloadResolvedAsync(Func<CancellationToken, Task<Uri>> resolve, IProgress<ModDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(store.Path("downloads"));
        string path = store.Path("downloads", Guid.NewGuid().ToString("N") + ".part");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        string? validator = null; bool resumable = false; long? expectedTotal = null;
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using var phase = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                    phase.CancelAfter(TimeSpan.FromSeconds(45));
                    var uri = await resolve(phase.Token);
                    long offset = resumable && File.Exists(path) ? new FileInfo(path).Length : 0;
                    using var response = await http.SendRangeAsync(uri, phase.Token, rangeStart: offset, ifRange: validator);
                    phase.CancelAfter(Timeout.InfiniteTimeSpan);
                    long? length = response.Content.Headers.ContentLength;
                    string? responseValidator = response.Headers.ETag is { IsWeak: false } tag ? tag.ToString() : response.Content.Headers.LastModified?.ToString("R");
                    if (response.StatusCode == System.Net.HttpStatusCode.PartialContent)
                    {
                        var range = response.Content.Headers.ContentRange;
                        if (offset == 0 || range?.From != offset || range.To is null || range.Length is null
                            || range.To < range.From || range.To >= range.Length || length != range.To - range.From + 1
                            || expectedTotal is not null && range.Length != expectedTotal)
                            throw new InvalidDataException("The source returned an invalid resume range. Open source page and choose a complete file.");
                        if (validator != responseValidator)
                        {
                            resumable = false;
                            throw new IOException("The source file changed during download. Restarting with a fresh copy.");
                        }
                        expectedTotal = range.Length;
                    }
                    else { offset = 0; expectedTotal = length; }
                    if (expectedTotal > maximumBytes) throw new InvalidDataException("Download exceeds the archive safety limit. Choose a smaller .win or ZIP.");
                    validator = responseValidator;
                    resumable = response.Headers.AcceptRanges.Contains("bytes") && validator is not null;
                    await using (var output = new FileStream(path, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
                    await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
                    {
                        long received = offset, lastReport = Environment.TickCount64; byte[] buffer = new byte[65536];
                        progress?.Report(new(received, expectedTotal));
                        while (true)
                        {
                            using var stall = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                            stall.CancelAfter(StallTimeout);
                            int count = await input.ReadAsync(buffer, stall.Token);
                            if (count == 0) break;
                            received += count;
                            if (received > maximumBytes || expectedTotal is { } declared && received > declared)
                                throw new InvalidDataException("Source sent more data than declared or allowed. Choose a different release.");
                            await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                            if (Environment.TickCount64 - lastReport >= 100)
                            { progress?.Report(new(received, expectedTotal)); lastReport = Environment.TickCount64; }
                        }
                        progress?.Report(new(received, expectedTotal));
                        if (expectedTotal is { } total && received != total) throw new IOException("Download was truncated. Retrying the incomplete transfer.");
                    }
                    string extension = await Task.Run(() => Validate(path, maximumBytes, timeout.Token), timeout.Token);
                    string complete = Path.ChangeExtension(path, extension);
                    timeout.Token.ThrowIfCancellationRequested(); File.Move(path, complete); return complete;
                }
                catch (Exception ex) when (attempt < 3 && !timeout.IsCancellationRequested && Retryable(ex))
                {
                    if (ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.RequestedRangeNotSatisfiable }) resumable = false;
                    await Task.Delay(TimeSpan.FromMilliseconds(RetryDelay.TotalMilliseconds * (1 << attempt)), timeout.Token);
                }
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("Download timed out or stopped responding. Check your connection and Retry; the partial file was removed.", ex); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    private static bool Retryable(Exception ex) => ex switch
    {
        HttpRequestException h => !h.Data.Contains("JammPermanent") && (h.StatusCode is null || (int)h.StatusCode >= 500 || (int)h.StatusCode is 401 or 403 or 408 or 429 or 416),
        OperationCanceledException or TimeoutException => true,
        IOException => true,
        _ => false
    };
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
