using PingYi.Core;
using PingYi.Infrastructure;
using SkiaSharp;

namespace PingYi.Core.Tests;

public sealed class InfrastructureSmokeTests
{
    [Fact]
    public void GlobalHotkeyGesture_ParsesCtrlAltD()
    {
        var gesture = GlobalHotkeyGesture.Parse("Ctrl+Alt+D");

        Assert.True(gesture.Control);
        Assert.True(gesture.Alt);
        Assert.False(gesture.Shift);
        Assert.Equal('D', gesture.Key);
    }

    [Fact]
    [Trait("Category", "WindowsHotkey")]
    public async Task WindowsGlobalHotkey_RegistersCtrlAltDWhenEnabled()
    {
        if (!OperatingSystem.IsWindows() ||
            !string.Equals(Environment.GetEnvironmentVariable("PINGYI_RUN_HOTKEY_TESTS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        await using var service = GlobalHotkeyServiceFactory.Create();
        await service.StartAsync("Ctrl+Alt+D");
        await service.StopAsync();
    }

    [Fact]
    public async Task ManagedModelService_DisposeCancelsOperationWaitingForGate()
    {
        var service = new ManagedModelService(new AppDataPaths());
        var gate = Assert.IsType<SemaphoreSlim>(
            typeof(ManagedModelService)
                .GetField("_operationGate", System.Reflection.BindingFlags.Instance |
                                             System.Reflection.BindingFlags.NonPublic)!
                .GetValue(service));
        await gate.WaitAsync();

        var pendingOperation = service.StopAsync();
        var dispose = service.DisposeAsync().AsTask();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingOperation);
        }
        finally
        {
            gate.Release();
        }

        await dispose.WaitAsync(TimeSpan.FromSeconds(2));
        await service.DisposeAsync();
    }

    [Fact]
    public async Task ScreenCaptureAndCrop_ReturnValidInMemoryPng()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var capture = await ScreenCaptureServiceFactory.Create().CaptureDesktopAsync();
        Assert.True(capture.Width > 0);
        Assert.True(capture.Height > 0);
        Assert.True(capture.PngBytes.Length > 8);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, capture.PngBytes[..4]);

        var cropWidth = Math.Min(64, capture.Width);
        var cropHeight = Math.Min(64, capture.Height);
        var cropped = new SkiaImageCropper().Crop(capture, new PixelRect(0, 0, cropWidth, cropHeight));
        Assert.Equal(cropWidth, cropped.Width);
        Assert.Equal(cropHeight, cropped.Height);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, cropped.PngBytes[..4]);
    }

    private static double NormalizedSimilarity(string expected, string actual)
    {
        static string Normalize(string value) => new(
            value.Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());

        var left = Normalize(expected);
        var right = Normalize(actual);
        if (left.Length == 0 && right.Length == 0)
        {
            return 1;
        }

        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var substitution = previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return 1d - (double)previous[right.Length] / Math.Max(left.Length, right.Length);
    }
}
