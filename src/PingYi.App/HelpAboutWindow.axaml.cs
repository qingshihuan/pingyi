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
        ProductText.Text = UiText.IsEnglish ? AppEdition.IsComplete ? "Screen Insight Complete" : "Screen Insight" : AppEdition.ProductName;
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
        try
        {
            var route = ConfigurationRouting.Resolve(settings, ModelTasks.Ocr);
            return new Uri(route.Settings.CustomTranslationEndpoint).IsLoopback
                ? CaptureUiText.Pick("图片和文字只发送到本机模型服务。二维码在本机解析。", "Images and text go only to the local model service. QR decoding stays local.")
                : CaptureUiText.Pick("图片和文字经确认后发送到当前模型：", "After confirmation, images and text go to the current model: ") + route.Label;
        }
        catch (ProviderException) { return CaptureUiText.Pick("尚未选择有效的当前模型；不会发送截图。", "No valid current model selected; screenshots are not sent."); }
    }
}
