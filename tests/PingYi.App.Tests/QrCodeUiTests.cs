using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using PingYi.Core;
using Xunit;

namespace PingYi.App.Tests;

public class QrCodeUiTests
{
    [AvaloniaTheory]
    [InlineData("zh-CN", false)]
    [InlineData("en-US", true)]
    public void Multiple_results_preserve_payloads_and_only_selected_web_link_can_launch(string language, bool dark)
    {
        UiText.Configure(language);
        var window = new ResultWindow { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        var launched = new List<Uri>();
        window.OpenWebLinkAsync = uri => { launched.Add(uri); return Task.FromResult(true); };
        const string url = "https://example.com/qr-demo?source=screen-insight";
        const string text = "合成二维码示例：你好，世界！";
        try
        {
            window.Show();
            window.SetQrResults([new(url), new(text), new("javascript:alert(1)")]);
            Assert.Empty(launched);
            Assert.True(C<Button>(window, "OpenQrLinkButton").IsVisible);
            Assert.False(C<Border>(window, "SourceCard").IsVisible);
            Assert.False(C<TextBlock>(window, "AnalysisHint").IsVisible);
            Assert.Equal(url, C<TextBox>(window, "TranslationTextBox").Text);
            C<Button>(window, "OpenQrLinkButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(url, Assert.Single(launched).AbsoluteUri);
            SaveFrame(window, $"qr-result-{language}");

            C<ComboBox>(window, "QrSelectionCombo").SelectedIndex = 1;
            Assert.Equal(text, C<TextBox>(window, "TranslationTextBox").Text);
            Assert.False(C<Button>(window, "OpenQrLinkButton").IsVisible);
            C<ComboBox>(window, "QrSelectionCombo").SelectedIndex = 2;
            C<Button>(window, "OpenQrLinkButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(launched);
            Assert.Contains(text, window.BuildCopyText());

            UiText.Configure(language == "zh-CN" ? "en-US" : "zh-CN");
            Assert.Equal("javascript:alert(1)", C<TextBox>(window, "TranslationTextBox").Text);
            Assert.Equal(UiText.Get("String.DecodeQrCode"), C<TextBlock>(window, "ResultHeading").Text);
            window.SetPurpose(CapturePurpose.TranslateText);
            Assert.False(C<Button>(window, "OpenQrLinkButton").IsVisible);
            Assert.False(C<Grid>(window, "QrSelectionPanel").IsVisible);
            Assert.True(C<Border>(window, "SourceCard").IsVisible);
        }
        finally { window.ClosePermanently(); }
    }

    [AvaloniaFact]
    public void Empty_failed_and_retry_states_cannot_reuse_old_link_or_offer_model_repair()
    {
        UiText.Configure("zh-CN");
        var window = new ResultWindow();
        try
        {
            window.Show();
            window.SetQrResults([new("https://example.com")]);
            window.SetLoading(UiText.Get("String.QrLoading"), UiText.Get("String.QrPrivacy"));
            Assert.False(C<Button>(window, "OpenQrLinkButton").IsVisible);
            Assert.Equal(string.Empty, window.BuildCopyText());
            window.SetQrResults([]);
            Assert.False(C<Button>(window, "RepairButton").IsVisible);
            Assert.False(C<ProgressBar>(window, "ProcessingProgress").IsVisible);
            Assert.Equal(UiText.Get("String.QrNotFound"), C<TextBlock>(window, "StatusText").Text);
            window.SetQrFailure();
            Assert.False(C<Button>(window, "OpenQrLinkButton").IsVisible);
            Assert.False(C<Button>(window, "RepairButton").IsVisible);
            window.SetQrResults([new("https://example.com")]);
            window.OpenWebLinkAsync = _ => throw new InvalidOperationException("Synthetic failure");
            C<Button>(window, "OpenQrLinkButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(UiText.Get("String.QrOpenFailed"), C<TextBlock>(window, "StatusText").Text);
            Assert.True(C<Button>(window, "OpenQrLinkButton").IsEnabled);
        }
        finally { window.ClosePermanently(); }
    }

    [AvaloniaFact]
    public void Existing_capture_can_be_reused_for_qr_decoding()
    {
        var window = new ResultWindow();
        CapturePurpose? requested = null;
        window.AnalyzeRequested += purpose => { requested = purpose; return Task.CompletedTask; };
        try
        {
            window.Show();
            C<Button>(window, "QrResultButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(CapturePurpose.DecodeQrCode, requested);
        }
        finally { window.ClosePermanently(); }
    }

    private static T C<T>(Window window, string name) where T : Control => window.FindControl<T>(name)!;
    private static void SaveFrame(Window window, string name)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var directory = Environment.GetEnvironmentVariable("PINGYI_UI_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame); frame.Save(Path.Combine(directory, name + ".png"));
    }
}
