using PingYi.Core;

namespace PingYi.App;

public sealed partial class AppServices
{
    internal Task<AppSettings?> AuthorizeRemoteCaptureAsync(AppSettings expected,
        Func<CancellationToken, Task<RemoteCaptureConsentChoice>> prompt, CancellationToken token) =>
        RemoteCaptureConsent.AuthorizeAsync(expected, () => Settings, prompt, RememberRemoteCaptureAsync, token);

    private async Task<AppSettings> RememberRemoteCaptureAsync(AppSettings expected, CancellationToken token)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _settingsTransitionGate.WaitAsync(operation.Token);
        try
        {
            ObjectDisposedException.ThrowIf(IsShuttingDown, this);
            if (Settings != expected) throw new ProviderException("capture_consent_changed",
                "配置已变化，未保存授权，本次未发送。 / Configuration changed; approval was not saved and nothing was sent.");
            var next = RemoteCaptureConsent.Remember(Settings);
            await SettingsStore.SaveAsync(next, operation.Token);
            Settings = next;
            return next;
        }
        finally { _settingsTransitionGate.Release(); }
    }

    internal async Task ResetRemoteCaptureApprovalsAsync(CancellationToken token)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _settingsTransitionGate.WaitAsync(operation.Token);
        try
        {
            ObjectDisposedException.ThrowIf(IsShuttingDown, this);
            var next = Settings with { RemoteCaptureApprovals = "" };
            await SettingsStore.SaveAsync(next, operation.Token);
            Settings = next;
        }
        finally { _settingsTransitionGate.Release(); }
    }
}
