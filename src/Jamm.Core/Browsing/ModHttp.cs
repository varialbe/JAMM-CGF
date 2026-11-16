using System.Net;
using System.Net.Sockets;

namespace Jamm.Core.Browsing;

/// <summary>Bounded public HTTPS transport with explicit redirect checks and DNS pinning at connect time.</summary>
public sealed class ModHttp : IDisposable
{
    private readonly HttpClient client;
    /// <summary>Creates the production transport. Every connected address must be publicly routable.</summary>
    public ModHttp()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            ConnectCallback = async (context, token) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
                if (addresses.Length == 0 || addresses.Any(a => !IsPublicAddress(a)))
                    throw new HttpRequestException("The source resolves to a local/private network. Open a public HTTPS source instead.");
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, token); return new NetworkStream(socket, true); }
                catch { socket.Dispose(); throw; }
            }
        };
        client = new(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }
    /// <summary>Creates a transport over a deterministic test handler; redirects are still checked.</summary>
    public ModHttp(HttpMessageHandler handler) { client = new(handler) { Timeout = Timeout.InfiniteTimeSpan }; }

    /// <summary>Rejects special-use, private and loopback addresses, including mapped IPv4.</summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        byte[] b = address.GetAddressBytes();
        if (b.Length == 16) return b[0] >= 0x20 && b[0] <= 0x3f && !(b[0] == 0x20 && (b[1] == 2 || b[1] == 1 && (b[2] < 2 || b[2] == 0x0d && b[3] == 0xb8))) && !(b[0] == 0x3f && b[1] == 0xff);
        return b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224
            && !(b[0] == 100 && b[1] >= 64 && b[1] <= 127)
            && !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            && !(b[0] == 192 && (b[1] == 168 || b[1] == 0 || b[1] == 2))
            && !(b[0] == 198 && (b[1] == 18 || b[1] == 19 || b[1] == 51 && b[2] == 100))
            && !(b[0] == 203 && b[1] == 0 && b[2] == 113);
    }
    /// <summary>Checks URL scheme, port, credentials and literal/private hostnames before any request.</summary>
    public static Uri PublicUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0 || uri.HostNameType == UriHostNameType.Unknown
            || !uri.Host.Contains('.') && !IPAddress.TryParse(uri.Host.Trim('[', ']'), out _)
            || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && !IsPublicAddress(ip))
            throw new HttpRequestException("This source is not a public HTTPS URL. Use Open source page or choose a public file link.");
        return uri;
    }
    /// <summary>Sends a GET or the public Game Jolt URL lookup POST, without automatic redirects.</summary>
    public async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken token, string? apiKey = null, bool lookupPost = false)
    {
        for (int hop = 0; hop < 6; hop++)
        {
            PublicUri(uri.AbsoluteUri);
            using var request = new HttpRequestMessage(lookupPost ? HttpMethod.Post : HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("JAMM-CGF/1.0");
            if (apiKey is not null) request.Headers.Add("apikey", apiKey);
            if (lookupPost) request.Content = new StringContent("{\"forceDownload\":true}", System.Text.Encoding.UTF8, "application/json");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and <= 399)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) throw new HttpRequestException("The source returned a redirect without a destination. Retry or open its source page.");
                var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (next.Host != uri.Host) apiKey = null;
                uri = PublicUri(next.AbsoluteUri); lookupPost = false; continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode; response.Dispose();
                throw new HttpRequestException($"Source '{uri.Host}' returned HTTP {(int)status}. Retry or open the source page.", null, status);
            }
            return response;
        }
        throw new HttpRequestException("Too many source redirects. Open the source page and choose a direct file.");
    }
    /// <summary>Reads bounded metadata or image bytes with a timeout.</summary>
    public async Task<byte[]> ReadAsync(string url, int limit, CancellationToken token, string? apiKey = null, bool lookupPost = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            using var response = await SendAsync(PublicUri(url), timeout.Token, apiKey, lookupPost);
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Source metadata/image is too large. Open the source page instead.");
            using var output = new MemoryStream();
            await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(timeout.Token), output, limit, null, null, timeout.Token);
            return output.ToArray();
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        { throw new TimeoutException("The source did not respond within 45 seconds. Check your connection and Retry.", ex); }
    }
    internal static async Task CopyBoundedAsync(Stream input, Stream output, long limit, long? total, IProgress<ModDownloadProgress>? progress, CancellationToken token)
    {
        await using (input)
        {
            byte[] buffer = new byte[65536]; long received = 0, lastReport = Environment.TickCount64;
            while (true)
            {
                int count = await input.ReadAsync(buffer, token); if (count == 0) break;
                received += count;
                if (received > limit) throw new InvalidDataException($"Download exceeds the {limit / 1048576} MB safety limit. Choose a smaller .win or ZIP.");
                await output.WriteAsync(buffer.AsMemory(0, count), token);
                if (Environment.TickCount64 - lastReport >= 100)
                {
                    progress?.Report(new(received, total)); lastReport = Environment.TickCount64;
                }
            }
            progress?.Report(new(received, total));
            if (total is { } expected && received != expected) throw new IOException("Download was incomplete. Retry before importing it.");
        }
    }
    /// <summary>Releases HTTP connections.</summary>
    public void Dispose() => client.Dispose();
}
