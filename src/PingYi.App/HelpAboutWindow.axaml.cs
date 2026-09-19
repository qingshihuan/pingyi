using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public partial class HelpAboutWindow : Window
{
    private readonly Func<AppSettings> _settings;
    public HelpAboutWindow() : this(() => new AppSettings()) { }
    public HelpAboutWindow(Func<AppSettings> settings)
    {
        _settings = settings;
        InitializeComponent();
        UiText.Attach(this);
        RefreshDetails();
        Activated += (_, _) => RefreshDetails();
        UiText.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => UiText.LanguageChanged -= OnLanguageChanged;
    }
    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshDetails();
    private void RefreshDetails()
    {
        var settings = _settings();
        ProductText.Text = UiText.IsEnglish ? AppEdition.IsComplete ? "PingYi Complete" : "PingYi" : AppEdition.ProductName;
        var version = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? typeof(App).Assembly.GetName().Version?.ToString(3) ?? "—";
        VersionText.Text = $"{UiText.Get("String.Version")}: {version}";
        CaptureShortcutText.Text = $"{UiText.Get("String.StartCapture")}: {LinuxDesktopUi.ShortcutLabel(settings.Hotkey)}";
        PrivacySummaryText.Text = DataUseDescription.For(settings) + LinuxDesktopUi.PortalPrivacyNote;
    }
    private void Close_OnClick(object? sender, RoutedEventArgs e) => Close();
    private void Repository_OnClick(object? sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://github.com/qingshihuan/pingyi") { UseShellExecute = true }); }
        catch (Exception exception) { VersionText.Text = UiText.Error(exception); }
    }
}

internal static class DataUseDescription
{
    public static string For(AppSettings settings)
    {
        var localEndpoint = AppSettings.TryParseChatCompletionsEndpoint(settings.CustomTranslationEndpoint, out var endpoint) && endpoint.IsLoopback;
        var recognition = settings.OcrProviderId switch
        {
            "local-paddle" => UiText.Get("String.OcrLocalData"),
            "local-vlm-ocr" or "local-vlm-corrected" => UiText.Get(localEndpoint ? "String.OcrLoopbackData" : "String.OcrRemoteData"),
            "baidu-ocr" => UiText.Get("String.OcrBaiduData"),
            "google-vision-ocr" => UiText.Get("String.OcrGoogleData"),
            _ => UiText.Get("String.UnknownDataRoute")
        };
        var translation = settings.TranslationProviderId switch
        {
            "local-argos" => UiText.Get("String.TranslationLocalData"),
            "custom-chat" => UiText.Get(localEndpoint ? "String.TranslationLoopbackData" : "String.TranslationRemoteData"),
            "baidu-translate" => UiText.Get("String.TranslationBaiduData"),
            "google-translate" => UiText.Get("String.TranslationGoogleData"),
            _ => UiText.Get("String.UnknownDataRoute")
        };
        return recognition + "\n" + translation;
    }
}
