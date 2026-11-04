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
        Current = new(ModeReadinessState.Unconfigured, "current-unconfigured"),
        Basic = new(ModeReadinessState.Unconfigured, "basic-unconfigured"),
        Cloud = new(ModeReadinessState.Unconfigured, "cloud-unconfigured"),
        CheckedAt = DateTimeOffset.UtcNow
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
            Assert.Equal(new[] { "current", "basic", "cloud" }, cards.Select(c => c.ModeId));
            Assert.Equal(ModeReadinessState.Unconfigured, cards[0].State.State);
            Assert.All(cards.Skip(1), card => Assert.Equal(ModeReadinessState.Unconfigured, card.State.State));
            Assert.Equal(Color.Parse("#D74750"), ((ISolidColorBrush)cards[0].Indicator.Background!).Color);
            Assert.All(cards.Skip(1), card => Assert.Equal(Color.Parse("#D74750"), ((ISolidColorBrush)card.Indicator.Background!).Color));
            foreach (var card in cards)
            {
                Assert.Contains(card.TitleText.Text!, AutomationProperties.GetName(card));
                AssertInside(card.TitleText, card); AssertInside(card.BadgeText, card);
                Assert.True(card.Bounds.Width > 0);
            }
            Save(home, $"status-home-{width}-{language}-{dark}");
            UiText.Configure(language == "zh-CN" ? "en-US" : "zh-CN");
            Assert.Equal(language == "zh-CN" ? "Cloud APIs" : "云端 API", cards[2].TitleText.Text);
            Assert.Equal(ModeReadinessState.Unconfigured, cards[2].State.State);
        }
        finally { home.Close(); }
    }
    [AvaloniaFact]
    public void New_windows_never_show_a_fake_green_status_before_a_probe()
    {
        var home = new MainWindow();
        try { Assert.All(home.FindControl<ModeStatusCards>("ModeCards")!.Cards, card => Assert.False(card.State.IsReady)); }
        finally { home.Close(); }
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
        frame.Save(Path.Combine(directory, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}
