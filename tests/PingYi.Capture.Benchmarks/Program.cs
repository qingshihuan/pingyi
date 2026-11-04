using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PingYi.App;
using PingYi.Core;
using PingYi.Infrastructure;

// Opt-in only. Captures stay in memory; output is timings and numeric display metadata.
// This exercises the production capture/barrier/overlay path, not physical keyboard delivery.
if (!args.Contains("--desktop"))
{
    Console.WriteLine("Requires --desktop on an unlocked desktop. Never writes images or application configuration.");
    return;
}
var count = args.Length > 1 && int.TryParse(args[^1], out var n) ? Math.Clamp(n, 1, 100) : 12;
AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().SetupWithoutStarting();
using var done = new CancellationTokenSource();
Dispatcher.UIThread.Post(async () =>
{
    try { await RunAsync(count, args.Contains("--prewarm"), args.Contains("--verify")); }
    catch (Exception error)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { error = error.GetType().Name,
            check = (error as DesktopVerificationException)?.Check }));
        Environment.ExitCode = 1;
    }
    finally { done.Cancel(); }
});
Dispatcher.UIThread.MainLoop(done.Token);

static async Task RunAsync(int count, bool prewarm, bool verify)
{
    var shell = new Window { Width = 400, Height = 160, Title = "PingYi capture timing probe" };
    shell.Show();
    try
    {
        var displays = shell.Screens.All.Select(s => new CaptureDisplay(
            new(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height), s.Scaling)).ToArray();
        int composition = 0;
        var dwmResult = OperatingSystem.IsWindows() ? DwmIsCompositionEnabled(out composition) : -1;
        Console.WriteLine(JsonSerializer.Serialize(new { environment = new { os = OperatingSystem.IsWindows() ? "Windows" : "Linux",
            displays = displays.Select(d => new { d.Bounds.Width, d.Bounds.Height, d.Scaling }), dwmResult, composition } }));
        await Task.Delay(500);
        if (prewarm) CaptureOverlayWarmup.Run();
        var service = ScreenCaptureServiceFactory.Create();
        var cropper = new SkiaImageCropper();
        for (int index = 0; index < count; index++)
        {
            var samples = new ConcurrentQueue<CaptureTimingSample>();
            var timing = new CaptureTiming();
            timing.Sampled += samples.Enqueue;
            timing.SetDisplays(displays.Length);
            // Alternate same-thread button entry and a worker-to-UI queued entry.
            // This does not synthesize keys or measure OS WM_HOTKEY delivery.
            if (index % 2 == 0) timing.Mark("ui_trigger");
            else
            {
                var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await Task.Run(() =>
                {
                    timing.Mark("dispatch_trigger");
                    Dispatcher.UIThread.Post(() => dispatched.TrySetResult());
                });
                await dispatched.Task;
            }
            using var scope = timing.Enter();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await CaptureWindowScope.RunAsync(new[] { shell }, DesktopCaptureBarrier.WaitAsync,
                token => CaptureSelectionRouter.SelectAsync(service, async (frame, ct) =>
                {
                    timing.Mark("desktop_frame_ready");
                    var overlay = new CaptureOverlaySession(frame, displays, cropper, timing.StartedAt);
                    var selection = overlay.ShowAndSelectAsync(ct);
                    await overlay.FirstFrame.Task.WaitAsync(ct);
                    // Give the compositor a chance to present before ending this sample.
                    await Task.Delay(35, ct);
                    overlay.Cancel();
                    await selection;
                    return null;
                }, token), timeout.Token, captureStartedAt: timing.StartedAt);
            Console.WriteLine(JsonSerializer.Serialize(new { sample = index + 1, shellRestored = shell.IsVisible, stages = samples.ToArray() }));
            await Task.Delay(200);
        }
        if (verify && OperatingSystem.IsWindows()) await DesktopVerification.RunAsync(shell);
    }
    finally { shell.Close(); }
}

[DllImport("dwmapi.dll")]
static extern int DwmIsCompositionEnabled(out int enabled);
