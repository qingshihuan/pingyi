using Avalonia.Controls;

namespace PingYi.App;

public partial class SettingsWindow
{
    private readonly CancellationTokenSource _runtimeStatusLifetime = new();
    private bool _runtimeStatusBusy, _runtimeStatusClosed, _statusOpenedWithoutProviders;
    private void InitializeRuntimeStatusPage()
    {
        RefreshRuntimeStatusLanguage();
        RuntimeStatusPage.RefreshRequested += async (_, _) => await RefreshRuntimeStatusAsync(true);
        RuntimeStatusPage.NavigateRequested += tab => SettingsTabs.SelectedIndex = tab;
        SettingsTabs.SelectionChanged += async (_, _) =>
        {
            if (_runtimeStatusClosed || _services is null) return;
            if (RuntimeStatusSettingsTab.IsSelected) await RefreshRuntimeStatusAsync();
            else if (_statusOpenedWithoutProviders)
            {
                _statusOpenedWithoutProviders = false;
                await RefreshCredentialStatusAsync();
                await RefreshLocalModelStatusAsync();
                await RefreshManagedModelStatusAsync(attemptConfiguredStart: false);
            }
        };
        Activated += async (_, _) => { if (RuntimeStatusSettingsTab.IsSelected) await RefreshRuntimeStatusAsync(); };
        Closed += (_, _) => { _runtimeStatusClosed = true; _runtimeStatusLifetime.Cancel(); };
    }
    private void RefreshRuntimeStatusLanguage() =>
        RuntimeStatusNavText.Text = ModeStatusText.Pick("运行状态", "Runtime status");
    internal void SelectRuntimeStatus() => SettingsTabs.SelectedItem = RuntimeStatusSettingsTab;
    private async Task RefreshRuntimeStatusAsync(bool force = false)
    {
        if (_services is null || _runtimeStatusBusy || _runtimeStatusClosed) return;
        _runtimeStatusBusy = true; RuntimeStatusPage.SetBusy(true);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var snapshot = await _services.ReadRuntimeStatusAsync(_runtimeStatusLifetime.Token, force);
                if (_runtimeStatusClosed) return;
                if (snapshot.Settings != _services.Settings) continue;
                RuntimeStatusPage.SetSnapshot(snapshot); return;
            }
            RuntimeStatusPage.SetError();
        }
        catch (OperationCanceledException) when (_runtimeStatusClosed || _services.IsShuttingDown) { }
        catch (Exception) { if (!_runtimeStatusClosed) RuntimeStatusPage.SetError(); }
        finally { _runtimeStatusBusy = false; if (!_runtimeStatusClosed) RuntimeStatusPage.SetBusy(false); }
    }
}

public partial class App
{
    internal async Task<bool> OpenRuntimeStatusPageAsync()
    {
        if (_services is null || _isExiting) return false;
        await OpenSettingsWindowAsync(true);
        return true;
    }
}
