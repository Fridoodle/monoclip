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
    // Measured upload speed in bytes per second (SharePolicy.UpdateUploadEstimate), kept across sessions.
    static readonly string UploadFile = Path.Combine(Path.GetDirectoryName(Root)!, "upload-speed.txt");
    double upload;
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
        try { if (File.Exists(UploadFile) && double.TryParse(File.ReadAllText(UploadFile), System.Globalization.CultureInfo.InvariantCulture, out var known) && known > 0) upload = known; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        // Copies from a crashed session are never served again.
        try { if (Directory.Exists(Root)) foreach (var f in Directory.EnumerateFiles(Root)) TryDelete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    public async Task<ShareInfo> ShareAsync(string clipPath, TimeSpan duration, IProgress<string>? progress = null, ShareOptions? options = null)
    {
        if (duration < TimeSpan.FromMinutes(SharePolicy.MinMinutes) || duration > TimeSpan.FromMinutes(SharePolicy.MaxMinutes)) throw new ArgumentOutOfRangeException(nameof(duration));
        if (!File.Exists(clipPath)) throw new FileNotFoundException("Clip not found.", clipPath);
        Stop(); idle.Stop(); var cancel = new CancellationTokenSource(); starting = cancel; Changed?.Invoke(this, EventArgs.Empty);
        var token = SharePolicy.NewToken(); var target = Path.Combine(Root, token + ".mp4"); Task remux = Task.CompletedTask; Task<Preview>? preview = null;
        try
        {
            Directory.CreateDirectory(Root); copy = target;
            var local = server ??= StartServer();
            local.Publish(target, SharePolicy.RequestPath(token, clipPath), (int)duration.TotalSeconds, ready: false);
            progress?.Report(tunnel != null ? "Preparing clip…" : "Preparing clip · requesting link…");
            // Cloudflare needs about 6 s to assign a new link: prepare the MP4 in the meantime, not before.
            remux = Task.Run(() => ClipRemux.ToFastStartMp4(clipPath, target), cancel.Token);
            // Preview image and video facts for the page, prepared while Cloudflare assigns the link.
            options ??= new(); var title = string.IsNullOrWhiteSpace(options.Title) ? "MonoClip clip" : options.Title;
            var budget = SharePolicy.EmbedBudget(upload);
            preview = options.DirectLink ? null : remux.ContinueWith(_ => PreparePreviewAsync(target, title, budget, cancel.Token), cancel.Token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default).Unwrap();
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
            var url = t.PublicUrl + local.RequestPath;
            if (preview != null)
            {
                progress?.Report("Creating preview…");
                // A failed preview must not fail the share: the direct video link still works.
                try
                {
                    var (mp4, jpeg, imageWidth, imageHeight, embed) = await preview;
                    var page = new SharePageInfo(title, SharePage.Describe(mp4), t.PublicUrl + SharePolicy.PagePath(token), url, t.PublicUrl + SharePolicy.ImagePath(token), mp4?.Width ?? 1920, mp4?.Height ?? 1080, imageWidth, imageHeight,
                        embed != null ? t.PublicUrl + SharePolicy.EmbedPath(token) : null, embed?.Width ?? 0, embed?.Height ?? 0);
                    local.AttachPage(new(SharePolicy.PagePath(token), System.Text.Encoding.UTF8.GetBytes(SharePage.Build(page)), SharePolicy.ImagePath(token), jpeg, SharePolicy.EmbedPath(token), embed != null ? EmbedFile(target) : null));
                    url = page.PageUrl;
                }
                catch (Exception e) when (e is not OperationCanceledException || !cancel.IsCancellationRequested) { }
            }
            var info = new ShareInfo(url, DateTimeOffset.Now + duration, clipPath);
            Current = info; expiry.Interval = (int)duration.TotalMilliseconds; expiry.Start();
            return info;
        }
        catch
        {
            // Let a running remux finish first, otherwise it could recreate the copy after cleanup.
            try { await remux; } catch { }
            try { if (preview != null) await preview; } catch { }
            if (ReferenceEquals(starting, cancel)) Stop(); else { TryDelete(target); TryDelete(EmbedFile(target)); }
            throw;
        }
        finally { if (ReferenceEquals(starting, cancel)) starting = null; cancel.Dispose(); Changed?.Invoke(this, EventArgs.Empty); }
    }
    sealed record Preview(Mp4Info? Info, byte[] Jpeg, int Width, int Height, Mp4Info? Embed);
    // 1280 px wide preview in the clip's aspect ratio. A thumbnail that takes too long falls back to the placeholder.
    // Large clips also get the smaller embed copy; without it (failed, or not needed) chat apps get the original.
    static async Task<Preview> PreparePreviewAsync(string mp4, string title, long budget, CancellationToken cancel)
    {
        Mp4Info? info = null; try { info = Mp4Info.Read(mp4); } catch (IOException) { } catch (InvalidDataException) { }
        var embed = Task.FromResult<Mp4Info?>(null);
        if (info != null && SharePolicy.NeedsEmbedCopy(new FileInfo(mp4).Length, budget))
            embed = Task.Run(() =>
            {
                try { ClipEmbedCopy.Create(mp4, EmbedFile(mp4), info.Duration, budget, cancel); return Mp4Info.Read(EmbedFile(mp4)); }
                catch (Exception e) when (e is not OperationCanceledException) { TryDelete(EmbedFile(mp4)); return null; }
            }, cancel);
        int width = 1280, height = info is { Width: > 0, Height: > 0 } ? Math.Clamp(1280 * info.Height / info.Width, 360, 1280) : 720;
        byte[] jpeg;
        try { jpeg = await ClipThumbnail.CreateJpegAsync(mp4, width, height, title).WaitAsync(TimeSpan.FromSeconds(5)); }
        // Any failure (timeout, shell error) only costs the real frame, never the page.
        catch (Exception e) when (e is not OperationCanceledException) { jpeg = ClipThumbnail.CreateJpeg("", width, height, title); }
        return new(info, jpeg, width, height, await embed);
    }
    static string EmbedFile(string copy) => Path.ChangeExtension(copy, ".embed.mp4");
    ClipShareServer StartServer() { var s = new ClipShareServer { Sent = Measured }; s.Start(); return s; }
    // Runs on a server thread; the estimate is a plain double, a lost update only skips one measurement.
    void Measured(long bytes, TimeSpan elapsed)
    {
        var next = SharePolicy.UpdateUploadEstimate(upload, bytes, elapsed); if (next == upload) return; upload = next;
        try { File.WriteAllText(UploadFile, ((long)next).ToString(System.Globalization.CultureInfo.InvariantCulture)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
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
        if (copy != null) { TryDelete(copy); TryDelete(EmbedFile(copy)); copy = null; }
        Current = null;
        if (tunnel != null) { idle.Interval = (int)KeepTunnel.TotalMilliseconds; idle.Start(); }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }
    static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    public void Dispose() { Stop(); CloseTunnel(); expiry.Dispose(); idle.Dispose(); }
}
