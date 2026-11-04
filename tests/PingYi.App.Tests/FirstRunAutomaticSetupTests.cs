using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using PingYi.Core;
using Xunit;

namespace PingYi.App.Tests;

public sealed class FirstRunAutomaticSetupTests
{
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaTheory]
    [InlineData("zh-CN", 600, false)]
    [InlineData("en-US", 440, true)]
    public async Task Wizard_exposes_one_click_setup_and_waits_for_verified_save(string language, int width, bool dark)
    {
        UiText.Configure(language);
        var calls = 0;
        var verifiedAndSaved = new TaskCompletionSource();
        var owner = new Window();
        var dialog = new FirstRunSetupWindow(false,
            (_, _, _, _) => throw new InvalidOperationException("Automatic setup must use the shared automatic service"),
            async (mirrors, prefixes, progress, token) =>
            {
                calls++;
                Assert.False(mirrors);
                Assert.Empty(prefixes);
                progress.Report(new("verifying", "Synthetic capability verification", 0, 0, true));
                await verifiedAndSaved.Task.WaitAsync(token);
            }, (_, _) => throw new InvalidOperationException("Automatic service already saves the verified model"))
        {
            Width = width, Height = 720,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            owner.Show();
            var selection = dialog.ShowDialog<InitialSetupChoice>(owner);
            Dispatcher.UIThread.RunJobs();
            dialog.UpdateLayout();
            Assert.Equal(0, calls);
            Assert.True(dialog.AutomaticButton.IsEffectivelyVisible);
            Assert.False(dialog.ManualOptions.IsExpanded);
            Assert.Equal(language == "zh-CN" ? "一键检测并自动配置" : "Detect and configure automatically",
                AutomationProperties.GetName(dialog.AutomaticButton));
            var buttonPosition = dialog.AutomaticButton.TranslatePoint(default, dialog)!.Value;
            Assert.InRange(buttonPosition.Y, 0, dialog.ClientSize.Height - dialog.AutomaticButton.Bounds.Height);
            Assert.InRange(buttonPosition.X + dialog.AutomaticButton.Bounds.Width, 0, dialog.ClientSize.Width);
            Frame(dialog, "first-run-automatic-" + language);
            Click(dialog.AutomaticButton);
            var setup = dialog.OperationTask;
            Dispatcher.UIThread.RunJobs();
            Assert.False(selection.IsCompleted);
            Assert.False(dialog.AutomaticButton.IsEnabled);
            Assert.False(dialog.DownloadButton.IsEnabled);
            Assert.False(dialog.ApiButton.IsEnabled);
            Assert.True(dialog.CancelButton.IsVisible);
            Assert.Contains(dialog.GetLogicalDescendants().OfType<TextBlock>(),
                text => text.Text == "Synthetic capability verification");
            Click(dialog.AutomaticButton);
            Assert.Equal(1, calls);
            verifiedAndSaved.SetResult();
            await setup;
            Assert.Equal(InitialSetupChoice.Ready, await selection);
        }
        finally { dialog.Close(); owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Automatic_setup_cancel_or_close_never_completes_the_wizard(bool close)
    {
        var owner = new Window();
        var cancelled = false;
        var dialog = new FirstRunSetupWindow(true,
            (_, _, _, _) => throw new InvalidOperationException("Must not run manual setup"),
            async (_, _, _, token) =>
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { cancelled = true; throw; }
            }, (_, _) => throw new InvalidOperationException("Must not save a cancelled choice"));
        try
        {
            owner.Show();
            var selection = dialog.ShowDialog<InitialSetupChoice>(owner);
            Click(dialog.AutomaticButton);
            if (close) dialog.Close(); else Click(dialog.CancelButton);
            await dialog.OperationTask;
            Assert.True(cancelled);
            if (close) Assert.Equal(InitialSetupChoice.None, await selection);
            else
            {
                Assert.False(selection.IsCompleted);
                Assert.True(dialog.AutomaticButton.IsEnabled);
                Assert.True(dialog.ApiButton.IsEnabled);
                Assert.False(dialog.CancelButton.IsVisible);
            }
        }
        finally { dialog.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task Automatic_verification_failure_keeps_the_wizard_open_for_retry()
    {
        var attempts = 0;
        var owner = new Window();
        var dialog = new FirstRunSetupWindow(true,
            (_, _, _, _) => throw new InvalidOperationException("Must not run manual setup"),
            (_, _, _, _) => ++attempts == 1
                ? Task.FromException(new ProviderException("synthetic_failure", "Synthetic verification failure"))
                : Task.CompletedTask,
            (_, _) => throw new InvalidOperationException("Automatic service owns the save"));
        try
        {
            owner.Show();
            var selection = dialog.ShowDialog<InitialSetupChoice>(owner);
            Click(dialog.AutomaticButton);
            await dialog.OperationTask;
            Assert.False(selection.IsCompleted);
            Assert.Contains(dialog.GetLogicalDescendants().OfType<TextBlock>(),
                text => text.Text == "Synthetic verification failure");
            Assert.True(dialog.AutomaticButton.IsEnabled);
            Click(dialog.AutomaticButton);
            await dialog.OperationTask;
            Assert.Equal(InitialSetupChoice.Ready, await selection);
            Assert.Equal(2, attempts);
        }
        finally { dialog.Close(); owner.Close(); }
    }

    private static void Frame(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("PINGYI_UI_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}
