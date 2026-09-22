using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public sealed partial class AppServices
{
    private readonly SemaphoreSlim _hotkeyEditGate = new(1, 1);
    // UI status is independent from model readiness; healthy OCR must not erase binding errors.
    public Exception? HotkeyRegistrationError { get; internal set; }
    public bool IsEditingHotkey { get; private set; }
    public event EventHandler? HotkeyChanged;

    public async Task ApplyHotkeySettingsAsync(AppSettings settings)
    {
        await _hotkeyEditGate.WaitAsync();
        IsEditingHotkey = true;
        try
        {
            var normalized = settings with { Hotkey = GlobalHotkeyGesture.Parse(settings.Hotkey).ToString() };
            await HotkeyBindingChange.ApplyAsync(HotkeyService, Settings.Hotkey, normalized.Hotkey,
                () => SaveSettingsAsync(normalized), error => HotkeyRegistrationError = error,
                retry: HotkeyRegistrationError is not null);
            HotkeyChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { IsEditingHotkey = false; _hotkeyEditGate.Release(); }
    }

    public async Task<string?> RecordHotkeyAsync(Func<Task<string?>> record)
    {
        await _hotkeyEditGate.WaitAsync();
        IsEditingHotkey = true;
        try
        {
            return await HotkeyBindingChange.RecordAsync(HotkeyService, Settings.Hotkey,
                record, error => HotkeyRegistrationError = error);
        }
        finally { IsEditingHotkey = false; _hotkeyEditGate.Release(); }
    }
}
