using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using PingYi.Core;
using Xunit;

namespace PingYi.App.Tests;

public sealed class ConfigurationSafetyUiTests
{
    private static ModelServiceProfile Profile => new() { Id = "service-test", Name = "Synthetic service",
        Endpoint = "https://example.test/v1/chat/completions", DefaultModel = "vision-test",
        Models = [new("vision-test", true, true)] };
    private static SettingsWindow Open()
    {
        UiText.Configure("zh-CN"); var window = new SettingsWindow();
        window.ConfigurationCenter.Load(new() { ModelConfiguration = new() { Services = [Profile] } });
        window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 3;
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); return window;
    }
    private static void Fetch(ConfigurationCenterView view) => view.GetLogicalDescendants().OfType<Button>().Distinct()
        .Single(button => Equals(button.Content, "获取模型列表")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public async Task Raw_network_or_header_errors_do_not_echo_credentials_in_status()
    {
        var window = Open();
        try
        {
            var view = window.ConfigurationCenter;
            view.Discover = (_, _, _) => throw new FormatException("secret-test-key and remote response body");
            Fetch(view); await view.OperationTask;
            Assert.DoesNotContain("secret-test-key", view.StatusMessage);
            Assert.DoesNotContain("remote response body", view.StatusMessage);
            Assert.Contains("操作未完成", view.StatusMessage);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Changing_an_account_key_invalidates_old_capability_checks_without_deleting_model_ids()
    {
        var window = Open();
        try
        {
            var view = window.ConfigurationCenter; view.KeyEditor.Text = "replacement-test-key";
            var profile = Assert.Single(view.BuildDraft().Services);
            var model = Assert.Single(profile.Models);
            Assert.Equal("vision-test", model.Id); Assert.False(model.TextVerified); Assert.False(model.VisionVerified);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Saving_during_model_discovery_is_blocked_until_the_request_is_cancelled()
    {
        var window = Open(); var view = window.ConfigurationCenter;
        view.Discover = async (_, _, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return []; };
        Fetch(view);
        var error = Assert.Throws<ProviderException>(() => view.BuildDraft());
        Assert.Equal("profile_operation_active", error.Code);
        window.Close(); await view.OperationTask;
    }
}
