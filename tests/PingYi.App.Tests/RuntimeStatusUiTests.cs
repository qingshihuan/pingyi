using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using PingYi.Core;
using Xunit;

namespace PingYi.App.Tests;

public sealed class RuntimeStatusUiTests
{
    internal static RuntimeStatusSnapshot SyntheticSnapshot() => RuntimeStatusSnapshot.Unknown with
    {
        Lightweight = ModeReadinessPolicy.Lightweight(true, true),
        Basic = new(ModeReadinessState.Unconfigured, "basic-unconfigured"),
        Cloud = new(ModeReadinessState.Unconfigured, "cloud-unconfigured"),
        PaddleReady = true, ArgosReady = true, CheckedAt = DateTimeOffset.UtcNow
    };
    [AvaloniaTheory]
    [InlineData(1040, "zh-CN", false)]
    [InlineData(1040, "en-US", true)]
    [InlineData(800, "zh-CN", true)]
    [InlineData(800, "en-US", false)]
    public void Home_renders_three_real_controls_with_non_overlapping_badges(int width, string language, bool dark)
    {
        UiText.Configure(language);
        var home = new MainWindow { Width = width, Height = 800,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            home.Show(); home.RenderRuntimeStatus(SyntheticSnapshot());
            home.FindControl<TextBlock>("LiveStatusDetailText")!.Text = WorkspaceText.Preview;
            home.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var cards = home.FindControl<ModeStatusCards>("ModeCards")!.Cards;
            Assert.Equal(new[] { "lite", "basic", "cloud" }, cards.Select(c => c.ModeId));
            Assert.Equal(ModeReadinessState.Available, cards[0].State.State);
            Assert.All(cards.Skip(1), card => Assert.Equal(ModeReadinessState.Unconfigured, card.State.State));
            Assert.Equal(Color.Parse("#2C9A5E"), ((ISolidColorBrush)cards[0].Indicator.Background!).Color);
            Assert.All(cards.Skip(1), card => Assert.Equal(Color.Parse("#D74750"), ((ISolidColorBrush)card.Indicator.Background!).Color));
            foreach (var card in cards)
            {
                Assert.Contains(card.TitleText.Text!, AutomationProperties.GetName(card));
                AssertInside(card.TitleText, card); AssertInside(card.BadgeText, card);
                Assert.True(card.Bounds.Width > 0);
            }
            Save(home, $"status-home-{width}-{language}-{dark}");
            UiText.Configure(language == "zh-CN" ? "en-US" : "zh-CN");
            Assert.Equal(language == "zh-CN" ? "Cloud mode" : "云端模式", cards[2].TitleText.Text);
            Assert.Equal(ModeReadinessState.Unconfigured, cards[2].State.State);
        }
        finally { home.Close(); }
    }
    [AvaloniaTheory]
    [InlineData("zh-CN", false)]
    [InlineData("en-US", true)]
    public void Details_are_a_real_sixth_settings_page_and_keep_unsaved_fields(string language, bool dark)
    {
        UiText.Configure(language);
        var settings = new SettingsWindow { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            settings.Show();
            var endpoint = settings.FindControl<TextBox>("CustomEndpointBox")!;
            endpoint.Text = "http://127.0.0.1:3456/v1";
            settings.SelectRuntimeStatus();
            Assert.Equal(5, settings.FindControl<TabControl>("SettingsTabs")!.SelectedIndex);
            var page = settings.FindControl<RuntimeStatusView>("RuntimeStatusPage")!;
            page.SetSnapshot(SyntheticSnapshot());
            settings.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var details = page.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
            Assert.Contains(details, text => text.Contains(language == "zh-CN" ? "未联网测试" : "not been tested online"));
            settings.FindControl<TextBlock>("GlobalStatusText")!.Text = WorkspaceText.Preview;
            Save(settings, $"status-settings-{language}-{dark}");
            settings.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 3;
            Assert.Equal("http://127.0.0.1:3456/v1", endpoint.Text);
            settings.SelectRuntimeStatus();
            Assert.Equal(SyntheticSnapshot().Cloud, page.Snapshot.Cloud);
        }
        finally { settings.Close(); }
    }
    [AvaloniaFact]
    public void New_windows_never_show_a_fake_green_status_before_a_probe()
    {
        var home = new MainWindow();
        try { Assert.All(home.FindControl<ModeStatusCards>("ModeCards")!.Cards, card => Assert.False(card.State.IsReady)); }
        finally { home.Close(); }
    }
    [Theory]
    [InlineData("https://example.com/v1/chat/completions", true)]
    [InlineData("http://127.0.0.1:8080/v1/chat/completions", false)]
    public async Task Passive_probe_never_calls_remote_endpoints_or_starts_managed_models(string endpoint, bool remote)
    {
        var calls = 0;
        var settings = new AppSettings { CustomTranslationEndpoint = endpoint, InitialSetupCompleted = true };
        var reachable = await AppServices.ProbeLocalServiceAsync(settings, false, _ =>
        { calls++; return Task.FromResult(ProviderAvailability.Available); }, CancellationToken.None);
        Assert.Equal(remote ? 0 : 1, calls);
        Assert.Equal(remote ? null : (bool?)true, reachable);
        await AppServices.ProbeLocalServiceAsync(settings, true, _ => throw new InvalidOperationException("must not probe a managed model"), CancellationToken.None);
    }
    [Fact]
    public async Task Cloud_snapshot_does_not_expose_secrets_or_claim_online_verification()
    {
        var state = await AppServices.ReadCloudStateAsync(new AppSettings(), new SecretStore(), CancellationToken.None);
        Assert.Equal(ModeReadinessState.Unverified, state.State);
        Assert.DoesNotContain("private-test-value", state.ToString());
        Assert.False(state.IsReady);
    }
    private sealed class SecretStore : ISecretStore
    {
        public Task<string?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<string?>(key == SecretKeys.GoogleCloudApiKey ? "private-test-value" : null);
        public Task SetAsync(string key, string value, CancellationToken token = default) => throw new InvalidOperationException();
        public Task DeleteAsync(string key, CancellationToken token = default) => throw new InvalidOperationException();
    }
    private static void AssertInside(Control child, Control parent)
    {
        var point = child.TranslatePoint(default, parent);
        Assert.True(point.HasValue);
        Assert.True(point.Value.X >= 0 && point.Value.Y >= 0);
        Assert.True(point.Value.X + child.Bounds.Width <= parent.Bounds.Width + 1,
            $"{child.GetType().Name}: x={point.Value.X}, width={child.Bounds.Width}, parent={parent.Bounds.Width}");
    }
    private static void Save(Window window, string name)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var directory = Environment.GetEnvironmentVariable("PINGYI_UI_ARTIFACTS");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }
}
