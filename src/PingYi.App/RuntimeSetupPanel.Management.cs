using Avalonia.Controls;
using Avalonia.Media;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

internal sealed partial class RuntimeSetupPanel
{
    internal Button SwitchButton { get; } = new() { Name = "SwitchRuntimeButton" };
    internal Button UninstallButton { get; } = new() { Name = "UninstallRuntimeButton" };
    internal Button DefaultButton { get; } = new() { Name = "DefaultVulkanButton" };
    internal TextBlock ManagementStatus { get; } = new() { TextWrapping = TextWrapping.Wrap };
    internal Task OperationTask { get; private set; } = Task.CompletedTask;
    public bool IsBusy => _work is not null;
    private Func<string, string, bool, string, IProgress<ManagedModelProgress>, CancellationToken, Task>? _switchRuntime;
    private Func<string, CancellationToken, Task<RuntimeRemovalResult>>? _removeRuntime;
    private Func<string, bool>? _hasDownloaded;
    private Func<AppSettings>? _currentSettings;
    private string? _removalConfirmation;
    private DateTimeOffset _confirmationUntil;
    public event Action<bool>? ActivityChanged;

    private string SelectedBackend => (_backend.SelectedItem as ManagedRuntimeBackend)?.Id ?? ManagedRuntimeBackends.Default.Id;

    private void InitializeManagement(AppServices? services)
    {
        SwitchButton.Content = Pick("安装并切换到所选后端", "Install and switch to selected backend");
        UninstallButton.Content = Pick("卸载所选已下载后端", "Uninstall selected downloaded backend");
        DefaultButton.Content = Pick("选择默认 Vulkan", "Select default Vulkan");
        foreach (var button in new[] { SwitchButton, UninstallButton, DefaultButton })
        {
            button.Classes.Add("secondary");
            Avalonia.Automation.AutomationProperties.SetAutomationId(button, button.Name);
            Avalonia.Automation.AutomationProperties.SetName(button, button.Content!.ToString());
        }
        ManagementStatus.Classes.Add("workspace-help");
        ManagementStatus.Text = Pick("默认 Vulkan。更换时先验证新后端；成功切换后再卸载旧的已下载后端。内置 Vulkan／CPU 保留，不删除模型、驱动或外部服务。",
            "Vulkan is the default. Verify and switch first, then uninstall downloaded old backends. Bundled Vulkan/CPU, models, drivers and external services are retained.");
        _currentSettings = services is null ? null : () => services.Settings;
        _switchRuntime = services is null ? null : services.SwitchRuntimeAsync;
        _removeRuntime = services is null ? null : services.UninstallRuntimeAsync;
        _hasDownloaded = _manager is null ? null : _manager.HasDownloadedBackend;
        SwitchButton.Click += async (_, _) => { OperationTask = SwitchAsync(); await OperationTask; };
        UninstallButton.Click += async (_, _) => { OperationTask = UninstallAsync(); await OperationTask; };
        DefaultButton.Click += (_, _) => _backend.SelectedItem = ManagedRuntimeBackends.Default;
        SetManagementControls(true);
    }

    // The same control path can be tested without downloading packages or inventing GPU success.
    internal void SetManagementActions(
        Func<string, string, bool, string, IProgress<ManagedModelProgress>, CancellationToken, Task> change,
        Func<string, CancellationToken, Task<RuntimeRemovalResult>> remove, Func<string, bool> hasDownloaded)
    { _switchRuntime = change; _removeRuntime = remove; _hasDownloaded = hasDownloaded; SetControls(); }

    private void SetManagementControls(bool ready)
    {
        SwitchButton.IsEnabled = ready && _switchRuntime is not null;
        UninstallButton.IsEnabled = ready && SelectedBackend != "auto" && _removeRuntime is not null &&
            (_hasDownloaded?.Invoke(SelectedBackend) ?? false);
        DefaultButton.IsEnabled = ready;
    }

    internal Task SwitchAsync()
    {
        if (_switchRuntime is null) return Task.CompletedTask;
        return RunManagementAsync(async token =>
        {
            _removalConfirmation = null;
            await _switchRuntime(SelectedBackend, SelectedDevice, AllowMirrors, MirrorPrefixes,
                new Progress<ManagedModelProgress>(p =>
                {
                    if (_closed || _work is null) return;
                    _progress.IsIndeterminate = p.IsIndeterminate; _progress.Value = p.Percentage;
                    _status.Text = UiText.T(p.Message);
                }), token);
            var refreshWarning = await RefreshAfterCommittedOperationAsync(token);
            ManagementStatus.Text = Pick("所选后端已保存。已配置托管模型时，切换包含 OCR／翻译验证；未配置模型时请继续模型配置。旧后端文件保留，可选择并卸载已下载的旧后端。",
                "Backend choice saved. Configured managed models pass OCR/translation verification; otherwise continue model setup. Previous downloaded backends may now be selected and uninstalled.") + refreshWarning;
        });
    }

    internal Task UninstallAsync()
    {
        if (_removeRuntime is null || _work is not null || _closed || _parentBusy) return Task.CompletedTask;
        var backend = SelectedBackend;
        if (_hasDownloaded?.Invoke(backend) != true) return Task.CompletedTask;
        if (_removalConfirmation != backend || DateTimeOffset.UtcNow > _confirmationUntil)
        {
            _removalConfirmation = backend; _confirmationUntil = DateTimeOffset.UtcNow.AddSeconds(10);
            ManagementStatus.Text = Pick($"再次点击确认卸载已下载的 {backend} 后端及其未共享缓存。请先切换到其他后端。内置 Vulkan／CPU、模型、配置与驱动不会删除。",
                $"Click again to uninstall downloaded {backend} and its unshared cache. Switch away first. Bundled Vulkan/CPU, models, settings and drivers will not be deleted.");
            return Task.CompletedTask;
        }
        _removalConfirmation = null;
        return RunManagementAsync(async token =>
        {
            var result = await _removeRuntime(backend, token);
            var saved = _currentSettings?.Invoke();
            _selection = saved?.ManagedRuntimeDevice ?? "auto";
            if (saved is not null) _backend.SelectedItem = ManagedRuntimeBackends.Get(saved.ManagedRuntimeBackend);
            var refreshWarning = await RefreshAfterCommittedOperationAsync(token);
            ManagementStatus.Text = result.CleanupPending
                ? Pick("已停用下载后端，但部分文件被占用，尚未释放全部磁盘空间；退出占用程序后再次清理。内置后端和模型保留。",
                       "Downloaded backend disabled; some files are locked and disk cleanup is pending. Close their owner and retry. Bundled runtimes and models remain.")
                : Pick("已清理所选已下载后端；内置 Vulkan／CPU、模型、配置和系统驱动保留。",
                       "Selected downloaded backend removed. Bundled Vulkan/CPU, models, settings and system drivers retained.");
            ManagementStatus.Text += refreshWarning;
        });
    }

    private async Task<string> RefreshAfterCommittedOperationAsync(CancellationToken token)
    {
        if (_manager is null || _closed) return "";
        try { await RefreshCoreAsync(token); return ""; }
        catch (Exception)
        {
            // The mutation already succeeded. A failed/cancelled presentation refresh must
            // not claim that old preferences were restored or that the operation was cancelled.
            SetDeviceChoices([], null);
            return Pick("\n操作已完成，但设备列表刷新未完成，请点击检测／刷新显卡。",
                "\nOperation completed, but device refresh did not. Select Detect / refresh GPUs.");
        }
    }

    private async Task RunManagementAsync(Func<CancellationToken, Task> action)
    {
        if (_closed || _parentBusy || _work is not null) return;
        _work = new CancellationTokenSource();
        var enabled = _backend.IsEnabled;
        _backend.IsEnabled = false; SetControls(); ActivityChanged?.Invoke(true);
        try { await action(_work.Token); }
        catch (OperationCanceledException)
        {
            if (!_closed) ManagementStatus.Text = Pick("已取消，原配置和旧后端文件保留。", "Cancelled; previous settings and backend files retained.");
        }
        catch (Exception error)
        {
            if (!_closed) ManagementStatus.Text = Pick("操作未完成，未宣称切换成功：", "Operation failed; no successful switch is claimed: ") + UiText.Error(error);
        }
        finally
        {
            _work.Dispose(); _work = null; _backend.IsEnabled = enabled;
            ActivityChanged?.Invoke(false); SetControls();
        }
    }
}
