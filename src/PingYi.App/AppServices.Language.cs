using PingYi.Core;

namespace PingYi.App;

public sealed partial class AppServices
{
    // Change only this preference under the same gate as other settings writes.
    // Do not reload model services or persist any unrelated editor values.
    public async Task SaveUiLanguageAsync(string language)
    {
        await _settingsTransitionGate.WaitAsync(_lifetime.Token);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
            var updated = (Settings with { UiLanguage = language }).Normalize();
            await SettingsStore.SaveAsync(updated, _lifetime.Token);
            Settings = updated;
        }
        finally { _settingsTransitionGate.Release(); }
    }
}
