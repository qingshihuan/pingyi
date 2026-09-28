using Avalonia.Controls;
using Avalonia.Interactivity;
using PingYi.Core;

namespace PingYi.App;

public partial class ResultWindow
{
    private IReadOnlyList<QrCodeResult> _qrResults = [];
    internal Func<Uri, Task<bool>> OpenWebLinkAsync { get; set; } = DesktopWebLinks.OpenAsync;

    private void ClearQrResults()
    {
        _qrResults = [];
        QrSelectionCombo.ItemsSource = null;
        QrSelectionPanel.IsVisible = false;
        OpenQrLinkButton.IsVisible = false;
    }

    public void SetQrResults(IReadOnlyList<QrCodeResult> results)
    {
        SetPurpose(CapturePurpose.DecodeQrCode);
        _qrResults = results.ToArray();
        PrivacyText.Text = UiText.Get("String.QrPrivacy");
        SourceTextBox.Text = string.Empty;
        TranslationTextBox.Text = string.Empty;
        RepairButton.IsVisible = false;
        if (_qrResults.Count == 0)
        {
            SetStatusVisual(UiText.Get("String.QrNotFound"), "SecondaryTextBrush", "BrandBrush", false);
            return;
        }
        QrSelectionPanel.IsVisible = _qrResults.Count > 1;
        QrSelectionCombo.ItemsSource = Enumerable.Range(1, _qrResults.Count).ToArray();
        QrSelectionCombo.SelectedIndex = 0;
        UpdateSelectedQr();
    }

    public void SetQrFailure()
    {
        ClearQrResults();
        TranslationTextBox.Text = string.Empty;
        SetStatusVisual(UiText.Get("String.QrFailed"), "DangerTextBrush", "DangerBrush", false);
        RepairButton.IsVisible = false;
    }

    private QrCodeResult? SelectedQr => Purpose == CapturePurpose.DecodeQrCode &&
        QrSelectionCombo.SelectedIndex >= 0 && QrSelectionCombo.SelectedIndex < _qrResults.Count
            ? _qrResults[QrSelectionCombo.SelectedIndex] : null;

    private void QrSelection_OnChanged(object? sender, SelectionChangedEventArgs e) => UpdateSelectedQr();

    private void UpdateSelectedQr()
    {
        var selected = SelectedQr;
        if (selected is null) { OpenQrLinkButton.IsVisible = false; return; }
        TranslationTextBox.Text = selected.Text;
        OpenQrLinkButton.IsVisible = selected.WebUri is not null;
        OpenQrLinkButton.IsEnabled = true;
        SetStatusVisual(UiText.Get(selected.WebUri is not null ? "String.QrWebResult" : "String.QrTextResult"),
            "SuccessTextBrush", "SuccessBrush", false);
    }

    private async void OpenQrLink_OnClick(object? sender, RoutedEventArgs e)
    {
        var selected = SelectedQr;
        if (selected?.WebUri is not { } uri) return;
        OpenQrLinkButton.IsEnabled = false;
        bool opened;
        try { opened = await OpenWebLinkAsync(uri); }
        catch { opened = false; }
        if (!ReferenceEquals(selected, SelectedQr)) return;
        OpenQrLinkButton.IsEnabled = true;
        SetStatusVisual(UiText.Get(opened ? "String.QrOpened" : "String.QrOpenFailed"),
            opened ? "SuccessTextBrush" : "DangerTextBrush", opened ? "SuccessBrush" : "DangerBrush", false);
    }

    private async void QrResult_OnClick(object? sender, RoutedEventArgs e)
    {
        if (AnalyzeRequested is not null) await AnalyzeRequested(CapturePurpose.DecodeQrCode);
    }
}
