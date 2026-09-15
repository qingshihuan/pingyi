using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Diagnostics;
using PingYi.Core;
using PingYi.Infrastructure;
using SkiaSharp;

namespace PingYi.App;

public partial class SettingsWindow
{
    private async void ManagedModelCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || _services is null || !ManagedModelExpander.IsVisible)
        {
            return;
        }

        UpdateManagedModelDescription();
        await RefreshManagedModelStatusAsync(attemptConfiguredStart: false);
    }

    private void ManagedRuntimeBackendCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        UpdateManagedRuntimeBackendDescription();
    }

    private async void ManagedModelInstallButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null || ManagedModelCombo.SelectedItem is not ManagedMultimodalModel model)
        {
            return;
        }

        _managedModelOperation?.Cancel();
        _managedModelOperation?.Dispose();
        _managedModelOperation = new CancellationTokenSource();
        BeginButtonOperation(ManagedModelInstallButton, "正在下载并配置…");
        SetManagedModelBusy(true);
        SetGlobalStatus(UiText.IsEnglish
            ? $"Downloading {model.LocalizedDisplayName} from ModelScope…"
            : $"正在从魔搭下载 {model.DisplayName}…", isError: false);
        try
        {
            var progress = new Progress<ManagedModelProgress>(UpdateManagedModelProgress);
            await _services.ManagedModels.DownloadAsync(model, progress, _managedModelOperation.Token);
            await ApplyAndStartManagedModelAsync(model, progress, _managedModelOperation.Token);
            SetGlobalStatus(UiText.IsEnglish
                ? $"{model.LocalizedDisplayName} was downloaded, verified, started, and applied."
                : $"{model.DisplayName} 已下载、校验、启动并应用。", isError: false);
            _buttonDefaultEnabledStates[ManagedModelInstallButton] = false;
            FinishButtonOperation(ManagedModelInstallButton, "已安装并应用", success: true, isEnabledAfterResult: false);
        }
        catch (OperationCanceledException)
        {
            SetInlineStatus(ManagedModelStatusText, "操作已取消；已下载部分会保留，下次可断点续传。", "WarningTextBrush");
            SetGlobalStatus("模型操作已取消，可稍后继续。", isError: false);
            FinishButtonOperation(ManagedModelInstallButton, "已取消，可继续", success: false);
        }
        catch (Exception exception)
        {
            SetInlineStatus(ManagedModelStatusText, UiText.Error(exception), "DangerTextBrush");
            SetGlobalStatus(UiText.Error(exception), isError: true);
            FinishButtonOperation(ManagedModelInstallButton, "下载或配置失败", success: false);
        }
        finally
        {
            SetManagedModelBusy(false);
            _managedModelOperation?.Dispose();
            _managedModelOperation = null;
            await RefreshManagedModelStatusAsync(attemptConfiguredStart: false);
        }
    }

    private async void ManagedModelStartButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null || ManagedModelCombo.SelectedItem is not ManagedMultimodalModel model)
        {
            return;
        }

        _managedModelOperation?.Cancel();
        _managedModelOperation?.Dispose();
        _managedModelOperation = new CancellationTokenSource();
        BeginButtonOperation(ManagedModelStartButton, "正在启动…");
        SetManagedModelBusy(true);
        try
        {
            var progress = new Progress<ManagedModelProgress>(UpdateManagedModelProgress);
            await ApplyAndStartManagedModelAsync(model, progress, _managedModelOperation.Token);
            SetGlobalStatus(UiText.IsEnglish
                ? $"{model.LocalizedDisplayName} is running and selected for OCR and translation."
                : $"{model.DisplayName} 已启动并设为 OCR 与翻译模型。", isError: false);
            FinishButtonOperation(ManagedModelStartButton, "已启动并应用", success: true);
        }
        catch (OperationCanceledException)
        {
            SetInlineStatus(ManagedModelStatusText, "启动已取消。", "WarningTextBrush");
            FinishButtonOperation(ManagedModelStartButton, "已取消", success: false);
        }
        catch (Exception exception)
        {
            SetInlineStatus(ManagedModelStatusText, UiText.Error(exception), "DangerTextBrush");
            SetGlobalStatus(UiText.Error(exception), isError: true);
            FinishButtonOperation(ManagedModelStartButton, "启动失败", success: false);
        }
        finally
        {
            SetManagedModelBusy(false);
            _managedModelOperation?.Dispose();
            _managedModelOperation = null;
            await RefreshManagedModelStatusAsync(attemptConfiguredStart: false);
        }
    }

    private void ManagedModelCancelButton_OnClick(object? sender, RoutedEventArgs e) =>
        _managedModelOperation?.Cancel();

    private void ManagedModelFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null || ManagedModelCombo.SelectedItem is not ManagedMultimodalModel model)
        {
            return;
        }

        var directory = _services.ManagedModels.GetModelDirectory(model);
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
    }

    private void ManagedModelSourceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ManagedModelCombo.SelectedItem is ManagedMultimodalModel model)
        {
            Process.Start(new ProcessStartInfo { FileName = model.ModelScopePageUrl, UseShellExecute = true });
        }
    }

    private async Task ApplyAndStartManagedModelAsync(
        ManagedMultimodalModel model,
        IProgress<ManagedModelProgress> progress,
        CancellationToken cancellationToken)
    {
        if (_services is null)
        {
            return;
        }

        CustomEndpointBox.Text = AppSettings.ManagedModelEndpoint;
        CustomModelBox.Text = model.ModelAlias;
        SelectOcrProvider("local-vlm-corrected");
        SelectTranslationProvider("custom-chat");
        await _services.SaveSettingsAsync(BuildSettingsFromForm() with
        {
            OcrProviderId = "local-vlm-corrected",
            TranslationProviderId = "custom-chat",
            CustomTranslationEndpoint = AppSettings.ManagedModelEndpoint,
            CustomTranslationModel = model.ModelAlias,
            ManagedModelPackageId = model.Id,
            ManagedRuntimeBackend = SelectedManagedRuntimeBackendId,
            ManagedRuntimeEnabled = true
        }, cancellationToken);

        var startResult = await _services.ManagedModels.EnsureStartedAsync(
            model,
            SelectedManagedRuntimeBackendId,
            progress,
            cancellationToken);
        SetInlineStatus(
            ManagedModelStatusText,
            UiText.IsEnglish
                ? $"{UiText.T(startResult)}, validating image recognition and translation…"
                : $"{startResult}，正在验证图片识别与翻译…",
            "SecondaryTextBrush");
        await TestCustomVisionConnectionCoreAsync();
        await TestCustomTranslationConnectionCoreAsync();
    }

    private async Task RefreshManagedModelStatusAsync(bool attemptConfiguredStart)
    {
        if (_services is null || !ManagedModelExpander.IsVisible ||
            ManagedModelCombo.SelectedItem is not ManagedMultimodalModel selected)
        {
            return;
        }

        if (!_services.ManagedModels.HasBundledRuntime)
        {
            SetInlineStatus(ManagedModelStatusText, "完全版 llama.cpp 运行时缺失，请重新安装完全版。", "DangerTextBrush");
            ManagedModelInstallButton.IsEnabled = false;
            ManagedModelStartButton.IsEnabled = false;
            return;
        }

        SetInlineStatus(ManagedModelStatusText, "正在校验已下载模型…", "SecondaryTextBrush");
        try
        {
            var status = await _services.ManagedModels.GetStatusAsync(selected);
            SetInlineStatus(
                ManagedModelStatusText,
                status.Message,
                status.IsInstalled ? "SuccessTextBrush" : status.HasPartialDownload ? "WarningTextBrush" : "SecondaryTextBrush");
            ManagedModelInstallButton.IsEnabled = !status.IsInstalled;
            ManagedModelStartButton.IsEnabled = status.IsInstalled;

            if (attemptConfiguredStart && status.IsInstalled &&
                _services.Settings.ManagedRuntimeEnabled &&
                string.Equals(_services.Settings.ManagedModelPackageId, selected.Id, StringComparison.OrdinalIgnoreCase))
            {
                var progress = new Progress<ManagedModelProgress>(UpdateManagedModelProgress);
                var result = await _services.ManagedModels.EnsureStartedAsync(
                    selected,
                    _services.Settings.ManagedRuntimeBackend,
                    progress);
                SetInlineStatus(ManagedModelStatusText, result, "SuccessTextBrush");
            }
        }
        catch (Exception exception)
        {
            SetInlineStatus(ManagedModelStatusText, UiText.Error(exception), "DangerTextBrush");
        }
    }

    private void UpdateManagedModelDescription()
    {
        if (ManagedModelCombo.SelectedItem is not ManagedMultimodalModel model)
        {
            return;
        }

        ManagedModelSummaryText.Text = UiText.IsEnglish
            ? $"{model.LocalizedSummary} Quantization: {model.Quantization} · License: {model.License} · Released: {model.ReleaseDate}"
            : $"{model.Summary} 量化：{model.Quantization} · 许可：{model.License} · 发布：{model.ReleaseDate}";
        ManagedModelHardwareText.Text = model.LocalizedHardwareHint;
    }

    private string SelectedManagedRuntimeBackendId =>
        (ManagedRuntimeBackendCombo.SelectedItem as ManagedRuntimeBackend)?.Id ?? ManagedRuntimeBackends.Auto.Id;

    private void UpdateManagedRuntimeBackendDescription()
    {
        var backend = ManagedRuntimeBackendCombo.SelectedItem as ManagedRuntimeBackend ?? ManagedRuntimeBackends.Auto;
        ManagedRuntimeBackendHintText.Text = backend.LocalizedDescription;
    }

    private void UpdateManagedModelProgress(ManagedModelProgress progress)
    {
        ManagedModelProgressBar.IsVisible = true;
        ManagedModelProgressBar.IsIndeterminate = progress.IsIndeterminate;
        if (!progress.IsIndeterminate)
        {
            ManagedModelProgressBar.Value = progress.Percentage;
        }

        var transferred = progress.TotalBytes > 0
            ? $" · {FormatGiB(progress.BytesCompleted)}/{FormatGiB(progress.TotalBytes)}"
            : string.Empty;
        SetInlineStatus(ManagedModelStatusText, UiText.T(progress.Message) + transferred, "SecondaryTextBrush");
    }

    private void SetManagedModelBusy(bool isBusy)
    {
        ManagedModelCombo.IsEnabled = !isBusy;
        ManagedRuntimeBackendCombo.IsEnabled = !isBusy;
        ManagedModelSourceButton.IsEnabled = !isBusy;
        ManagedModelFolderButton.IsEnabled = !isBusy;
        ManagedModelInstallButton.IsEnabled = !isBusy;
        ManagedModelStartButton.IsEnabled = !isBusy;
        ManagedModelCancelButton.IsVisible = isBusy;
        ManagedModelCancelButton.IsEnabled = isBusy;
        ManagedModelProgressBar.IsVisible = isBusy;
        if (!isBusy)
        {
            ManagedModelProgressBar.IsIndeterminate = false;
        }
    }

    private static string FormatGiB(long bytes) => $"{bytes / 1024d / 1024d / 1024d:0.00} GiB";

    private async void InstallOcrModelsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null)
        {
            return;
        }

        var button = sender as Button;
        BeginButtonOperation(button, "正在下载…");
        SetInlineStatus(OcrModelStatusText, "OCR 模型：正在下载并校验…", "SecondaryTextBrush");
        SetGlobalStatus("正在下载中英离线 OCR 模型，请勿退出…", isError: false);
        try
        {
            await _services.PaddleProvider.InstallModelsAsync();
            await RefreshLocalModelStatusAsync();
            SetGlobalStatus("中英离线 OCR 模型安装完成并已校验。", isError: false);
            FinishButtonOperation(button, "下载完成", success: true, isEnabledAfterResult: false);
        }
        catch (Exception exception)
        {
            SetInlineStatus(OcrModelStatusText, $"OCR 模型：{UiText.Error(exception)}", "DangerTextBrush");
            SetGlobalStatus(UiText.Error(exception), isError: true);
            FinishButtonOperation(button, "下载失败", success: false);
        }
    }

    private async void InstallTranslationModelsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null)
        {
            return;
        }

        var button = sender as Button;
        BeginButtonOperation(button, "正在下载…");
        SetInlineStatus(TranslationModelStatusText, "翻译模型：正在下载并校验…", "SecondaryTextBrush");
        SetGlobalStatus("正在下载中英离线翻译模型，请勿退出…", isError: false);
        try
        {
            await _services.ArgosProvider.InstallModelsAsync();
            await RefreshLocalModelStatusAsync();
            SetGlobalStatus("中英离线翻译模型安装完成并已校验。", isError: false);
            FinishButtonOperation(button, "下载完成", success: true, isEnabledAfterResult: false);
        }
        catch (Exception exception)
        {
            SetInlineStatus(TranslationModelStatusText, $"翻译模型：{UiText.Error(exception)}", "DangerTextBrush");
            SetGlobalStatus(UiText.Error(exception), isError: true);
            FinishButtonOperation(button, "下载失败", success: false);
        }
    }

    private async void DeleteModelsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now > _deleteConfirmationExpiresAt)
        {
            _deleteConfirmationExpiresAt = now.AddSeconds(8);
            DeleteModelsButton.Content = "确认清理下载模型";
            SetGlobalStatus("再次点击可清理用户下载的翻译模型；安装包内离线基础模型、凭据和设置都会保留。", isError: false);
            _ = ResetDeleteConfirmationAsync(_deleteConfirmationExpiresAt);
            return;
        }

        try
        {
            DeleteModelsButton.IsEnabled = false;
            await _services.ClearDownloadedTranslationModelsAsync();
            await RefreshLocalModelStatusAsync();
            SetGlobalStatus("用户下载模型已清理，安装包内离线基础模型仍可使用。", isError: false);
        }
        catch (Exception exception)
        {
            SetGlobalStatus(UiText.Error(exception), isError: true);
        }
        finally
        {
            DeleteModelsButton.IsEnabled = true;
            _deleteConfirmationExpiresAt = default;
            DeleteModelsButton.Content = _deleteModelsDefaultContent;
        }
    }

    private async Task ResetDeleteConfirmationAsync(DateTimeOffset expiresAt)
    {
        await Task.Delay(TimeSpan.FromSeconds(8));
        if (_deleteConfirmationExpiresAt == expiresAt && DateTimeOffset.UtcNow >= expiresAt)
        {
            _deleteConfirmationExpiresAt = default;
            DeleteModelsButton.Content = _deleteModelsDefaultContent;
            SetGlobalStatus("删除操作已取消。", isError: false);
        }
    }

    private async Task RefreshLocalModelStatusAsync()
    {
        if (_services is null)
        {
            return;
        }

        SetInlineStatus(OcrModelStatusText, "OCR 模型：正在检查…", "SecondaryTextBrush");
        SetInlineStatus(TranslationModelStatusText, "翻译模型：正在检查…", "SecondaryTextBrush");

        var ocrAvailability = await _services.PaddleProvider.GetAvailabilityAsync();
        SetInlineStatus(
            OcrModelStatusText,
            ocrAvailability.IsAvailable
                ? "OCR 模型：已安装，可离线使用"
                : $"OCR 模型：{ocrAvailability.Message ?? "不可用"}",
            ocrAvailability.IsAvailable ? "SuccessTextBrush" : "WarningTextBrush");
        SetModelInstallButtonState(
            InstallOcrModelsButton,
            OcrModelButtonText,
            OcrModelDownloadIcon,
            OcrModelInstalledIcon,
            ocrAvailability.IsAvailable,
            "下载中英 OCR 模型",
            "中英 OCR 模型已安装");

        var translationAvailability = await _services.ArgosProvider.GetAvailabilityAsync();
        SetInlineStatus(
            TranslationModelStatusText,
            translationAvailability.IsAvailable
                ? "翻译模型：已安装，可离线使用"
                : $"翻译模型：{translationAvailability.Message ?? "不可用"}",
            translationAvailability.IsAvailable ? "SuccessTextBrush" : "WarningTextBrush");
        SetModelInstallButtonState(
            InstallTranslationModelsButton,
            TranslationModelButtonText,
            TranslationModelDownloadIcon,
            TranslationModelInstalledIcon,
            translationAvailability.IsAvailable,
            "下载中英翻译模型",
            "中英翻译模型已安装");
    }

}
