using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Xunit;

namespace PingYi.App.Tests;

public sealed class RuntimeStatusSpacingTests
{
    [AvaloniaTheory]
    [InlineData(1040, 720, "zh-CN", false)]
    [InlineData(1040, 720, "en-US", true)]
    [InlineData(1040, 800, "zh-CN", false)]
    [InlineData(1040, 800, "en-US", false)]
    [InlineData(1040, 900, "zh-CN", true)]
    [InlineData(1440, 900, "en-US", true)]
    public void Readable_cards_use_the_viewport_instead_of_leaving_a_blank_tail(
        int width, int height, string language, bool dark)
    {
        UiText.Configure(language);
        var home = new MainWindow { Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            home.Show();
            home.RenderRuntimeStatus(RuntimeStatusUiTests.SyntheticSnapshot());
            // Include the real, longer basic-mode pipeline labels, not just the short Lite labels.
            home.FindControl<TextBlock>("OcrSummaryText")!.Text = language == "zh-CN"
                ? "本机多模态大模型 OCR" : "Local multimodal model OCR";
            home.FindControl<TextBlock>("TranslationSummaryText")!.Text = language == "zh-CN"
                ? "本地 / 自定义大模型 · → 自动翻译" : "Local / custom LLM · → Auto translation";
            Settle(home);
            AssertLayout(home);
            Save(home, $"status-balanced-{width}x{height}-{language}-{dark}");
        }
        finally { home.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void Shrink_expand_language_changes_and_recovery_do_not_leave_stale_heights(string language)
    {
        UiText.Configure(language);
        var home = new MainWindow();
        try
        {
            home.Show(); home.RenderRuntimeStatus(RuntimeStatusUiTests.SyntheticSnapshot());
            foreach (var size in new[] { new Size(1040, 900), new Size(800, 560), new Size(1040, 720), new Size(1040, 800) })
            {
                home.Width = size.Width; home.Height = size.Height; Settle(home);
                var cards = home.FindControl<ModeStatusCards>("ModeCards")!.Cards;
                Assert.All(cards, card => Assert.True(card.Bounds.Height >= 71.5));
                if (size.Width >= 1040) AssertLayout(home);
                else
                {
                    var scroll = home.FindControl<ScrollViewer>("WorkspaceScroll")!;
                    Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                    scroll.Offset = new Vector(0, scroll.Extent.Height);
                    Settle(home);
                    Assert.True(scroll.Offset.Y > 0);
                    var panel = home.FindControl<Border>("ModeStatusBorder")!;
                    Assert.True(panel.TranslatePoint(new Point(0, panel.Bounds.Height), scroll)!.Value.Y <= scroll.Bounds.Height + 1);
                    Save(home, $"status-balanced-800x560-scrolled-{language}");
                    scroll.Offset = default;
                }
            }
            UiText.Configure(language == "zh-CN" ? "en-US" : "zh-CN"); Settle(home); AssertLayout(home);
            var recovery = home.FindControl<Border>("RecoveryBorder")!;
            recovery.IsVisible = true;
            home.FindControl<TextBlock>("RecoveryDetailText")!.Text = "Synthetic failure: the repair action must remain reachable.";
            Settle(home);
            var viewport = home.FindControl<ScrollViewer>("WorkspaceScroll")!;
            viewport.Offset = new Vector(0, viewport.Extent.Height); Settle(home);
            Assert.True(recovery.TranslatePoint(new Point(0, recovery.Bounds.Height), viewport)!.Value.Y <= viewport.Bounds.Height + 1);
            recovery.IsVisible = false; viewport.Offset = default; Settle(home); AssertLayout(home);
        }
        finally { home.Close(); }
    }

    private static void AssertLayout(MainWindow home)
    {
        var status = home.FindControl<Border>("ModeStatusBorder")!;
        var viewport = home.FindControl<ScrollViewer>("WorkspaceScroll")!;
        var bottom = status.TranslatePoint(new Point(0, status.Bounds.Height), viewport)!.Value.Y;
        Assert.True(bottom <= viewport.Bounds.Height + 1, $"Panel ends at {bottom}; viewport {viewport.Bounds.Height}.");
        Assert.InRange(viewport.Bounds.Height - bottom, 8, 17);
        Assert.True(status.Bounds.Height >= 139, $"Status panel is still compressed: {status.Bounds.Height}.");
        var cards = home.FindControl<ModeStatusCards>("ModeCards")!.Cards;
        Assert.All(cards, card =>
        {
            Assert.True(card.Bounds.Height >= 71.5, $"Mode card is only {card.Bounds.Height} high.");
            Assert.Equal(15, card.TitleText.FontSize);
            Assert.Equal(12, card.BadgeText.FontSize);
            var textTop = card.TitleText.TranslatePoint(default, card)!.Value.Y;
            var textBottom = textTop + card.TitleText.Bounds.Height;
            Assert.True(textTop >= 12 && textBottom <= card.Bounds.Height - 12);
        });
        for (var i = 1; i < cards.Length; i++) Assert.InRange(Math.Abs(cards[i].Bounds.Height - cards[0].Bounds.Height), 0, 1);
    }

    private static void Settle(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static void Save(Window home, string name)
    {
        var directory = Environment.GetEnvironmentVariable("PINGYI_UI_ARTIFACTS");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        home.FindControl<TextBlock>("LiveStatusDetailText")!.Text = WorkspaceText.Preview;
        Settle(home);
        using var frame = home.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }
}
