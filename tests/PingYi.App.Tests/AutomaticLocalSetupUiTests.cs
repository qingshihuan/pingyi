using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PingYi.Core;
using PingYi.Infrastructure;
using Xunit;

namespace PingYi.App.Tests;

public sealed class AutomaticLocalSetupUiTests
{
    private static AppSettings Before => new()
    {
        ModelConfiguration = new()
        {
            Services = [new() { Id = "test-api", Name = "Synthetic API", Endpoint = "https://example.test/v1",
                DefaultModel = "vision", Models = [new("vision", true, true)] }],
            CurrentModel = new("test-api", "vision")
        }
    };
    private static AppSettings Completed(AppSettings before) => before with
    {
        ManagedModelPackageId = ManagedMultimodalModels.Recommended.Id,
        ManagedRuntimeBackend = "auto", ManagedRuntimeDevice = "auto", ManagedRuntimeEnabled = true,
        ModelConfiguration = before.ModelConfiguration! with
        {
            CurrentModel = new(ModelConfiguration.Managed, ManagedMultimodalModels.Recommended.Id),
            VerifiedManagedModelId = ManagedMultimodalModels.Recommended.Id
        }
    };
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaTheory]
    [InlineData("zh-CN", "auto")]
    [InlineData("en-US", "cpu")]
    public async Task One_click_applies_local_results_without_overwriting_api_key_or_general_drafts(string language, string backend)
    {
        UiText.Configure(language);
        var before = Before;
        var window = new SettingsWindow();
        try
        {
            window.LoadSettings(before);
            window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 2;
            window.ConfigurationCenter.KeyEditor.Text = "unsaved-synthetic-key";
            window.FindControl<TextBox>("HotkeyBox")!.Text = "Ctrl+Alt+Q";
            window.FindControl<CheckBox>("StartMinimizedCheckBox")!.IsChecked = true;
            var calls = 0;
            window.ConfigureLocalAutomatically = (_, _, progress, _) =>
            {
                calls++;
                progress.Report(new("detecting", "Synthetic local detection", 0, 0, true));
                return Task.FromResult(Completed(before) with { ManagedRuntimeBackend = backend });
            };
            Click(window.FindControl<Button>("AutomaticLocalSetupButton")!);
            await window.AutomaticModelSetupTask;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, calls);
            Assert.Equal("unsaved-synthetic-key", window.ConfigurationCenter.KeyEditor.Text);
            Assert.Equal("Ctrl+Alt+Q", window.FindControl<TextBox>("HotkeyBox")!.Text);
            Assert.True(window.FindControl<CheckBox>("StartMinimizedCheckBox")!.IsChecked);
            var configuration = window.BuildModelConfiguration();
            Assert.Equal(ModelConfiguration.Managed, configuration.CurrentModel!.ServiceId);
            Assert.Single(configuration.Services);
            Assert.Equal(configuration.CurrentModel, window.ConfigurationCenter.Baseline!.CurrentModel);
            Assert.Equal(ManagedRuntimeBackends.Get(backend), window.FindControl<ComboBox>("ManagedRuntimeBackendCombo")!.SelectedItem);
            Assert.True(window.FindControl<Button>("SaveSettingsButton")!.IsEnabled);
            Assert.False(window.FindControl<Button>("AutomaticLocalSetupCancelButton")!.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Busy_setup_blocks_duplicates_and_cancel_preserves_the_current_model()
    {
        var before = Before;
        var window = new SettingsWindow();
        try
        {
            window.LoadSettings(before);
            var calls = 0;
            window.ConfigureLocalAutomatically = async (_, _, _, token) =>
            {
                calls++;
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Completed(before);
            };
            var pending = window.RunAutomaticLocalSetupAsync();
            Assert.False(window.FindControl<Button>("SaveSettingsButton")!.IsEnabled);
            Assert.False(window.FindControl<Button>("ManagedModelStartButton")!.IsEnabled);
            Assert.False(window.FindControl<TabItem>("ApiSettingsTab")!.IsEnabled);
            await window.RunAutomaticLocalSetupAsync();
            Assert.Equal(1, calls);
            Click(window.FindControl<Button>("AutomaticLocalSetupCancelButton")!);
            await pending;
            Assert.Equal(before.ModelConfiguration!.CurrentModel, window.BuildModelConfiguration().CurrentModel);
            Assert.True(window.FindControl<Button>("AutomaticLocalSetupButton")!.IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Failure_does_not_replace_the_model_or_erase_a_draft_key()
    {
        UiText.Configure("en-US");
        var before = Before;
        var window = new SettingsWindow();
        try
        {
            window.LoadSettings(before);
            window.ConfigurationCenter.KeyEditor.Text = "retain-synthetic-draft";
            window.ConfigureLocalAutomatically = (_, _, _, _) => throw new IOException("Synthetic setup failure");
            await window.RunAutomaticLocalSetupAsync();
            Assert.Equal("retain-synthetic-draft", window.ConfigurationCenter.KeyEditor.Text);
            Assert.Equal(before.ModelConfiguration!.CurrentModel, window.ConfigurationCenter.Baseline!.CurrentModel);
            Assert.Contains("Synthetic setup failure", window.FindControl<TextBlock>("AutomaticLocalSetupStatus")!.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Closing_the_window_cancels_automatic_configuration()
    {
        var window = new SettingsWindow();
        var cancelled = false;
        window.ConfigureLocalAutomatically = async (_, _, _, token) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { cancelled = true; throw; }
            return Completed(Before);
        };
        window.Show();
        var pending = window.RunAutomaticLocalSetupAsync();
        window.Close();
        await pending;
        Assert.True(cancelled);
    }
}
