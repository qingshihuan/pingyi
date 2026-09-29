using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using PingYi.Core;
using PingYi.Infrastructure;
using Xunit;

namespace PingYi.App.Tests;

public sealed class RuntimeManagementUiTests
{
    [AvaloniaTheory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public async Task Switching_is_explicit_and_uninstall_requires_a_second_click(string language)
    {
        UiText.Configure(language);
        var backends = new ComboBox { ItemsSource = ManagedRuntimeBackends.All, SelectedItem = ManagedRuntimeBackends.Default };
        var panel = new RuntimeSetupPanel(null, backends, new AppSettings());
        var switches = 0; var removed = 0;
        panel.SetManagementActions((backend, device, relay, prefix, progress, token) =>
        { Assert.Equal("rocm", backend); switches++; return Task.CompletedTask; },
            (backend, token) => { Assert.Equal("vulkan", backend); removed++; return Task.FromResult(new RuntimeRemovalResult(true, false)); }, _ => true);
        var window = new Window { Content = new StackPanel { Children = { backends, panel } } };
        try
        {
            window.Show();
            backends.SelectedItem = ManagedRuntimeBackends.Rocm;
            Assert.Equal(0, switches);
            panel.SwitchButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await panel.OperationTask;
            Assert.Equal(1, switches);
            panel.DefaultButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(ManagedRuntimeBackends.Vulkan, backends.SelectedItem);
            Assert.Equal(1, switches);
            panel.UninstallButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await panel.OperationTask;
            Assert.Equal(0, removed);
            panel.UninstallButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await panel.OperationTask;
            Assert.Equal(1, removed);
            Assert.Contains("Vulkan", panel.ManagementStatus.Text);
        }
        finally { panel.Cancel(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Busy_switch_blocks_duplicate_actions_and_cancellation_remains_available()
    {
        var backends = new ComboBox { ItemsSource = ManagedRuntimeBackends.All, SelectedItem = ManagedRuntimeBackends.Rocm };
        var panel = new RuntimeSetupPanel(null, backends, new AppSettings());
        var calls = 0; var changes = new List<bool>();
        panel.ActivityChanged += changes.Add;
        panel.SetManagementActions(async (_, _, _, _, _, token) => { calls++; await Task.Delay(Timeout.Infinite, token); },
            (_, _) => Task.FromResult(new RuntimeRemovalResult(true, false)), _ => true);
        var pending = panel.SwitchAsync();
        Assert.False(backends.IsEnabled); Assert.False(panel.UninstallButton.IsEnabled);
        await panel.SwitchAsync(); Assert.Equal(1, calls);
        panel.Cancel(); await pending;
        Assert.Equal(new[] { true, false }, changes);
        Assert.False(panel.IsBusy);
    }

    [AvaloniaFact]
    public async Task Failed_switch_is_reported_and_never_runs_uninstall()
    {
        UiText.Configure("en-US");
        var backends = new ComboBox { ItemsSource = ManagedRuntimeBackends.All, SelectedItem = ManagedRuntimeBackends.Rocm };
        var panel = new RuntimeSetupPanel(null, backends, new AppSettings());
        panel.SetManagementActions((_, _, _, _, _, _) => throw new IOException("Synthetic ROCm dependency failure"),
            (_, _) => throw new InvalidOperationException("Must not uninstall"), _ => false);
        await panel.SwitchAsync();
        Assert.Contains("Operation failed", panel.ManagementStatus.Text);
        Assert.False(panel.UninstallButton.IsEnabled);
        panel.Cancel();
    }
}
