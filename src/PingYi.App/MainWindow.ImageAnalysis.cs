using Avalonia.Controls;
using Avalonia.Interactivity;
using PingYi.Core;

namespace PingYi.App;

public partial class MainWindow
{
    private async void DescribeImage_OnClick(object? sender, RoutedEventArgs e) =>
        await CaptureImageAnalysisAsync(sender as Button, CapturePurpose.DescribeImage);

    private async void ReconstructPrompt_OnClick(object? sender, RoutedEventArgs e) =>
        await CaptureImageAnalysisAsync(sender as Button, CapturePurpose.ReconstructPrompt);

    private async Task CaptureImageAnalysisAsync(Button? button, CapturePurpose purpose)
    {
        if (_captureCoordinator is null)
        {
            SetGlobalStatus(UiText.T("截图服务尚未初始化。"), true);
            return;
        }
        if (button is not null) button.IsEnabled = false;
        try { await _captureCoordinator.StartCaptureAsync(this, purpose); }
        catch (Exception exception) { SetGlobalStatus(VisionErrors.Describe(exception), true); }
        finally { if (button is not null) button.IsEnabled = true; }
    }
}
