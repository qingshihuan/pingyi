using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PingYi.Core;

namespace PingYi.App;

public partial class SettingsWindow
{
    private void InitializeDesktopShortcuts()
    {
        HotkeyBox.PlaceholderText = AppSettings.DefaultHotkey;
        LinuxShortcutHelpPanel.IsVisible = OperatingSystem.IsLinux();
        CaptureCommandBox.Text = DesktopCaptureCommand();
        RefreshHotkeyFeedback();
        UiText.LanguageChanged += HotkeyStateChanged;
        Closed += (_, _) => UiText.LanguageChanged -= HotkeyStateChanged;
    }
    private void AttachHotkeyFeedback()
    {
        if (_services?.HotkeyService is IGlobalHotkeyStatus status)
        {
            status.RegistrationChanged += HotkeyStateChanged;
            Closed += (_, _) => status.RegistrationChanged -= HotkeyStateChanged;
        }
        RefreshHotkeyFeedback();
    }
    private void HotkeyStateChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshHotkeyFeedback);
    private void RefreshHotkeyFeedback() => HotkeyRegistrationText.Text = HotkeyFeedback.Message(_services?.HotkeyService);
    private async void RetryHotkey_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null) return;
        try
        {
            await _services.HotkeyService.StopAsync();
            await _services.HotkeyService.StartAsync(_services.Settings.Hotkey);
        }
        catch (Exception error) { SetGlobalStatus(UiText.Error(error), true); }
    }
    private static string DesktopCaptureCommand()
    {
        static string Quote(string text) => "'" + text.Replace("'", "'\\''") + "'";
        var executable = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PingYi.App");
        return Path.GetFileNameWithoutExtension(executable) == "dotnet"
            ? Quote(executable) + " " + Quote(typeof(App).Assembly.Location) + " --capture"
            : Quote(executable) + " --capture";
    }
    private async void CopyCaptureCommand_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard is not { } clipboard) throw new InvalidOperationException(UiText.T("剪贴板不可用。"));
            await clipboard.SetTextAsync(CaptureCommandBox.Text);
            SetGlobalStatus(UiText.Get("Linux.CopiedCommand"), false);
        }
        catch (Exception error) { SetGlobalStatus(UiText.Error(error), true); }
    }
    private void SystemKeyboard_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            const string program = "/usr/bin/gnome-control-center";
            if (!OperatingSystem.IsLinux() || !File.Exists(program))
            { SetGlobalStatus(UiText.Get("Linux.KeyboardMissing"), true); return; }
            var info = new ProcessStartInfo(program) { UseShellExecute = false };
            info.ArgumentList.Add("keyboard");
            Process.Start(info)?.Dispose();
        }
        catch (Exception error) { SetGlobalStatus(UiText.Error(error), true); }
    }
}
