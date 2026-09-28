using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PingYi.Core;

namespace PingYi.App;

public partial class ResultWindow
{
    public CapturePurpose Purpose { get; private set; } = CapturePurpose.TranslateText;
    public event Func<CapturePurpose, Task>? AnalyzeRequested;
    public event Action? CancelRequested;

    public void SetPurpose(CapturePurpose purpose)
    {
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        Purpose = purpose;
        var isText = purpose == CapturePurpose.TranslateText;
        var isQr = purpose == CapturePurpose.DecodeQrCode;
        var isAuto = purpose == CapturePurpose.Auto;
        var key = purpose switch
        {
            CapturePurpose.DescribeImage => "String.DescribeImage",
            CapturePurpose.DecodeQrCode => "String.DecodeQrCode",
            _ => "String.ReconstructPrompt"
        };
        ClearQrResults();
        QrResultButton.IsVisible = !isQr;
        CopyAllButton.Classes.Set("primary", !isQr);
        CopyAllButton.Classes.Set("secondary", isQr);
        TranslateResultButton.IsVisible = !isText;
        SourceCard.IsVisible = isText;
        AnalysisHint.IsVisible = !isText && !isQr && !isAuto;
        ResultContentGrid.RowDefinitions = new RowDefinitions(isText ? "*,*" : "0,*");
        ResultContentGrid.RowSpacing = isText ? 14 : 0;
        ThemeResources.Use(ResultHeading, TextBlock.TextProperty, isText ? "String.ResultTitle" : key);
        ThemeResources.Use(OutputHeading, TextBlock.TextProperty, isText ? "Text.80461c2decc5" : isQr ? "String.QrContent" : key);
        ThemeResources.Use(TranslationTextBox, AutomationProperties.NameProperty, isText ? "Text.49e1e6be89fd" : key);
        ThemeResources.Use(CopyOutputButton, AutomationProperties.NameProperty,
            isText ? "Text.032eb48c7a43" : isQr ? "String.QrCopy" : "String.CopyAnalysis");
        RefreshAutomaticPresentation();
    }
    public void SetAnalysisResult(ImageAnalysisResult result)
    {
        SetPurpose(result.Purpose);
        SourceTextBox.Text = string.Empty;
        TranslationTextBox.Text = result.Text;
        SetStatusVisual(UiText.Get("String.AnalysisComplete"), "SuccessTextBrush", "SuccessBrush", false);
        RepairButton.IsVisible = false;
    }
    public void SetAnalysisCancelled(string message)
    {
        SetStatusVisual(message, "SecondaryTextBrush", "BrandBrush", false);
        RepairButton.IsVisible = false;
    }
    public void UpdateLoadingStatus(string status) => SetStatusVisual(status, "SecondaryTextBrush", "BrandBrush", true);
    private void CancelProcessing_OnClick(object? sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke();
        SetStatusVisual(UiText.Get(Purpose == CapturePurpose.DecodeQrCode ? "String.QrCancelled" : "String.ProcessingCancelled"),
            "SecondaryTextBrush", "BrandBrush", false);
    }
    private async void TranslateResult_OnClick(object? sender, RoutedEventArgs e)
    { if (AnalyzeRequested is not null) await AnalyzeRequested(CapturePurpose.TranslateText); }
    internal string BuildCopyText() => Purpose switch
    {
        CapturePurpose.TranslateText => $"{UiText.T("原文")}{Environment.NewLine}{SourceTextBox.Text}{Environment.NewLine}{Environment.NewLine}{UiText.T("译文")}{Environment.NewLine}{TranslationTextBox.Text}",
        CapturePurpose.DecodeQrCode => string.Join(Environment.NewLine + Environment.NewLine, _qrResults.Select(result => result.Text)),
        _ => TranslationTextBox.Text ?? string.Empty
    };
    private async void DescribeResult_OnClick(object? sender, RoutedEventArgs e)
    { if (AnalyzeRequested is not null) await AnalyzeRequested(CapturePurpose.DescribeImage); }
    private async void PromptResult_OnClick(object? sender, RoutedEventArgs e)
    { if (AnalyzeRequested is not null) await AnalyzeRequested(CapturePurpose.ReconstructPrompt); }
}
