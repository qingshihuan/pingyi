using System.Diagnostics;
using System.Globalization;

namespace PingYi.Core;

/// <summary>Operation-scoped, metadata-only diagnostics. Never accepts image data or exception text.</summary>
public sealed class CaptureTiming
{
    private static readonly AsyncLocal<CaptureTiming?> Ambient = new();
    private static long _nextId;
    private long _lastMark;
    private int _displayCount, _width, _height, _pngBytes;
    private readonly bool _consoleEnabled = Environment.GetEnvironmentVariable("PINGYI_CAPTURE_TIMING") == "1";
    public static CaptureTiming? Current => Ambient.Value;
    public long Id { get; } = Interlocked.Increment(ref _nextId);
    public long StartedAt { get; } = Stopwatch.GetTimestamp();
    public event Action<CaptureTimingSample>? Sampled;

    public CaptureTiming() => _lastMark = StartedAt;

    public IDisposable Enter()
    {
        var previous = Ambient.Value;
        Ambient.Value = this;
        return new Scope(() => Ambient.Value = previous);
    }

    public void SetDisplays(int count) => Volatile.Write(ref _displayCount, count);
    public void SetPngBytes(int count) => Volatile.Write(ref _pngBytes, count);
    public void SetFrame(int width, int height, int pngBytes)
    {
        Volatile.Write(ref _width, width);
        Volatile.Write(ref _height, height);
        Volatile.Write(ref _pngBytes, pngBytes);
    }

    public void Mark(string stage) => Record(stage, Interlocked.Exchange(ref _lastMark, Stopwatch.GetTimestamp()));
    public IDisposable Measure(string stage)
    {
        var start = Stopwatch.GetTimestamp();
        return new Scope(() => Record(stage, start));
    }

    public void Record(string stage, long start)
    {
        var now = Stopwatch.GetTimestamp();
        var sample = new CaptureTimingSample(Id, stage, Stopwatch.GetElapsedTime(start, now).TotalMilliseconds,
            Stopwatch.GetElapsedTime(StartedAt, now).TotalMilliseconds,
            Volatile.Read(ref _displayCount), Volatile.Read(ref _width), Volatile.Read(ref _height), Volatile.Read(ref _pngBytes));
        var message = string.Create(CultureInfo.InvariantCulture,
            $"[PingYi.CaptureTiming] id={sample.Id} stage={stage} duration_ms={sample.DurationMs:F3} total_ms={sample.TotalMs:F3} displays={sample.Displays} width={sample.Width} height={sample.Height} png_bytes={sample.PngBytes}");
        Trace.WriteLine(message);
        if (_consoleEnabled) Console.Error.WriteLine(message);
        Sampled?.Invoke(sample);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

public sealed record CaptureTimingSample(long Id, string Stage, double DurationMs, double TotalMs,
    int Displays, int Width, int Height, int PngBytes);
