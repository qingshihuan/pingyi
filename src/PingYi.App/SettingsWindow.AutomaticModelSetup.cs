using Avalonia.Interactivity;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public partial class SettingsWindow
{
    internal Func<bool, string, IProgress<ManagedModelProgress>, CancellationToken, Task<AppSettings>>? ConfigureLocalAutomatically { get; set; }
    internal Task AutomaticModelSetupTask { get; private set; } = Task.CompletedTask;

    private async void AutomaticLocalSetup_OnClick(object? sender, RoutedEventArgs e)
    {
        AutomaticModelSetupTask = RunAutomaticLocalSetupAsync();
        await AutomaticModelSetupTask;
    }

    internal async Task RunAutomaticLocalSetupAsync()
    {
        if (ConfigureLocalAutomatically is null || _settingsClosed || _managedModelOperation is not null) return;
        if (_runtimeHardware?.IsBusy == true)
        {
            SetGlobalStatus(CaptureUiText.Pick("设备操作正在进行，请完成后再自动配置。", "Finish the current device operation before automatic setup."), false);
            return;
        }
        using var operation = new CancellationTokenSource();
        _managedModelOperation = operation;
        SetManagedModelBusy(true);
        AutomaticLocalSetupCancelButton.IsVisible = true;
        AutomaticLocalSetupProgress.IsVisible = AutomaticLocalSetupProgress.IsIndeterminate = true;
        SetInlineStatus(AutomaticLocalSetupStatus, CaptureUiText.Pick("正在检测并准备本地模型…", "Detecting and preparing the local model…"), "SecondaryTextBrush");
        try
        {
            var progress = new Progress<ManagedModelProgress>(value =>
            {
                if (_settingsClosed || !ReferenceEquals(_managedModelOperation, operation)) return;
                AutomaticLocalSetupProgress.IsIndeterminate = value.IsIndeterminate;
                if (!value.IsIndeterminate) AutomaticLocalSetupProgress.Value = value.Percentage;
                var transferred = value.TotalBytes > 0 ? $" · {FormatGiB(value.BytesCompleted)}/{FormatGiB(value.TotalBytes)}" : "";
                SetInlineStatus(AutomaticLocalSetupStatus, value.Message + transferred, "SecondaryTextBrush");
                UpdateManagedModelProgress(value);
            });
            var settings = await ConfigureLocalAutomatically(_runtimeHardware?.AllowMirrors ?? false,
                _runtimeHardware?.MirrorPrefixes ?? "", progress, operation.Token);
            if (_settingsClosed) return;
            ApplyAutomaticLocalSetupResult(settings);
            var complete = CaptureUiText.Pick("自动配置完成，识字与翻译验证通过，已设为当前模型。", "Automatic setup complete. Recognition and translation verified; current model selected.");
            complete += "\n" + (_services?.ManagedModels.CurrentRuntimeDescription ??
                ManagedRuntimeBackends.Get(settings.ManagedRuntimeBackend).LocalizedDisplayName);
            SetInlineStatus(AutomaticLocalSetupStatus, complete, "SuccessTextBrush");
            SetGlobalStatus(complete, false);
        }
        catch (OperationCanceledException)
        {
            if (!_settingsClosed) SetInlineStatus(AutomaticLocalSetupStatus,
                CaptureUiText.Pick("已取消，原配置保留；已下载部分可继续使用或续传。", "Cancelled. Previous settings retained; downloads can be reused or resumed."), "WarningTextBrush");
        }
        catch (Exception error)
        {
            if (!_settingsClosed)
            {
                SetInlineStatus(AutomaticLocalSetupStatus, UiText.Error(error), "DangerTextBrush");
                SetGlobalStatus(UiText.Error(error), true);
            }
        }
        finally
        {
            _managedModelOperation = null;
            if (!_settingsClosed)
            {
                SetManagedModelBusy(false);
                AutomaticLocalSetupCancelButton.IsVisible = false;
                AutomaticLocalSetupProgress.IsVisible = false;
                await RefreshManagedModelStatusAsync(false);
                if (_runtimeHardware is not null) await _runtimeHardware.RefreshAsync();
                await RefreshRuntimeStatusAsync(true);
            }
        }
    }

    internal void ApplyAutomaticLocalSetupResult(AppSettings settings)
    {
        // Update only the committed local model; preserve API/key, hotkey and general-setting drafts.
        _isLoadingSettings = true;
        try
        {
            _baselineConfiguration = settings.ModelConfiguration;
            if (settings.ModelConfiguration is { } configuration) ConfigurationCenter.UpdateCurrentModelContext(configuration);
            if (ManagedMultimodalModels.TryGet(settings.ManagedModelPackageId, out var model))
            {
                ManagedModelCombo.SelectedItem = model;
                var current = new CurrentChoice(new(ModelConfiguration.Managed, model.Id),
                    CaptureUiText.Pick("本地托管 · ", "Managed local · ") + model.LocalizedDisplayName);
                CurrentModelCombo.ItemsSource = CurrentModelCombo.Items.OfType<CurrentChoice>()
                    .Where(choice => choice.Selection?.ServiceId != ModelConfiguration.Managed).Append(current).ToArray();
                CurrentModelCombo.SelectedItem = current;
            }
            ManagedRuntimeBackendCombo.SelectedItem = ManagedRuntimeBackends.Get(settings.ManagedRuntimeBackend);
            _runtimeHardware?.ApplySettings(settings);
            MigrationNoticeText.Text = settings.ModelConfiguration?.MigrationNotice;
            UpdateManagedModelDescription(); UpdateManagedRuntimeBackendDescription();
        }
        finally { _isLoadingSettings = false; }
    }
}
