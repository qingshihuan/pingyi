using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using PingYi.Core;
using PingYi.Infrastructure;
using Xunit;

namespace PingYi.App.Tests;

public sealed class CaptureCoordinatorCancellationTests
{
    [AvaloniaFact]
    public async Task Rapid_triggers_cancel_the_previous_worker_and_shutdown_drains_the_operation_gate()
    {
        var priorConfig = Environment.GetEnvironmentVariable("PINGYI_CONFIG_DIR");
        var priorModels = Environment.GetEnvironmentVariable("PINGYI_MODEL_DIR");
        var root = Path.Combine(Path.GetTempPath(), "pingyi-capture-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("PINGYI_CONFIG_DIR", root);
        Environment.SetEnvironmentVariable("PINGYI_MODEL_DIR", Path.Combine(root, "models"));
        var home = new MainWindow();
        try
        {
            var worker = new BlockingCapture();
            var store = new NoStorage();
            await using var services = new AppServices(new AppDataPaths(), store, store, new AppSettings(),
                new NoHotkey(), worker, new SkiaImageCropper(), new HttpClient());
            await using var coordinator = new CaptureCoordinator(services);
            home.Show();
            var pending = new List<Task> { coordinator.StartCaptureAsync(home) };
            await worker.First.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (int i = 0; i < 8; i++) pending.Add(coordinator.StartCaptureAsync(home));
            await worker.Second.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, worker.MaximumActive);
            Assert.Equal(0, worker.Active);
            Assert.False(coordinator.IsCapturingScreen);
            // Shutdown must not resurrect the main window after capture cancellation.
            Assert.False(home.IsVisible);
        }
        finally
        {
            home.Close(); Dispatcher.UIThread.RunJobs();
            Environment.SetEnvironmentVariable("PINGYI_CONFIG_DIR", priorConfig);
            Environment.SetEnvironmentVariable("PINGYI_MODEL_DIR", priorModels);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class BlockingCapture : IScreenCaptureService
    {
        public readonly TaskCompletionSource First = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Second = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Active, MaximumActive;
        private int _calls;
        public async Task<ImageFrame> CaptureDesktopAsync(CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref Active);
            MaximumActive = Math.Max(MaximumActive, active);
            if (Interlocked.Increment(ref _calls) == 1) First.TrySetResult(); else Second.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); throw new InvalidOperationException(); }
            finally { Interlocked.Decrement(ref Active); }
        }
    }
    private sealed class NoStorage : ISettingsStore, ISecretStore
    {
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected storage access");
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected storage access");
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected secret access");
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected secret access");
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected secret access");
    }
    private sealed class NoHotkey : IGlobalHotkeyService
    {
        public event EventHandler? Pressed { add { } remove { } }
        public Task StartAsync(string shortcut, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
