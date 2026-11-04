using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PingYi.Core;

namespace PingYi.App;

public partial class ImageAnalysisConsentWindow : Window
{
    public ImageAnalysisConsentWindow()
    {
        InitializeComponent();
        UiText.Attach(this);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(RemoteCaptureConsentChoice.Deny); } };
        Opened += (_, _) => RejectButton.Focus();
    }

    public static async Task<RemoteCaptureConsentChoice> ConfirmAsync(Window owner, Uri endpoint, string model, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var dialog = new ImageAnalysisConsentWindow();
        dialog.DestinationText.Text = endpoint.GetLeftPart(UriPartial.Authority);
        dialog.ModelText.Text = model;
        using var registration = token.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(RemoteCaptureConsentChoice.Deny)));
        try { return await dialog.ShowDialog<RemoteCaptureConsentChoice>(owner).WaitAsync(token); }
        finally { dialog.Close(RemoteCaptureConsentChoice.Deny); }
    }
    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(RemoteCaptureConsentChoice.Deny);
    private void Confirm_OnClick(object? sender, RoutedEventArgs e) => Close(RemoteCaptureConsentChoice.AllowOnce);
    private void Remember_OnClick(object? sender, RoutedEventArgs e) => Close(RemoteCaptureConsentChoice.AlwaysAllow);
}
