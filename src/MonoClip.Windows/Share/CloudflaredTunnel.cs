using MonoClip.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
namespace MonoClip.Windows.Share;

// Cloudflare Quick Tunnel: an outbound connection only. No account, no port forwarding,
// works behind DS-Lite/CGNAT and keeps the home IP out of the shared link.
internal sealed class CloudflaredTunnel : IDisposable
{
    public const string Version = "2026.9.3";
    const string Sha256 = "f096265ec2fcbe9bb6e2d64268db167ced3fcbb83d894bdb9e2fcdb26f2ea7e2";
    const string Url = "https://github.com/cloudflare/cloudflared/releases/download/" + Version + "/cloudflared-windows-amd64.exe";
    static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(45);
    public static string ExePath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonoClip", "tools", $"cloudflared-{Version}.exe");
    readonly Process process; readonly IntPtr job; readonly Queue<string> log = new(); bool disposed;
    public string PublicUrl { get; private set; } = "";
    public DateTime RegisteredAt { get; private set; }
    public event EventHandler? Exited;

    CloudflaredTunnel(Process process, IntPtr job) { this.process = process; this.job = job; }

    public static async Task<CloudflaredTunnel> StartAsync(int port, IProgress<string>? progress, CancellationToken cancel)
    {
        var exe = await EnsureBinaryAsync(progress, cancel);
        progress?.Report("Tunnel wird aufgebaut …");
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (var arg in new[] { "tunnel", "--no-autoupdate", "--url", $"http://127.0.0.1:{port}" }) info.ArgumentList.Add(arg);
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var job = KillOnCloseJob();
        var tunnel = new CloudflaredTunnel(process, job);
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); string? url = null;
        DataReceivedEventHandler onLine = (_, e) =>
        {
            if (e.Data is not { } line) return;
            lock (tunnel.log) { tunnel.log.Enqueue(line); while (tunnel.log.Count > 20) tunnel.log.Dequeue(); }
            url ??= SharePolicy.FindQuickTunnelUrl(line);
            // The host is assigned before the edge accepts traffic; wait for a registered connection.
            if (url != null && line.Contains("Registered tunnel connection", StringComparison.OrdinalIgnoreCase)) ready.TrySetResult(url);
        };
        process.ErrorDataReceived += onLine; process.OutputDataReceived += onLine;
        process.Exited += (_, _) => { ready.TrySetException(new InvalidOperationException(tunnel.ExitReason())); tunnel.Exited?.Invoke(tunnel, EventArgs.Empty); };
        try
        {
            process.Start();
            // If MonoClip dies, Windows closes the job handle and ends the tunnel with it.
            if (job != IntPtr.Zero) AssignProcessToJobObject(job, process.Handle);
            process.BeginErrorReadLine(); process.BeginOutputReadLine();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel); timeout.CancelAfter(StartTimeout);
            try { tunnel.PublicUrl = await ready.Task.WaitAsync(timeout.Token); tunnel.RegisteredAt = DateTime.UtcNow; }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { throw new TimeoutException("Cloudflare-Tunnel hat nicht rechtzeitig geantwortet. " + tunnel.LastLog()); }
            return tunnel;
        }
        catch { tunnel.Dispose(); throw; }
    }
    string LastLog() { lock (log) return log.Count == 0 ? "" : "Letzte Meldung: " + log.Last(); }
    // Account-less tunnels are rate limited per connection; say so instead of showing a raw log line.
    string ExitReason()
    {
        bool limited; lock (log) limited = log.Any(l => l.Contains("429") || l.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase));
        return limited ? "Cloudflare hat in kurzer Zeit zu viele Links von diesem Anschluss erstellt. Bitte ein paar Minuten warten und erneut teilen." : "cloudflared wurde beendet. " + LastLog();
    }
    public string[] RecentLog { get { lock (log) return log.ToArray(); } }

    static async Task<string> EnsureBinaryAsync(IProgress<string>? progress, CancellationToken cancel)
    {
        if (File.Exists(ExePath) && await HashAsync(ExePath, cancel) == Sha256) return ExePath;
        Directory.CreateDirectory(Path.GetDirectoryName(ExePath)!);
        var partial = ExePath + ".part";
        try
        {
            progress?.Report("cloudflared wird einmalig geladen …");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            using (var response = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? 0, done = 0; int lastPercent = -1;
                await using var source = await response.Content.ReadAsStreamAsync(cancel); await using var file = File.Create(partial);
                var buffer = new byte[1 << 16];
                for (int n; (n = await source.ReadAsync(buffer, cancel)) > 0;)
                {
                    await file.WriteAsync(buffer.AsMemory(0, n), cancel); done += n;
                    int percent = total > 0 ? (int)(done * 100 / total) : -1; if (percent / 10 != lastPercent / 10) { lastPercent = percent; progress?.Report($"cloudflared wird einmalig geladen … {percent} %"); }
                }
            }
            // Pinned official release: anything else is never executed.
            if (await HashAsync(partial, cancel) != Sha256) throw new InvalidDataException("cloudflared-Download hat eine falsche Prüfsumme und wurde verworfen.");
            File.Move(partial, ExePath, true);
            return ExePath;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
    static async Task<string> HashAsync(string path, CancellationToken cancel) { await using var s = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(s, cancel)).ToLowerInvariant(); }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(3000); } } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        process.Dispose(); if (job != IntPtr.Zero) CloseHandle(job);
    }

    [StructLayout(LayoutKind.Sequential)] struct BasicLimit { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] struct ExtendedLimit { public BasicLimit Basic; public IoCounters Io; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
    const int ExtendedLimitInformation = 9; const uint KillOnJobClose = 0x2000;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimit info, int size);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    static IntPtr KillOnCloseJob()
    {
        var job = CreateJobObject(IntPtr.Zero, null); if (job == IntPtr.Zero) return IntPtr.Zero;
        var info = new ExtendedLimit { Basic = new() { LimitFlags = KillOnJobClose } };
        if (!SetInformationJobObject(job, ExtendedLimitInformation, ref info, Marshal.SizeOf<ExtendedLimit>())) { CloseHandle(job); return IntPtr.Zero; }
        return job;
    }
}
