using PingYi.Core;

namespace PingYi.App;

public sealed partial class AppServices
{
    public async Task SaveUiLanguageAsync(string language)
    {
        if (language is not (UiText.Auto or UiText.Chinese or UiText.English))
            throw new ArgumentException("Unsupported interface language.", nameof(language));
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
