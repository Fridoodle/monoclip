using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
namespace MonoClip.Core;

// Serves exactly one video on loopback. The tunnel connects from this PC, so no inbound
// firewall rule, port forwarding or admin rights are needed, and the LAN never sees the port.
public sealed class ClipShareServer : IDisposable
{
    const int MaxHeaderBytes = 8192;
    static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(15);
    readonly string file; readonly byte[] path; volatile bool ready = true;
    readonly TcpListener listener = new(IPAddress.Loopback, 0); readonly CancellationTokenSource stop = new();
    readonly SemaphoreSlim slots; readonly HashSet<TcpClient> clients = []; readonly int cacheSeconds; long bytesSent; bool disposed;
    public string RequestPath { get; }
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public long BytesSent => Interlocked.Read(ref bytesSent);
    // False while the file is still being written: requests get 503 so the tunnel can start in parallel.
    public bool Ready { get => ready; set => ready = value; }
    public ClipShareServer(string file, string requestPath, int maxConnections = SharePolicy.MaxConnections, int cacheSeconds = SharePolicy.DefaultMinutes * 60)
    {
        this.cacheSeconds = cacheSeconds; this.file = file; RequestPath = requestPath; path = Encoding.UTF8.GetBytes(requestPath); slots = new(maxConnections, maxConnections);
    }
    public void Start() { listener.Start(); _ = AcceptAsync(); }
    async Task AcceptAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop.Token); } catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { return; }
            _ = ServeAsync(client);
        }
    }
    async Task ServeAsync(TcpClient client)
    {
        lock (clients) { if (disposed) { client.Dispose(); return; } clients.Add(client); }
        bool slot = slots.Wait(0);
        try
        {
            client.NoDelay = true; var stream = client.GetStream();
            var buffer = new byte[MaxHeaderBytes]; int filled = 0;
            while (true)
            {
                int end;
                using (var idle = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
                {
                    idle.CancelAfter(IdleTimeout);
                    while ((end = buffer.AsSpan(0, filled).IndexOf("\r\n\r\n"u8)) < 0)
                    {
                        if (filled == buffer.Length) { await WriteHead(stream, "431 Request Header Fields Too Large", [], 0, false); return; }
                        int n = await stream.ReadAsync(buffer.AsMemory(filled), idle.Token); if (n == 0) return; filled += n;
                    }
                }
                var head = Encoding.ASCII.GetString(buffer, 0, end); filled -= end + 4; Buffer.BlockCopy(buffer, end + 4, buffer, 0, filled);
                // Protect the home upload: extra viewers retry later instead of splitting the bandwidth further.
                // Answer only after reading the request; closing with unread data resets the connection.
                if (!slot) { await WriteHead(stream, "503 Service Unavailable", [("Retry-After", "5")], 0, false); return; }
                if (!await Respond(stream, head)) return;
            }
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        finally { if (slot) slots.Release(); lock (clients) clients.Remove(client); client.Dispose(); }
    }
    // Returns whether the connection stays open for another request.
    async Task<bool> Respond(NetworkStream stream, string head)
    {
        var lines = head.Split("\r\n"); var request = lines[0].Split(' ');
        if (request.Length != 3 || !request[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) { await WriteHead(stream, "400 Bad Request", [], 0, false); return false; }
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1)) { int colon = line.IndexOf(':'); if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim(); }
        bool keepAlive = request[2] == "HTTP/1.1" && !string.Equals(headers.GetValueOrDefault("Connection"), "close", StringComparison.OrdinalIgnoreCase);
        // Requests with a body are never valid here; refusing them keeps the stream in sync.
        if (headers.ContainsKey("Transfer-Encoding") || (headers.TryGetValue("Content-Length", out var body) && body != "0")) { await WriteHead(stream, "400 Bad Request", [], 0, false); return false; }
        if (request[0] is not ("GET" or "HEAD")) { await WriteHead(stream, "405 Method Not Allowed", [("Allow", "GET, HEAD")], 0, false); return false; }
        var target = Encoding.UTF8.GetBytes(request[1].Split('?')[0]);
        if (!CryptographicOperations.FixedTimeEquals(target, path)) { await WriteHead(stream, "404 Not Found", [], 0, keepAlive); return keepAlive; }
        if (!ready || !File.Exists(file)) { await WriteHead(stream, "503 Service Unavailable", [("Retry-After", "1")], 0, keepAlive); return keepAlive; }
        long length = new FileInfo(file).Length;
        var range = ParseRange(headers.GetValueOrDefault("Range"), length, out bool unsatisfiable);
        if (unsatisfiable) { await WriteHead(stream, "416 Range Not Satisfiable", [("Content-Range", $"bytes */{length}")], 0, keepAlive); return keepAlive; }
        var (start, last) = range ?? (0, length - 1); long count = length == 0 ? 0 : last - start + 1;
        List<(string, string)> extra = [("Content-Type", "video/mp4"), ("Accept-Ranges", "bytes"), ("Cache-Control", $"public, max-age={cacheSeconds}"), ("X-Content-Type-Options", "nosniff"), ("Content-Disposition", "inline")];
        if (range != null) extra.Add(("Content-Range", $"bytes {start}-{last}/{length}"));
        await WriteHead(stream, range != null ? "206 Partial Content" : "200 OK", extra, count, keepAlive);
        if (request[0] == "GET" && count > 0) await SendFile(stream, start, count);
        return keepAlive;
    }
    async Task SendFile(NetworkStream stream, long start, long count)
    {
        // FileShare.Delete: stopping the share may remove the file while a viewer still reads.
        using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan) { Position = start };
        var buffer = new byte[1 << 16];
        while (count > 0)
        {
            int n = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)), stop.Token); if (n == 0) throw new IOException("Clip ist kürzer als erwartet.");
            await stream.WriteAsync(buffer.AsMemory(0, n), stop.Token); count -= n; Interlocked.Add(ref bytesSent, n);
        }
    }
    async Task WriteHead(NetworkStream stream, string status, IEnumerable<(string Name, string Value)> headers, long contentLength, bool keepAlive)
    {
        var text = new StringBuilder("HTTP/1.1 ").Append(status).Append("\r\n");
        foreach (var (name, value) in headers) text.Append(name).Append(": ").Append(value).Append("\r\n");
        text.Append("Content-Length: ").Append(contentLength).Append("\r\nConnection: ").Append(keepAlive ? "keep-alive" : "close").Append("\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(text.ToString()), stop.Token);
    }
    // Single byte range per RFC 9110. Malformed or multi-range headers are ignored (full response).
    public static (long Start, long End)? ParseRange(string? header, long length, out bool unsatisfiable)
    {
        unsatisfiable = false;
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || header.Contains(',')) return null;
        var spec = header[6..].Trim(); int dash = spec.IndexOf('-'); if (dash < 0) return null;
        string first = spec[..dash].Trim(), last = spec[(dash + 1)..].Trim();
        if (first.Length == 0)
        {
            if (!long.TryParse(last, System.Globalization.NumberStyles.None, null, out var suffix)) return null;
            if (suffix == 0 || length == 0) { unsatisfiable = true; return null; }
            return (Math.Max(0, length - suffix), length - 1);
        }
        if (!long.TryParse(first, System.Globalization.NumberStyles.None, null, out var start)) return null;
        long end = length - 1;
        if (last.Length > 0) { if (!long.TryParse(last, System.Globalization.NumberStyles.None, null, out end)) return null; if (end < start) return null; }
        if (start >= length) { unsatisfiable = true; return null; }
        return (start, Math.Min(end, length - 1));
    }
    public void Dispose()
    {
        lock (clients) { if (disposed) return; disposed = true; }
        stop.Cancel(); listener.Stop();
        lock (clients) { foreach (var c in clients) c.Dispose(); clients.Clear(); }
    }
}
