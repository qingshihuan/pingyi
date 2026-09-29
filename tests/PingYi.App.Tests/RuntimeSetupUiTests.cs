using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using PingYi.Core;
using Xunit;

namespace PingYi.App.Tests;

public sealed class RuntimeSetupUiTests
{
    [AvaloniaTheory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void Runtime_controls_show_multi_gpu_choices_and_preserve_unavailable_selection(string language)
    {
        UiText.Configure(language);
        RuntimeDevice[] devices = [new("cuda13", "CUDA0", "Synthetic GPU A", 8192, 6000), new("cuda13", "CUDA1", "Synthetic GPU B", 16384, 14000)];
        var chosen = RuntimeDeviceChoice.Encode(devices[1], devices);
        var backend = new ComboBox { ItemsSource = ManagedRuntimeBackends.All, SelectedItem = ManagedRuntimeBackends.Cuda13 };
        var panel = new RuntimeSetupPanel(null, backend, new AppSettings { ManagedRuntimeDevice = chosen });
        var window = new Window { Width = 750, Height = 640, Content = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(24), Spacing = 12, Children = { backend, panel } } } };
        try
        {
            window.Show(); panel.SetDeviceChoices(devices, "cuda13");
            Assert.Equal(chosen, panel.SelectedDevice);
            Assert.Equal(3, panel.Devices.ItemCount);
            Assert.False(panel.AllowMirrors);
            panel.SetParentBusy(true); Assert.False(panel.InstallButton.IsEnabled);
            panel.SetParentBusy(false); Assert.True(panel.InstallButton.IsEnabled);
            panel.SetDeviceChoices([], "cuda13"); Assert.Equal(chosen, panel.SelectedDevice);
            panel.SetDeviceChoices(devices, "cuda13");
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var directory = Environment.GetEnvironmentVariable("PINGYI_UI_ARTIFACTS");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                frame.Save(Path.Combine(directory, "runtime-hardware-" + language + ".png"));
            }
        }
        finally { panel.Cancel(); window.Close(); }
    }
}
