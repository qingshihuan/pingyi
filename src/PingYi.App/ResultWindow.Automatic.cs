using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using PingYi.Core;

namespace PingYi.App;

public partial class ResultWindow
{
    private CaptureDecision? _automaticDecision;
    private readonly TextBlock _automaticHint = new()
    {
        TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxHeight = 48,
        FontSize = 12, Margin = new Thickness(0, 0, 0, 4), IsVisible = false
    };
    private readonly Button _autoButton = new()
    {
        Name = "AutoResultButton", MinHeight = 30, Padding = new Thickness(10, 4), Margin = new Thickness(0, 0, 6, 4)
    };
    public CapturePurpose RequestedPurpose { get; private set; } = CapturePurpose.TranslateText;

    private void InitializeAutomaticControls()
    {
        // Extend the current result actions without late registration in a completed XAML name scope.
        if (ImageActionPanel.Parent is StackPanel host) host.Children.Insert(0, _automaticHint);
        _autoButton.Classes.Add("secondary");
        ImageActionPanel.Children.Insert(0, _autoButton);
        AutomationProperties.SetAutomationId(_autoButton, "AutoResultButton");
        AutomationProperties.SetLiveSetting(_automaticHint, AutomationLiveSetting.Polite);
        _autoButton.Click += async (_, _) => { if (AnalyzeRequested is not null) await AnalyzeRequested(CapturePurpose.Auto); };
        UiText.LanguageChanged += AutomaticLanguageChanged;
        Closed += (_, _) => UiText.LanguageChanged -= AutomaticLanguageChanged;
        RefreshAutomaticPresentation();
    }
    private void AutomaticLanguageChanged(object? sender, EventArgs e) => RefreshAutomaticPresentation();
    private void RefreshAutomaticPresentation()
    {
        _autoButton.Content = CaptureUiText.Automatic;
        AutomationProperties.SetName(_autoButton, CaptureUiText.Automatic);
        _automaticHint.Text = _automaticDecision is null ? "" : CaptureUiText.Decision(_automaticDecision);
        ToolTip.SetTip(_automaticHint, _automaticHint.Text);
        _automaticHint.IsVisible = _automaticDecision is not null;
        if (Purpose == CapturePurpose.Auto) { ResultHeading.Text = CaptureUiText.Automatic; OutputHeading.Text = CaptureUiText.ChooseTask; }
    }
    public void BeginPurpose(CapturePurpose purpose)
    {
        RequestedPurpose = purpose;
        _automaticDecision = null;
        SetPurpose(purpose);
        RefreshAutomaticPresentation();
    }
    public void SetAutomaticDecision(CaptureDecision decision)
    {
        _automaticDecision = decision;
        RefreshAutomaticPresentation();
    }
}
