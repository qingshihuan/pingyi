using System.Diagnostics;

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
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        Trace.WriteLine($"[PingYi.CaptureTiming] stage={stage} elapsed_ms={elapsed.TotalMilliseconds:F1}");
    }
}
