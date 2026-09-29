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
        if (_services is null || _managedModelOperation is not null || ManagedModelCombo.SelectedItem is not ManagedMultimodalModel model) return;
        _managedModelOperation = new CancellationTokenSource();
        BeginButtonOperation(button, "正在下载并配置…");
        SetManagedModelBusy(true);
        SetGlobalStatus(CaptureUiText.Pick($"正在准备 {model.DisplayName}，下载后验证直接视觉 OCR 和翻译…",
            $"Preparing {model.LocalizedDisplayName}; direct visual OCR and translation will be verified…"), false);
        try
        {
            await ApplyAndStartManagedModelAsync(model, new Progress<ManagedModelProgress>(UpdateManagedModelProgress), _managedModelOperation.Token);
            SetGlobalStatus(CaptureUiText.Pick("基础模式已配置：本机视觉 OCR 与本机模型翻译。", "Basic configured: local vision OCR and local model translation."), false);
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
        _isLoadingSettings = true;
        try
        {
            CustomEndpointBox.Text = AppSettings.ManagedModelEndpoint;
            CustomModelBox.Text = model.ModelAlias;
            SelectOcrProvider("local-vlm-ocr");
            SelectTranslationProvider("custom-chat");
        }
        finally { _isLoadingSettings = false; }
        SetInlineStatus(ManagedModelStatusText, CaptureUiText.Pick("模型已校验，直接视觉 OCR 与翻译测试通过。", "Model verified; direct vision OCR and translation checks passed."), "SuccessTextBrush");
    }
    private async Task RefreshManagedModelStatusAsync(bool attemptConfiguredStart)
    {
        if (_services is null || !ManagedModelExpander.IsVisible || ManagedModelCombo.SelectedItem is not ManagedMultimodalModel selected) return;
        if (!_services.ManagedModels.HasBundledRuntime)
        {
            SetInlineStatus(ManagedModelStatusText, "完全版 llama.cpp 运行时缺失，请重新安装完全版。", "DangerTextBrush");
            ManagedModelInstallButton.IsEnabled = ManagedModelStartButton.IsEnabled = false;
            return;
        }
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
    private string SelectedManagedRuntimeBackendId => (ManagedRuntimeBackendCombo.SelectedItem as ManagedRuntimeBackend)?.Id ?? ManagedRuntimeBackends.Auto.Id;
    private void UpdateManagedRuntimeBackendDescription() => ManagedRuntimeBackendHintText.Text =
        (ManagedRuntimeBackendCombo.SelectedItem as ManagedRuntimeBackend ?? ManagedRuntimeBackends.Auto).LocalizedDescription;
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
        _runtimeHardware?.SetParentBusy(busy);
        ManagedModelCombo.IsEnabled = ManagedRuntimeBackendCombo.IsEnabled = ManagedModelSourceButton.IsEnabled =
            ManagedModelFolderButton.IsEnabled = ManagedModelInstallButton.IsEnabled = ManagedModelStartButton.IsEnabled = !busy;
        ManagedModelCancelButton.IsVisible = busy; ManagedModelCancelButton.IsEnabled = busy;
        ManagedModelProgressBar.IsVisible = busy;
        if (!busy) ManagedModelProgressBar.IsIndeterminate = false;
    }
    private static string FormatGiB(long bytes) => $"{bytes / 1073741824d:0.00} GiB";
    private async void InstallOcrModelsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null) return;
        var button = sender as Button;
        BeginButtonOperation(button, "正在下载…");
        SetInlineStatus(OcrModelStatusText, "OCR 模型：正在下载并校验…", "SecondaryTextBrush");
        try
        {
            await _services.PaddleProvider.InstallModelsAsync();
            await RefreshLocalModelStatusAsync();
            SetGlobalStatus("中英离线 OCR 模型安装完成并已校验。", false);
            FinishButtonOperation(button, "下载完成", true, isEnabledAfterResult: false);
        }
        catch (Exception error)
        {
            SetInlineStatus(OcrModelStatusText, $"OCR 模型：{UiText.Error(error)}", "DangerTextBrush");
            SetGlobalStatus(UiText.Error(error), true); FinishButtonOperation(button, "下载失败", false);
        }
    }
    private async void InstallTranslationModelsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null) return;
        var button = sender as Button;
        BeginButtonOperation(button, "正在下载…");
        SetInlineStatus(TranslationModelStatusText, "翻译模型：正在下载并校验…", "SecondaryTextBrush");
        try
        {
            await _services.ArgosProvider.InstallModelsAsync();
            await RefreshLocalModelStatusAsync();
            SetGlobalStatus("中英离线翻译模型安装完成并已校验。", false);
            FinishButtonOperation(button, "下载完成", true, isEnabledAfterResult: false);
        }
        catch (Exception error)
        {
            SetInlineStatus(TranslationModelStatusText, $"翻译模型：{UiText.Error(error)}", "DangerTextBrush");
            SetGlobalStatus(UiText.Error(error), true); FinishButtonOperation(button, "下载失败", false);
        }
    }
    private async void DeleteModelsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null) return;
        if (DateTimeOffset.UtcNow > _deleteConfirmationExpiresAt)
        {
            _deleteConfirmationExpiresAt = DateTimeOffset.UtcNow.AddSeconds(8);
            DeleteModelsButton.Content = "确认清理下载模型";
            SetGlobalStatus("再次点击可清理用户下载的翻译模型；安装包内离线基础模型、凭据和设置都会保留。", false);
            _ = ResetDeleteConfirmationAsync(_deleteConfirmationExpiresAt); return;
        }
        try
        {
            DeleteModelsButton.IsEnabled = false;
            await _services.ClearDownloadedTranslationModelsAsync();
            await RefreshLocalModelStatusAsync();
            SetGlobalStatus("用户下载模型已清理，安装包内离线基础模型仍可使用。", false);
        }
        catch (Exception error) { SetGlobalStatus(UiText.Error(error), true); }
        finally { DeleteModelsButton.IsEnabled = true; _deleteConfirmationExpiresAt = default; DeleteModelsButton.Content = _deleteModelsDefaultContent; }
    }
    private async Task ResetDeleteConfirmationAsync(DateTimeOffset expiresAt)
    {
        await Task.Delay(TimeSpan.FromSeconds(8));
        if (_deleteConfirmationExpiresAt != expiresAt || DateTimeOffset.UtcNow < expiresAt) return;
        _deleteConfirmationExpiresAt = default;
        DeleteModelsButton.Content = _deleteModelsDefaultContent;
        SetGlobalStatus("删除操作已取消。", false);
    }
    private async Task RefreshLocalModelStatusAsync()
    {
        if (_services is null) return;
        SetInlineStatus(OcrModelStatusText, "OCR 模型：正在检查…", "SecondaryTextBrush");
        SetInlineStatus(TranslationModelStatusText, "翻译模型：正在检查…", "SecondaryTextBrush");
        var ocr = await _services.PaddleProvider.GetAvailabilityAsync();
        SetInlineStatus(OcrModelStatusText, ocr.IsAvailable ? "OCR 模型：已安装，可离线使用" : $"OCR 模型：{ocr.Message ?? "不可用"}", ocr.IsAvailable ? "SuccessTextBrush" : "WarningTextBrush");
        SetModelInstallButtonState(InstallOcrModelsButton, OcrModelButtonText, OcrModelDownloadIcon, OcrModelInstalledIcon,
            ocr.IsAvailable, "下载中英 OCR 模型", "中英 OCR 模型已安装");
        var translation = await _services.ArgosProvider.GetAvailabilityAsync();
        SetInlineStatus(TranslationModelStatusText, translation.IsAvailable ? "翻译模型：已安装，可离线使用" : $"翻译模型：{translation.Message ?? "不可用"}", translation.IsAvailable ? "SuccessTextBrush" : "WarningTextBrush");
        SetModelInstallButtonState(InstallTranslationModelsButton, TranslationModelButtonText, TranslationModelDownloadIcon, TranslationModelInstalledIcon,
            translation.IsAvailable, "下载中英翻译模型", "中英翻译模型已安装");
    }
}
