using MonoClip.Core;
using System.Net;
using System.Net.Sockets;
namespace MonoClip.Windows.Share;

// One clip at a time, reachable for the chosen duration, then the tunnel, server and MP4 copy are removed.
public sealed class ClipShare : IClipSharer
{
    static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonoClip", "share");
    readonly System.Windows.Forms.Timer expiry = new(); readonly SynchronizationContext ui;
    ClipShareServer? server; CloudflaredTunnel? tunnel; string? copy; CancellationTokenSource? starting;
    public ShareInfo? Current { get; private set; }
    public bool IsStarting => starting != null;
    public bool FirstUse => !File.Exists(CloudflaredTunnel.ExePath);
    public event EventHandler? Changed;
    public ClipShare()
    {
        ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        expiry.Tick += (_, _) => Stop();
        // Copies from a crashed session are never served again.
        try { if (Directory.Exists(Root)) foreach (var f in Directory.EnumerateFiles(Root)) TryDelete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    public async Task<ShareInfo> ShareAsync(string clipPath, TimeSpan duration, IProgress<string>? progress = null)
    {
        if (duration < TimeSpan.FromMinutes(SharePolicy.MinMinutes) || duration > TimeSpan.FromMinutes(SharePolicy.MaxMinutes)) throw new ArgumentOutOfRangeException(nameof(duration));
        if (!File.Exists(clipPath)) throw new FileNotFoundException("Clip nicht gefunden.", clipPath);
        Stop(); var cancel = new CancellationTokenSource(); starting = cancel; Changed?.Invoke(this, EventArgs.Empty);
        var token = SharePolicy.NewToken(); var target = Path.Combine(Root, token + ".mp4");
        try
        {
            Directory.CreateDirectory(Root); copy = target;
            progress?.Report("Clip wird für Browser und Discord vorbereitet …");
            await Task.Run(() => ClipRemux.ToFastStartMp4(clipPath, target), cancel.Token); cancel.Token.ThrowIfCancellationRequested();
            var local = server = new ClipShareServer(target, SharePolicy.RequestPath(token, clipPath), cacheSeconds: (int)duration.TotalSeconds); local.Start();
            var t = tunnel = await CloudflaredTunnel.StartAsync(local.Port, progress, cancel.Token);
            t.Exited += (_, _) => ui.Post(_ => { if (ReferenceEquals(tunnel, t)) Stop(); }, null);
            var info = new ShareInfo(t.PublicUrl + local.RequestPath, DateTimeOffset.Now + duration, clipPath);
            progress?.Report("Link wird geprüft …");
            await WaitReachableAsync(info.Url, cancel.Token);
            // The public DNS record follows the registration by about 1.5 s. Asking too early would make
            // resolvers (including Windows on this PC) cache "does not exist" for a minute.
            var settle = t.RegisteredAt.AddSeconds(3) - DateTime.UtcNow; if (settle > TimeSpan.Zero) await Task.Delay(settle, cancel.Token);
            // The window counts from the moment the link is handed out, not from the start of preparation.
            info = info with { ExpiresAt = DateTimeOffset.Now + duration };
            Current = info; expiry.Interval = (int)duration.TotalMilliseconds; expiry.Start();
            return info;
        }
        // A cancel during remux can leave the copy behind after Stop() already ran.
        catch { if (ReferenceEquals(starting, cancel)) Stop(); else TryDelete(target); throw; }
        finally { if (ReferenceEquals(starting, cancel)) starting = null; cancel.Dispose(); Changed?.Invoke(this, EventArgs.Empty); }
    }
    // Checks the route through Cloudflare without looking up the brand-new host name: connect to the
    // edge addresses of trycloudflare.com and let TLS/Host select the tunnel. A new host reaches all
    // Cloudflare servers within seconds, so IPv4 and IPv6 must each answer four times in a row.
    // A dead link is never handed out.
    static async Task WaitReachableAsync(string url, CancellationToken cancel)
    {
        IPAddress[] edge;
        try { edge = await Dns.GetHostAddressesAsync("trycloudflare.com", cancel); } catch (SocketException) { return; }
        var families = edge.GroupBy(a => a.AddressFamily).Select(g => new Family(g.ToArray())).ToList();
        if (families.Count == 0) return;
        string last = "keine Antwort"; var until = DateTime.UtcNow.AddSeconds(40);
        while (DateTime.UtcNow < until)
        {
            foreach (var f in families.Where(f => f.Usable && f.Streak < RequiredStreak))
            {
                var (ok, responded, detail) = await ProbeAsync(f.Addresses, url, cancel);
                f.Streak = ok ? f.Streak + 1 : 0; if (responded) f.Responded = true; else f.ConnectFailures++;
                if (!ok) last = detail;
            }
            if (!families.Any(f => f.Usable)) return; // Neither family can reach Cloudflare from here; nothing to verify.
            if (families.All(f => !f.Usable || f.Streak >= RequiredStreak)) return;
            await Task.Delay(1000, cancel);
        }
        throw new InvalidOperationException($"Cloudflare leitet den Link noch nicht weiter ({last}). Bitte in einer Minute erneut teilen.");
    }
    const int RequiredStreak = 4;
    sealed class Family(IPAddress[] addresses)
    {
        public IPAddress[] Addresses { get; } = addresses; public int Streak, ConnectFailures; public bool Responded;
        // A family that never connects (no IPv6 on this network) is not required.
        public bool Usable => Responded || ConnectFailures < 3;
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
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested) { return (false, false, "Zeitüberschreitung"); }
    }
    public void Stop()
    {
        bool changed = Current != null || starting != null;
        starting?.Cancel(); starting = null; expiry.Stop();
        tunnel?.Dispose(); tunnel = null; server?.Dispose(); server = null;
        if (copy != null) { TryDelete(copy); copy = null; }
        Current = null;
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }
    static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    public void Dispose() { Stop(); expiry.Dispose(); }
}
