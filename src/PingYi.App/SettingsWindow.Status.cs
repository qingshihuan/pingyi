using Avalonia.Controls;
using Avalonia.Threading;
using PingYi.Core;

namespace PingYi.App;

public partial class SettingsWindow
{
    private PassiveStatusUpdater? _runtimeStatusUpdater;
    private bool _runtimeStatusClosed;
    private void InitializeRuntimeStatusPage()
    {
        RuntimeStatusPage.RefreshRequested += async (_, _) => await RefreshRuntimeStatusAsync(true);
        RuntimeStatusPage.NavigateRequested += tab => SettingsTabs.SelectedIndex = tab;
        SettingsTabs.SelectionChanged += async (_, _) =>
        {
            if (_runtimeStatusClosed || _services is null) return;
            if (LocalSettingsTab.IsSelected) await RefreshRuntimeStatusAsync();
        };
        Activated += async (_, _) => { if (LocalSettingsTab.IsSelected) await RefreshRuntimeStatusAsync(); };
        Closed += (_, _) => { _runtimeStatusClosed = true; _runtimeStatusUpdater?.Dispose(); };
    }
    internal void SelectRuntimeStatus()
    {
        SettingsTabs.SelectedItem = LocalSettingsTab;
        if (RuntimeStatusPage.Parent is Expander expander) expander.IsExpanded = true;
    }
    internal Task RefreshRuntimeStatusAsync(bool force = false) => _runtimeStatusUpdater?.RequestAsync(force) ?? Task.CompletedTask;
    internal void InitializeStatusUpdates(Func<AppSettings> settings,
        Func<CancellationToken, bool, Task<RuntimeStatusSnapshot>> read, Func<bool> paused)
    {
        _runtimeStatusUpdater?.Dispose();
        _runtimeStatusUpdater = new PassiveStatusUpdater(
            () => IsVisible && WindowState != WindowState.Minimized && LocalSettingsTab.IsSelected && !_runtimeStatusClosed && !paused(),
            (token, force) => RefreshRuntimeStatusCoreAsync(settings, read, token, force));
    }
    private void InitializeStatusUpdates(AppServices services)
    {
        InitializeStatusUpdates(() => services.Settings, services.ReadRuntimeStatusAsync,
            () => services.IsInitialSetupActive || services.IsShuttingDown);
        EventHandler changed = (_, _) => Dispatcher.UIThread.Post(() => _ = RefreshRuntimeStatusAsync(true));
        services.RuntimeStatusChanged += changed;
        Closed += (_, _) => services.RuntimeStatusChanged -= changed;
    }
    private async Task RefreshRuntimeStatusCoreAsync(Func<AppSettings> settings,
        Func<CancellationToken, bool, Task<RuntimeStatusSnapshot>> read, CancellationToken token, bool force)
    {
        if (force || RuntimeStatusPage.Snapshot.CheckedAt is null) RuntimeStatusPage.SetBusy(true);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var snapshot = await read(token, force);
                if (_runtimeStatusClosed) return;
                if (snapshot.Settings != settings()) continue;
                RuntimeStatusPage.SetSnapshot(snapshot);
                return;
            }
            RuntimeStatusPage.SetError();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || _services?.IsShuttingDown == true) { }
        catch (Exception) { if (!_runtimeStatusClosed) RuntimeStatusPage.SetError(); }
        finally { if (!_runtimeStatusClosed) RuntimeStatusPage.SetBusy(false); }
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
