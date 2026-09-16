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
        Purpose = purpose;
        var isText = purpose == CapturePurpose.TranslateText;
        var key = purpose == CapturePurpose.DescribeImage ? "String.DescribeImage" : "String.ReconstructPrompt";
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        SourceCard.IsVisible = isText;
        AnalysisHint.IsVisible = !isText;
        ResultContentGrid.RowDefinitions = new RowDefinitions(isText ? "*,*" : "0,*");
        ResultContentGrid.RowSpacing = isText ? 14 : 0;
        ThemeResources.Use(ResultHeading, TextBlock.TextProperty, isText ? "String.ResultTitle" : key);
        ThemeResources.Use(OutputHeading, TextBlock.TextProperty, isText ? "Text.80461c2decc5" : key);
        ThemeResources.Use(TranslationTextBox, AutomationProperties.NameProperty, isText ? "Text.49e1e6be89fd" : key);
        ThemeResources.Use(CopyOutputButton, AutomationProperties.NameProperty, isText ? "Text.032eb48c7a43" : "String.CopyAnalysis");
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

    public void UpdateLoadingStatus(string status) =>
        SetStatusVisual(status, "SecondaryTextBrush", "BrandBrush", true);

    private void CancelProcessing_OnClick(object? sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke();
        SetStatusVisual(UiText.Get("String.ProcessingCancelled"), "SecondaryTextBrush", "BrandBrush", false);
    }

    private async void TranslateResult_OnClick(object? sender, RoutedEventArgs e)
    {
        if (AnalyzeRequested is not null) await AnalyzeRequested(CapturePurpose.TranslateText);
    }

    internal string BuildCopyText() => Purpose == CapturePurpose.TranslateText
        ? $"{UiText.T("原文")}{Environment.NewLine}{SourceTextBox.Text}{Environment.NewLine}{Environment.NewLine}{UiText.T("译文")}{Environment.NewLine}{TranslationTextBox.Text}"
        : TranslationTextBox.Text ?? string.Empty;

    private async void DescribeResult_OnClick(object? sender, RoutedEventArgs e)
    {
        if (AnalyzeRequested is not null) await AnalyzeRequested(CapturePurpose.DescribeImage);
    }
    private async void PromptResult_OnClick(object? sender, RoutedEventArgs e)
    {
        if (AnalyzeRequested is not null) await AnalyzeRequested(CapturePurpose.ReconstructPrompt);
    }
}
