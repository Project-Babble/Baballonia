using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Baballonia.Services;

/// <summary>
/// CPU usage attributed to a thread <em>name</em> over the recent window, as a percentage of one core.
/// Threads that share a name (notably the .NET thread pool) are aggregated: <see cref="CpuPercent"/> is
/// their combined usage and <see cref="Count"/> is how many threads were folded in.
/// </summary>
public readonly record struct ThreadCpuSample(string Name, double CpuPercent, int Count);

/// <summary>
/// A lightweight, always-on sampling thread profiler. A dedicated background thread periodically reads
/// per-OS-thread CPU time from <see cref="Process.GetCurrentProcess"/> and derives each thread's CPU%
/// (of one core) over the interval, so the Debug page can show where compute is going.
///
/// It is registered as an <see cref="IHostedService"/> so it starts with the host and samples for the
/// whole app lifetime — independent of whether the Debug page is open. (If it were driven by the Debug
/// view-model we would only ever profile the Debug page itself.)
///
/// Thread names come from <see cref="Register"/> (called by the threads we own, for cross-platform
/// labels) and, on Linux, from <c>/proc/self/task/&lt;tid&gt;/comm</c> for everything else (runtime,
/// thread-pool and native/ONNX threads). Unlabelled threads fall back to <c>tid:N</c>.
/// </summary>
public sealed class ThreadProfiler : IHostedService, IDisposable
{
    private readonly ILogger<ThreadProfiler> _logger;
    private readonly ConcurrentDictionary<int, string> _names = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _sampler;
    private readonly int _intervalMs;

    // How strongly each new (10 ms-quantised) sample pulls the smoothed value. ~0.35 keeps the display
    // responsive while averaging the quantisation away over a few seconds.
    private const double Alpha = 0.35;

    // Touched only by the sampler thread.
    private Dictionary<int, TimeSpan> _prevCpu = new();
    private Dictionary<string, double> _ewma = new();
    private double _processEwma;
    private DateTime _prevWall;

    private volatile IReadOnlyList<ThreadCpuSample> _snapshot = Array.Empty<ThreadCpuSample>();
    private double _processCpuPercent;
    private bool _disposed;

    /// <summary>Latest per-thread CPU samples, sorted hottest-first. Safe to read from any thread.</summary>
    public IReadOnlyList<ThreadCpuSample> Snapshot => _snapshot;

    /// <summary>Sum of all threads' CPU% over the last interval (i.e. process CPU as % of one core).</summary>
    public double ProcessCpuPercent => Volatile.Read(ref _processCpuPercent);

    /// <summary>Logical processor count, for normalising <see cref="ProcessCpuPercent"/> against total capacity.</summary>
    public int ProcessorCount { get; } = Environment.ProcessorCount;

    public ThreadProfiler(ILogger<ThreadProfiler> logger, int intervalMs = 1000)
    {
        _logger = logger;
        _intervalMs = intervalMs;
        _sampler = new Thread(SamplerLoop) { IsBackground = true, Name = "ThreadProfiler" };
    }

    /// <summary>
    /// Label the calling thread in the profiler output. Call this from <em>inside</em> the thread you
    /// want named (it captures the current OS thread id). Names persist for the process lifetime.
    /// </summary>
    public void Register(string name)
    {
        var tid = CurrentOsThreadId();
        if (tid > 0)
            _names[tid] = name;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Per-OS-thread CPU time is read via Process.Threads, a desktop-only capability — on mobile it
        // throws every interval and there's no Debug page to surface it anyway. The singleton still
        // resolves everywhere (ProcessingLoopService depends on it for Register()); we just don't spin
        // up the sampler off-desktop. (On macOS Process.Threads may report no CPU time, in which case
        // it degrades to an empty table rather than failing.)
        if (Utils.IsSupportedDesktopOS)
            _sampler.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    private void SamplerLoop()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                SampleOnce();
            }
            catch (Exception ex)
            {
                // Per-platform Process.Threads quirks shouldn't take the profiler (or app) down.
                _logger.LogDebug("Thread profiler sample failed: {Ex}", ex);
            }

            // Cancellation-aware pace; WaitOne returns true when signalled (shutdown).
            if (token.WaitHandle.WaitOne(_intervalMs))
                break;
        }
    }

    private void SampleOnce()
    {
        using var proc = Process.GetCurrentProcess();
        var now = DateTime.UtcNow;

        var cur = new Dictionary<int, TimeSpan>(proc.Threads.Count);
        foreach (ProcessThread t in proc.Threads)
        {
            try { cur[t.Id] = t.TotalProcessorTime; }
            catch { /* thread exited between enumeration and read; skip it */ }
        }

        var dt = (now - _prevWall).TotalSeconds;
        if (_prevWall != default && dt > 0)
        {
            // Raw CPU% this interval, aggregated by thread name so the pool collapses into one row.
            var raw = new Dictionary<string, (double pct, int count)>();
            double total = 0;
            foreach (var (id, cpu) in cur)
            {
                if (!_prevCpu.TryGetValue(id, out var prev))
                    continue; // new thread this interval — no baseline to diff against yet

                var pct = (cpu - prev).TotalSeconds / dt * 100.0;
                if (pct < 0) pct = 0; // clamp clock jitter / counter resets
                total += pct;

                var name = ResolveName(id);
                var agg = raw.GetValueOrDefault(name);
                raw[name] = (agg.pct + pct, agg.count + 1);
            }

            // EWMA each name (rebuilt every sample, so names that vanish drop out). This recovers
            // sub-1% detail that a single 10 ms-quantised interval can't express on its own.
            var nextEwma = new Dictionary<string, double>(raw.Count);
            var list = new List<ThreadCpuSample>(raw.Count);
            foreach (var (name, agg) in raw)
            {
                var smoothed = _ewma.TryGetValue(name, out var prev)
                    ? prev + Alpha * (agg.pct - prev)
                    : agg.pct;
                nextEwma[name] = smoothed;
                list.Add(new ThreadCpuSample(name, smoothed, agg.count));
            }
            _ewma = nextEwma;

            list.Sort(static (a, b) => b.CpuPercent.CompareTo(a.CpuPercent));
            _snapshot = list;

            _processEwma = _processEwma <= 0 ? total : _processEwma + Alpha * (total - _processEwma);
            Volatile.Write(ref _processCpuPercent, _processEwma);
        }

        _prevCpu = cur;
        _prevWall = now;
    }

    private string ResolveName(int id)
    {
        if (_names.TryGetValue(id, out var registered))
            return registered;

        if (OperatingSystem.IsLinux())
        {
            try
            {
                var comm = File.ReadAllText($"/proc/self/task/{id}/comm").Trim();
                if (!string.IsNullOrEmpty(comm))
                    return comm;
            }
            catch { /* thread gone, or /proc unavailable */ }
        }

        return $"tid:{id}";
    }

    private static int CurrentOsThreadId()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return (int)GetCurrentThreadId();
            if (OperatingSystem.IsLinux())
                return gettid();
            if (OperatingSystem.IsMacOS() && pthread_threadid_np(IntPtr.Zero, out var mac) == 0)
                return (int)mac;
        }
        catch { /* platform without the export; registration is best-effort */ }

        return -1;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("libc", EntryPoint = "gettid")]
    private static extern int gettid();

    [DllImport("libSystem.dylib", EntryPoint = "pthread_threadid_np")]
    private static extern int pthread_threadid_np(IntPtr thread, out ulong threadId);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        if (_sampler.IsAlive)
            _sampler.Join(TimeSpan.FromSeconds(2));
        _cts.Dispose();
    }
}
