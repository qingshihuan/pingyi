using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using PingYi.Core;
using Xunit;

namespace PingYi.App.Tests;

public sealed class AutomaticStatusRefreshTests
{
    [AvaloniaFact]
    public async Task Forced_refresh_during_a_read_is_queued_without_concurrent_reads()
    {
        var release = new TaskCompletionSource();
        var forces = new List<bool>();
        var active = 0;
        using var updater = new PassiveStatusUpdater(() => true, async (_, force) =>
        {
            Assert.Equal(1, ++active);
            forces.Add(force);
            if (forces.Count == 1) await release.Task;
            active--;
        });
        var first = updater.RequestAsync();
        var second = updater.RequestAsync(true);
        await Task.Yield();
        Assert.False(second.IsCompleted);
        Assert.Single(forces);
        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(new[] { false, true }, forces);
    }

    [AvaloniaFact]
    public async Task Paused_or_hidden_windows_defer_reads_and_closing_cancels_pending_work()
    {
        var visible = false;
        var calls = 0;
        var cancelled = false;
        using var updater = new PassiveStatusUpdater(() => visible, async (token, force) =>
        {
            calls++;
            Assert.True(force);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { cancelled = true; throw; }
        });
        await updater.RequestAsync(true);
        Assert.Equal(0, calls);
        visible = true;
        var work = updater.RequestAsync();
        updater.Dispose();
        await work;
        Assert.True(cancelled);
        await updater.RequestAsync();
        Assert.Equal(1, calls);
    }

    [AvaloniaFact]
    public async Task Home_updates_after_model_start_without_activation_or_a_refresh_click()
    {
        UiText.Configure("en-US");
        var snapshot = RuntimeStatusUiTests.SyntheticSnapshot();
        snapshot = snapshot with { Current = new(ModeReadinessState.OnDemand, "basic-on-demand"),
            Basic = new(ModeReadinessState.OnDemand, "basic-on-demand") };
        var nextRead = new TaskCompletionSource();
        var home = new MainWindow();
        home.InitializeStatusUpdates(() => snapshot.Settings, (_, _) =>
        {
            if (snapshot.Current.State == ModeReadinessState.Available) nextRead.TrySetResult();
            return Task.FromResult(snapshot);
        }, () => false);
        try
        {
            home.Show();
            await home.RefreshDashboardAsync();
            var cards = home.FindControl<ModeStatusCards>("ModeCards")!;
            Assert.Equal(ModeReadinessState.OnDemand, cards.Snapshot.Current.State);
            snapshot = snapshot with { Current = new(ModeReadinessState.Available, "basic-running"),
                Basic = new(ModeReadinessState.Available, "basic-running"), CheckedAt = DateTimeOffset.UtcNow };
            await nextRead.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ModeReadinessState.Available, cards.Snapshot.Current.State);
            Assert.Equal(ModeReadinessState.Available, cards.Snapshot.Basic.State);
            home.SetGlobalStatus("Synthetic operation result", false);
            snapshot = snapshot with { CheckedAt = DateTimeOffset.UtcNow };
            await home.RefreshDashboardAsync();
            Assert.Equal("Synthetic operation result", home.FindControl<TextBlock>("LiveStatusDetailText")!.Text);
        }
        finally { home.Close(); }
    }

    [AvaloniaFact]
    public async Task Home_discards_an_old_read_when_configuration_changes_during_refresh()
    {
        UiText.Configure("zh-CN");
        var before = RuntimeStatusUiTests.SyntheticSnapshot();
        var next = before with { Settings = before.Settings with { TargetLanguage = "en" },
            Current = new(ModeReadinessState.Verified, "current-tested"), Api = new(ModeReadinessState.Verified, "api-tested") };
        var currentSettings = before.Settings;
        var pending = new TaskCompletionSource<RuntimeStatusSnapshot>();
        var calls = 0;
        var home = new MainWindow();
        home.InitializeStatusUpdates(() => currentSettings, (_, _) => ++calls == 1 ? pending.Task : Task.FromResult(next), () => false);
        try
        {
            home.Show();
            var first = home.RefreshDashboardAsync();
            currentSettings = next.Settings;
            var queued = home.RefreshDashboardAsync(true);
            pending.SetResult(before);
            await Task.WhenAll(first, queued);
            Assert.Equal(next.Settings, home.FindControl<ModeStatusCards>("ModeCards")!.Snapshot.Settings);
            Assert.Equal(ModeReadinessState.Verified, home.FindControl<ModeStatusCards>("ModeCards")!.Snapshot.Current.State);
            Assert.Equal("已验证", home.FindControl<TextBlock>("TopStatusText")!.Text);
            home.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var directory = Environment.GetEnvironmentVariable("PINGYI_UI_ARTIFACTS");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                using var frame = home.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(directory, "status-refresh-verified.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
        }
        finally { home.Close(); }
    }

    [AvaloniaFact]
    public async Task Detail_refresh_preserves_api_drafts_and_verified_is_distinct_from_online_green()
    {
        UiText.Configure("en-US");
        var snapshot = RuntimeStatusUiTests.SyntheticSnapshot() with
        {
            Current = new(ModeReadinessState.Verified, "current-tested"),
            Api = new(ModeReadinessState.Verified, "api-tested")
        };
        var settings = new SettingsWindow();
        settings.InitializeStatusUpdates(() => snapshot.Settings, (_, _) => Task.FromResult(snapshot), () => false);
        try
        {
            settings.Show(); settings.SelectRuntimeStatus();
            settings.ConfigurationCenter.KeyEditor.Text = "synthetic-unsaved-draft";
            await settings.RefreshRuntimeStatusAsync(true);
            Assert.Equal("synthetic-unsaved-draft", settings.ConfigurationCenter.KeyEditor.Text);
            Assert.Equal(snapshot, settings.FindControl<RuntimeStatusView>("RuntimeStatusPage")!.Snapshot);
            settings.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var card = settings.GetLogicalDescendants().OfType<ModeStatusCard>().Distinct().Single(c => c.ModeId == "api");
            Assert.Equal("Verified", card.BadgeText.Text);
            Assert.False(card.State.IsReady);
            Assert.Contains("live connectivity", ModeStatusText.Reason(snapshot.Current));
            Assert.NotEqual(Color.Parse("#2C9A5E"), Assert.IsAssignableFrom<ISolidColorBrush>(card.Indicator.Background).Color);
            snapshot = snapshot with { CheckedAt = DateTimeOffset.UtcNow };
            await settings.RefreshRuntimeStatusAsync();
            Assert.Same(card, settings.GetLogicalDescendants().OfType<ModeStatusCard>().Distinct().Single(c => c.ModeId == "api"));
        }
        finally { settings.Close(); }
    }
}
