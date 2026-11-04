using System.Diagnostics;
using PingYi.Core;

namespace PingYi.App;

/// <summary>
/// Emits timing-only capture diagnostics. Never records screenshots, image bytes,
/// window titles, file paths, prompts, or credentials.
/// </summary>
internal static class CapturePerformance
{
    public static long Start() => Stopwatch.GetTimestamp();

    public static void Log(string stage, long startedAt)
    {
        if (CaptureTiming.Current is { } timing) { timing.Mark(stage); return; }
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        Trace.WriteLine($"[PingYi.CaptureTiming] stage={stage} elapsed_ms={elapsed.TotalMilliseconds:F1}");
    }

    public static void LogDuration(string stage, long startedAt)
    {
        if (CaptureTiming.Current is { } timing) { timing.Record(stage, startedAt); return; }
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        Trace.WriteLine($"[PingYi.CaptureTiming] duration={stage} elapsed_ms={elapsed.TotalMilliseconds:F1}");
    }
}
