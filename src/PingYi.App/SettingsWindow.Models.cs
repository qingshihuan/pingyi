using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Diagnostics;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public partial class SettingsWindow
{
    private async void ManagedModelCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || _services is null || !ManagedModelExpander.IsVisible) return;
        UpdateManagedModelDescription();
        await RefreshManagedModelStatusAsync(false);
    }
    private void ManagedRuntimeBackendCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    { if (!_isLoadingSettings) UpdateManagedRuntimeBackendDescription(); }
    private async void ManagedModelInstallButton_OnClick(object? sender, RoutedEventArgs e) => await RunManagedModelSetupAsync(ManagedModelInstallButton);
    private async void ManagedModelStartButton_OnClick(object? sender, RoutedEventArgs e) => await RunManagedModelSetupAsync(ManagedModelStartButton);

    private async Task RunManagedModelSetupAsync(Button button)
    {
        if (_services is null || _managedModelOperation is not null || _runtimeHardware?.IsBusy == true || ManagedModelCombo.SelectedItem is not ManagedMultimodalModel model) return;
        try
        {
            if (BuildSettingsFromForm() != (_services.Settings with { InitialSetupCompleted = true }) || ConfigurationCenter.PendingKeys.Count > 0)
            { SetGlobalStatus(CaptureUiText.Pick("请先保存模型与 API 编辑，再配置托管模型。", "Save model and API edits before configuring a managed model."), true); return; }
        }
        catch (Exception error) { SetGlobalStatus(UiText.Error(error), true); return; }
        _managedModelOperation = new CancellationTokenSource();
        BeginButtonOperation(button, "正在下载并配置…");
        SetManagedModelBusy(true);
        SetGlobalStatus(CaptureUiText.Pick($"正在准备 {model.DisplayName}，下载后验证直接视觉 OCR 和翻译…",
            $"Preparing {model.LocalizedDisplayName}; direct visual OCR and translation will be verified…"), false);
        try
        {
            await ApplyAndStartManagedModelAsync(model, new Progress<ManagedModelProgress>(UpdateManagedModelProgress), _managedModelOperation.Token);
            SetGlobalStatus(CaptureUiText.Pick("当前模型已配置：本机多模态模型。", "Current model configured: local multimodal model."), false);
            FinishButtonOperation(button, "已安装并应用", true);
        }
        catch (OperationCanceledException)
        {
            SetInlineStatus(ManagedModelStatusText, "操作已取消；已下载部分会保留，下次可断点续传。", "WarningTextBrush");
            FinishButtonOperation(button, "已取消，可继续", false);
        }
        catch (Exception error)
        {
            SetInlineStatus(ManagedModelStatusText, UiText.Error(error), "DangerTextBrush");
            SetGlobalStatus(UiText.Error(error), true);
            FinishButtonOperation(button, "下载或配置失败", false);
        }
        finally
        {
            _managedModelOperation.Dispose(); _managedModelOperation = null;
            SetManagedModelBusy(false);
            await RefreshManagedModelStatusAsync(false);
        }
    }
    private void ManagedModelCancelButton_OnClick(object? sender, RoutedEventArgs e) => _managedModelOperation?.Cancel();
    private void ManagedModelFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null || ManagedModelCombo.SelectedItem is not ManagedMultimodalModel model) return;
        var directory = _services.ManagedModels.GetModelDirectory(model);
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
    }
    private void ManagedModelSourceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ManagedModelCombo.SelectedItem is ManagedMultimodalModel model)
            Process.Start(new ProcessStartInfo { FileName = model.ModelScopePageUrl, UseShellExecute = true });
    }
    private async Task ApplyAndStartManagedModelAsync(ManagedMultimodalModel model,
        IProgress<ManagedModelProgress> progress, CancellationToken token)
    {
        if (_services is null) return;
        await _services.ConfigureInitialModelAsync(model, SelectedManagedRuntimeBackendId,
            _runtimeHardware?.SelectedDevice ?? "auto", _runtimeHardware?.AllowMirrors ?? false,
            _runtimeHardware?.MirrorPrefixes ?? "", progress, token);
        LoadSettings(_services.Settings);
        SetInlineStatus(ManagedModelStatusText, CaptureUiText.Pick("模型已校验，直接视觉 OCR 与翻译测试通过。", "Model verified; direct vision OCR and translation checks passed."), "SuccessTextBrush");
    }
    private async Task RefreshManagedModelStatusAsync(bool attemptConfiguredStart)
    {
        if (_services is null || !ManagedModelExpander.IsVisible || ManagedModelCombo.SelectedItem is not ManagedMultimodalModel selected) return;
        SetInlineStatus(ManagedModelStatusText, "正在校验已下载模型…", "SecondaryTextBrush");
        try
        {
            var status = await _services.ManagedModels.GetStatusAsync(selected);
            SetInlineStatus(ManagedModelStatusText, status.Message, status.IsInstalled ? "SuccessTextBrush" : status.HasPartialDownload ? "WarningTextBrush" : "SecondaryTextBrush");
            ManagedModelInstallButton.IsEnabled = !status.IsInstalled;
            ManagedModelStartButton.IsEnabled = status.IsInstalled;
            if (attemptConfiguredStart && status.IsInstalled && _services.Settings.ManagedRuntimeEnabled &&
                string.Equals(_services.Settings.ManagedModelPackageId, selected.Id, StringComparison.OrdinalIgnoreCase))
            {
                var result = await _services.ManagedModels.EnsureStartedAsync(selected, _services.Settings.ManagedRuntimeBackend,
                    new Progress<ManagedModelProgress>(UpdateManagedModelProgress), deviceSelection: _services.Settings.ManagedRuntimeDevice);
                SetInlineStatus(ManagedModelStatusText, result, "SuccessTextBrush");
            }
        }
        catch (Exception error) { SetInlineStatus(ManagedModelStatusText, UiText.Error(error), "DangerTextBrush"); }
    }
    private void UpdateManagedModelDescription()
    {
        if (ManagedModelCombo.SelectedItem is not ManagedMultimodalModel model) return;
        ManagedModelSummaryText.Text = UiText.IsEnglish
            ? $"{model.LocalizedSummary} Quantization: {model.Quantization} · License: {model.License} · Released: {model.ReleaseDate}"
            : $"{model.Summary} 量化：{model.Quantization} · 许可：{model.License} · 发布：{model.ReleaseDate}";
        ManagedModelHardwareText.Text = model.LocalizedHardwareHint;
    }
    private string SelectedManagedRuntimeBackendId => (ManagedRuntimeBackendCombo.SelectedItem as ManagedRuntimeBackend)?.Id ?? ManagedRuntimeBackends.Default.Id;
    private void UpdateManagedRuntimeBackendDescription() => ManagedRuntimeBackendHintText.Text =
        (ManagedRuntimeBackendCombo.SelectedItem as ManagedRuntimeBackend ?? ManagedRuntimeBackends.Default).LocalizedDescription;
    private void UpdateManagedModelProgress(ManagedModelProgress progress)
    {
        if (_managedModelOperation is null) return;
        ManagedModelProgressBar.IsVisible = true;
        ManagedModelProgressBar.IsIndeterminate = progress.IsIndeterminate;
        if (!progress.IsIndeterminate) ManagedModelProgressBar.Value = progress.Percentage;
        var transferred = progress.TotalBytes > 0 ? $" · {FormatGiB(progress.BytesCompleted)}/{FormatGiB(progress.TotalBytes)}" : "";
        SetInlineStatus(ManagedModelStatusText, UiText.T(progress.Message) + transferred, "SecondaryTextBrush");
    }
    private void SetManagedModelBusy(bool busy)
    {
        if (busy) CancelButtonFeedbackReset(SaveSettingsButton);
        _runtimeHardware?.SetParentBusy(busy);
        ManagedModelCombo.IsEnabled = ManagedRuntimeBackendCombo.IsEnabled = ManagedModelSourceButton.IsEnabled =
            ManagedModelFolderButton.IsEnabled = ManagedModelInstallButton.IsEnabled = ManagedModelStartButton.IsEnabled = !busy;
        AutomaticLocalSetupButton.IsEnabled = SaveSettingsButton.IsEnabled = !busy;
        CurrentModelSettingsTab.IsEnabled = ApiSettingsTab.IsEnabled = HotkeySettingsTab.IsEnabled = GeneralSettingsTab.IsEnabled = !busy;
        ManagedModelCancelButton.IsVisible = busy; ManagedModelCancelButton.IsEnabled = busy;
        ManagedModelProgressBar.IsVisible = busy;
        if (!busy) ManagedModelProgressBar.IsIndeterminate = false;
    }
    private static string FormatGiB(long bytes) => $"{bytes / 1073741824d:0.00} GiB";
}
