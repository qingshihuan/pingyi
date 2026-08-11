using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using PingYi.Core;
using CorePixelRect = PingYi.Core.PixelRect;

namespace PingYi.App;

public partial class ResultWindow : Window
{
    private CorePixelRect _anchor;
    private bool _allowClose;

    public ResultWindow()
    {
        InitializeComponent();
        UiText.Attach(this);
        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key == Key.Escape && PinButton.IsChecked != true)
            {
                Hide();
            }
        };
        Closing += (_, eventArgs) =>
        {
            if (_allowClose)
            {
                return;
            }

            eventArgs.Cancel = true;
            Hide();
        };
        Opened += (_, _) => ClampToScreen();
    }

    public event Func<Task>? RetryRequested;
    public event Action? OpenSettingsRequested;
    public bool IsPinned => PinButton.IsChecked == true;

    public void ShowAt(CorePixelRect anchor)
    {
        _anchor = anchor;
        Position = new PixelPoint(anchor.X, anchor.Y + anchor.Height + 10);
        if (!IsVisible)
        {
            Show();
        }
        Activate();
        Dispatcher.UIThread.Post(ClampToScreen, DispatcherPriority.Loaded);
    }

    public void ShowCurrent()
    {
        if (!IsVisible)
        {
            Show();
        }

        Activate();
        Dispatcher.UIThread.Post(ClampToScreen, DispatcherPriority.Loaded);
    }

    public void ClosePermanently()
    {
        _allowClose = true;
        Close();
    }

    public void SetLoading(string status, string privacy)
    {
        SetStatusVisual(status, "SecondaryTextBrush", "BrandBrush", isProcessing: true);
        PrivacyText.Text = privacy;
        SourceTextBox.Text = string.Empty;
        TranslationTextBox.Text = string.Empty;
        RepairButton.IsVisible = false;
    }

    public void SetSource(OcrResult result, string providerName)
    {
        SourceTextBox.Text = result.PlainText;
        SetStatusVisual($"{providerName} 已识别 · 正在翻译…", "SecondaryTextBrush", "BrandBrush", isProcessing: true);
    }

    public void SetTranslation(TranslationResult result, string providerName)
    {
        TranslationTextBox.Text = result.Text;
        SetStatusVisual($"处理完成 · {providerName}", "SuccessTextBrush", "SuccessBrush", isProcessing: false);
    }

    public void SetError(string message, bool keepSource = false)
    {
        SetStatusVisual(message, "DangerTextBrush", "DangerBrush", isProcessing: false);
        if (!keepSource)
        {
            SourceTextBox.Text = string.Empty;
        }
        TranslationTextBox.Text = keepSource ? UiText.T("翻译暂不可用，可复制上方原文或重新处理。") : string.Empty;
        RepairButton.IsVisible = true;
    }

    private async void CopySourceButton_OnClick(object? sender, RoutedEventArgs e) =>
        await CopyAsync(SourceTextBox.Text ?? string.Empty, "原文已复制");

    private async void CopyTranslationButton_OnClick(object? sender, RoutedEventArgs e) =>
        await CopyAsync(TranslationTextBox.Text ?? string.Empty, "译文已复制");

    private async void CopyAllButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var sourceLabel = UiText.T("原文");
        var translationLabel = UiText.T("译文");
        var text = $"{sourceLabel}{Environment.NewLine}{SourceTextBox.Text}{Environment.NewLine}{Environment.NewLine}{translationLabel}{Environment.NewLine}{TranslationTextBox.Text}";
        await CopyAsync(text, "原文与译文已复制");
    }

    private async Task CopyAsync(string text, string status)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
            SetStatusVisual(status, "SuccessTextBrush", "SuccessBrush", isProcessing: false);
        }
    }

    private async void RetryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button is not null) button.IsEnabled = false;
        try
        {
            if (RetryRequested is not null)
            {
                await RetryRequested.Invoke();
            }
        }
        finally
        {
            if (button is not null) button.IsEnabled = true;
        }
    }

    private void PinButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Topmost = PinButton.IsChecked == true;
        PinButtonLabel.Text = UiText.T(Topmost ? "已固定" : "固定");
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e) => Hide();

    private void RepairButton_OnClick(object? sender, RoutedEventArgs e) => OpenSettingsRequested?.Invoke();

    private void SetStatusVisual(string message, string foregroundKey, string indicatorKey, bool isProcessing)
    {
        StatusText.Text = UiText.T(message);
        StatusText.Foreground = Application.Current?.FindResource(foregroundKey) as IBrush;
        ResultStatusIndicator.Background = Application.Current?.FindResource(indicatorKey) as IBrush;
        ProcessingProgress.IsVisible = isProcessing;
    }

    private void ClampToScreen()
    {
        var screen = Screens.ScreenFromPoint(new PixelPoint(_anchor.X, _anchor.Y)) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var width = (int)Math.Ceiling(Bounds.Width * screen.Scaling);
        var height = (int)Math.Ceiling(Bounds.Height * screen.Scaling);
        var x = Math.Clamp(Position.X, area.X, Math.Max(area.X, area.Right - width));
        var y = Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, area.Bottom - height));
        Position = new PixelPoint(x, y);
    }
}
