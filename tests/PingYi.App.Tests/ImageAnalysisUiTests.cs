using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PingYi.Core;
using Xunit;

namespace PingYi.App.Tests;

public class ImageAnalysisUiTests
{
    [AvaloniaTheory]
    [InlineData("zh-CN", false)]
    [InlineData("en-US", true)]
    public void Result_uses_the_full_output_area_and_preserves_content_during_language_switch(string language, bool dark)
    {
        UiText.Configure(language);
        var window = new ResultWindow { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show();
            window.SetPurpose(CapturePurpose.DescribeImage);
            window.SetLoading(UiText.Get("String.AnalysisPreparing"), UiText.Get("String.VisionLocal"));
            Assert.True(C<Button>(window, "CancelProcessingButton").IsVisible);
            Assert.False(C<Button>(window, "DescribeResultButton").IsEffectivelyEnabled);
            var text = UiText.IsEnglish ? "Synthetic preview: a blue circle on a white background." : "合成预览：白色背景上有一个蓝色圆形。";
            window.SetAnalysisResult(new(text, CapturePurpose.DescribeImage, "synthetic"));
            window.UpdateLayout();
            Assert.False(C<Border>(window, "SourceCard").IsVisible);
            Assert.True(C<TextBox>(window, "TranslationTextBox").Bounds.Height > 180);
            Assert.Equal(text, window.BuildCopyText());
            Assert.False(C<Button>(window, "CancelProcessingButton").IsVisible);
            Assert.True(C<Button>(window, "DescribeResultButton").IsEffectivelyEnabled);
            Frame(window, $"vision-description-{language}");
            UiText.Configure(language == "zh-CN" ? "en-US" : "zh-CN");
            Assert.Equal(text, C<TextBox>(window, "TranslationTextBox").Text);
            Assert.Equal(UiText.Get("String.DescribeImage"), C<TextBlock>(window, "ResultHeading").Text);
            window.SetPurpose(CapturePurpose.ReconstructPrompt);
            window.SetAnalysisResult(new(text, CapturePurpose.ReconstructPrompt, "synthetic"));
            Assert.Equal(UiText.Get("String.ReconstructPrompt"), C<TextBlock>(window, "OutputHeading").Text);
            window.SetPurpose(CapturePurpose.TranslateText);
            Assert.True(C<Border>(window, "SourceCard").IsVisible);
            Assert.False(C<TextBlock>(window, "AnalysisHint").IsVisible);
        }
        finally { window.ClosePermanently(); }
    }

    [AvaloniaTheory]
    [InlineData(800, 560, "zh-CN")]
    [InlineData(800, 560, "en-US")]
    [InlineData(1040, 720, "zh-CN")]
    [InlineData(1040, 720, "en-US")]
    public void New_capture_actions_are_visible_and_have_localized_labels(int width, int height, string language)
    {
        UiText.Configure(language);
        var window = new MainWindow { Width = width, Height = height };
        try
        {
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            foreach (var name in new[] { "CaptureButtonV2", "DescribeImageButton", "ReconstructPromptButton" })
            {
                var control = C<Button>(window, name);
                var point = control.TranslatePoint(default, window);
                Assert.True(point.HasValue && control.Bounds.Width > 0);
                Assert.True(point.Value.X >= 0 && point.Value.Y >= 0);
                Assert.True(point.Value.X + control.Bounds.Width <= window.ClientSize.Width + 1);
                Assert.True(point.Value.Y + control.Bounds.Height <= window.ClientSize.Height + 1);
            }
            C<TextBlock>(window, "LiveStatusDetailText").Text = WorkspaceText.Preview;
            Frame(window, $"vision-home-{width}-{language}");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Result_buttons_route_the_selected_task_and_cancel_disables_busy_state()
    {
        UiText.Configure("en-US");
        var window = new ResultWindow();
        var purposes = new List<CapturePurpose>();
        var cancelled = false;
        window.AnalyzeRequested += purpose => { purposes.Add(purpose); return Task.CompletedTask; };
        window.CancelRequested += () => cancelled = true;
        try
        {
            window.Show();
            foreach (var name in new[] { "DescribeResultButton", "PromptResultButton", "TranslateResultButton" })
                C<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new[] { CapturePurpose.DescribeImage, CapturePurpose.ReconstructPrompt, CapturePurpose.TranslateText }, purposes);
            window.SetLoading("Waiting", "Local");
            C<Button>(window, "CancelProcessingButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(cancelled);
            Assert.False(C<ProgressBar>(window, "ProcessingProgress").IsVisible);
            Assert.True(C<Button>(window, "DescribeResultButton").IsEffectivelyEnabled);
        }
        finally { window.ClosePermanently(); }
    }

    [AvaloniaTheory]
    [InlineData(false, "zh-CN")]
    [InlineData(true, "en-US")]
    public async Task Consent_is_an_explicit_choice_not_a_preselected_permission(bool accept, string language)
    {
        UiText.Configure(language);
        var owner = new ResultWindow();
        var dialog = new ImageAnalysisConsentWindow();
        try
        {
            owner.Show();
            C<TextBlock>(dialog, "DestinationText").Text = "https://example.invalid";
            C<TextBlock>(dialog, "ModelText").Text = "synthetic-vision";
            var decision = dialog.ShowDialog<bool>(owner);
            dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.False(decision.IsCompleted);
            Frame(dialog, $"vision-consent-{language}");
            C<Button>(dialog, accept ? "ConfirmButton" : "RejectButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(accept, await decision);
        }
        finally { dialog.Close(false); owner.ClosePermanently(); }
    }

    [AvaloniaFact]
    public async Task Cancelled_consent_does_not_open_a_window()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var owner = new ResultWindow();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImageAnalysisConsentWindow.ConfirmAsync(
                owner, new Uri("https://example.invalid"), "synthetic", cts.Token));
        }
        finally { owner.ClosePermanently(); }
    }

    private static T C<T>(Window window, string name) where T : Control => window.FindControl<T>(name)!;
    private static void Frame(Window window, string name)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var directory = Environment.GetEnvironmentVariable("PINGYI_UI_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame); frame.Save(Path.Combine(directory, name + ".png"));
    }
}
