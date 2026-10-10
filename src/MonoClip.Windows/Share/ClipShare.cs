using MonoClip.Core;
using System.Net;
using System.Net.Sockets;
namespace MonoClip.Windows.Share;

// One clip at a time, reachable for the chosen duration. The MP4 copy is removed when the share ends.
// A tunnel that works is kept for KeepTunnel after the last share and reused for the next clip: new
// quick tunnels take ~6 s and sometimes never get a public DNS record (HTTP 530), more often when many
// are created in a row. Each clip still gets its own random path; the previous one stops working.
public sealed class ClipShare : IClipSharer
{
    const int Attempts = 3;
    static readonly TimeSpan KeepTunnel = TimeSpan.FromMinutes(30);
    static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonoClip", "share");
    readonly System.Windows.Forms.Timer expiry = new(), idle = new(); readonly SynchronizationContext ui;
    ClipShareServer? server; CloudflaredTunnel? tunnel; string? copy; CancellationTokenSource? starting;
    public ShareInfo? Current { get; private set; }
    public bool IsStarting => starting != null;
    public bool FirstUse => !File.Exists(CloudflaredTunnel.ExePath);
    public event EventHandler? Changed;
    public ClipShare()
    {
        ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        expiry.Tick += (_, _) => Stop();
        idle.Tick += (_, _) => { idle.Stop(); if (Current == null && starting == null) CloseTunnel(); };
        // Copies from a crashed session are never served again.
        try { if (Directory.Exists(Root)) foreach (var f in Directory.EnumerateFiles(Root)) TryDelete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    public async Task<ShareInfo> ShareAsync(string clipPath, TimeSpan duration, IProgress<string>? progress = null)
    {
        if (duration < TimeSpan.FromMinutes(SharePolicy.MinMinutes) || duration > TimeSpan.FromMinutes(SharePolicy.MaxMinutes)) throw new ArgumentOutOfRangeException(nameof(duration));
        if (!File.Exists(clipPath)) throw new FileNotFoundException("Clip not found.", clipPath);
        Stop(); idle.Stop(); var cancel = new CancellationTokenSource(); starting = cancel; Changed?.Invoke(this, EventArgs.Empty);
        var token = SharePolicy.NewToken(); var target = Path.Combine(Root, token + ".mp4"); Task remux = Task.CompletedTask;
        try
        {
            Directory.CreateDirectory(Root); copy = target;
            var local = server ??= StartServer();
            local.Publish(target, SharePolicy.RequestPath(token, clipPath), (int)duration.TotalSeconds, ready: false);
            progress?.Report(tunnel != null ? "Preparing clip…" : "Preparing clip · requesting link…");
            // Cloudflare needs about 6 s to assign a new link: prepare the MP4 in the meantime, not before.
            remux = Task.Run(() => ClipRemux.ToFastStartMp4(clipPath, target), cancel.Token);
            CloudflaredTunnel t;
            for (int attempt = 1; ; attempt++)
            {
                bool fresh = tunnel == null;
                t = tunnel ??= await OpenTunnelAsync(local, progress, cancel.Token);
                await remux; local.Ready = true; cancel.Token.ThrowIfCancellationRequested();
                // Never ask for a brand-new host before Cloudflare has set it up (~1.5 s after registering):
                // an early request makes the edge cache "no such tunnel" and answer 530 for up to 10+ s.
                // It would also make DNS resolvers, including this PC's, cache "does not exist".
                if (fresh) { var settle = t.RegisteredAt + TimeSpan.FromSeconds(2.5) - DateTime.UtcNow; if (settle > TimeSpan.Zero) await Task.Delay(settle, cancel.Token); }
                progress?.Report("Checking link…");
                var problem = await ProbeUntilReachableAsync(t.PublicUrl + local.RequestPath, TimeSpan.FromSeconds(6), cancel.Token);
                if (problem == null) break;
                CloseTunnel(keepServer: true);
                if (attempt == Attempts) throw new InvalidOperationException($"Cloudflare did not provide a working link ({problem}). Try again in a few minutes.");
                progress?.Report("No response · requesting a new link…");
                await Task.Delay(TimeSpan.FromSeconds(2), cancel.Token);
            }
            // The window counts from the moment the link is handed out, not from the start of preparation.
            var info = new ShareInfo(t.PublicUrl + local.RequestPath, DateTimeOffset.Now + duration, clipPath);
            Current = info; expiry.Interval = (int)duration.TotalMilliseconds; expiry.Start();
            return info;
        }
        catch
        {
            // Let a running remux finish first, otherwise it could recreate the copy after cleanup.
            try { await remux; } catch { }
            if (ReferenceEquals(starting, cancel)) Stop(); else TryDelete(target);
            throw;
        }
        finally { if (ReferenceEquals(starting, cancel)) starting = null; cancel.Dispose(); Changed?.Invoke(this, EventArgs.Empty); }
    }
    static ClipShareServer StartServer() { var s = new ClipShareServer(); s.Start(); return s; }
    async Task<CloudflaredTunnel> OpenTunnelAsync(ClipShareServer local, IProgress<string>? progress, CancellationToken cancel)
    {
        var t = await CloudflaredTunnel.StartAsync(local.Port, progress, cancel);
        // A tunnel that dies ends the current share; the next share opens a new one.
        t.Exited += (_, _) => ui.Post(_ => { if (!ReferenceEquals(tunnel, t)) return; CloseTunnel(keepServer: true); if (Current != null) Stop(); }, null);
        return t;
    }
    void CloseTunnel(bool keepServer = false)
    {
        var t = tunnel; tunnel = null; t?.Dispose();
        if (!keepServer) { server?.Dispose(); server = null; }
    }
    // Checks the route through Cloudflare without looking up the brand-new host name: connect to the
    // edge addresses of trycloudflare.com and let TLS/Host select the tunnel. IPv4 and IPv6 are probed
    // in parallel and must each answer twice in a row. Returns null when reachable, else the last problem.
    static async Task<string?> ProbeUntilReachableAsync(string url, TimeSpan limit, CancellationToken cancel)
    {
        IPAddress[] edge;
        try { edge = await Dns.GetHostAddressesAsync("trycloudflare.com", cancel); } catch (SocketException) { return null; }
        var families = edge.GroupBy(a => a.AddressFamily).Select(g => g.ToArray()).ToList();
        if (families.Count == 0) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel); deadline.CancelAfter(limit);
        var results = await Task.WhenAll(families.Select(f => FamilyAsync(f, url, deadline.Token)));
        cancel.ThrowIfCancellationRequested();
        // A family that never connects (no IPv6 on this network) is not required.
        if (results.All(r => !r.Usable)) return null;
        return results.FirstOrDefault(r => r.Usable && !r.Ok).Detail;
    }
    static async Task<(bool Ok, bool Usable, string? Detail)> FamilyAsync(IPAddress[] addresses, string url, CancellationToken token)
    {
        int streak = 0, connectFailures = 0; bool responded = false; string last = "no response";
        try
        {
            while (true)
            {
                var (ok, answered, detail) = await ProbeAsync(addresses, url, token);
                if (answered) responded = true; else if (++connectFailures >= 3 && !responded) return (false, false, detail);
                streak = ok ? streak + 1 : 0; if (!ok) last = detail;
                if (streak >= 2) return (true, true, null);
                await Task.Delay(250, token);
            }
        }
        catch (OperationCanceledException) { return (false, responded || connectFailures < 3, last); }
    }
    static async Task<(bool Ok, bool Responded, string Detail)> ProbeAsync(IPAddress[] addresses, string url, CancellationToken cancel)
    {
        using var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.Zero, ConnectTimeout = TimeSpan.FromSeconds(4),
            ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(addresses[0].AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try { await socket.ConnectAsync(addresses, 443, token); return new NetworkStream(socket, true); } catch { socket.Dispose(); throw; }
            }
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
        try { using var r = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), cancel); return (r.IsSuccessStatusCode, true, "HTTP " + (int)r.StatusCode); }
        catch (HttpRequestException e) { return (false, false, e.Message); }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested) { return (false, false, "timeout"); }
    }
    // Ends the current share: its path stops working and the copy is deleted. The tunnel stays for reuse.
    public void Stop()
    {
        bool changed = Current != null || starting != null;
        starting?.Cancel(); starting = null; expiry.Stop();
        server?.Unpublish();
        if (copy != null) { TryDelete(copy); copy = null; }
        Current = null;
        if (tunnel != null) { idle.Interval = (int)KeepTunnel.TotalMilliseconds; idle.Start(); }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }
    static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    public void Dispose() { Stop(); CloseTunnel(); expiry.Dispose(); idle.Dispose(); }
}
