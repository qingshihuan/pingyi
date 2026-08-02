using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Diagnostics;
using PingYi.Core;
using PingYi.Infrastructure;
using SkiaSharp;

namespace PingYi.App;

public partial class MainWindow : Window, IMainWindowShell
{
    private readonly AppServices? _services;
    private readonly CaptureCoordinator? _captureCoordinator;
    private readonly bool _settingsMode;
    private readonly Dictionary<Button, object?> _buttonDefaultContents = [];
    private readonly Dictionary<Button, bool> _buttonDefaultEnabledStates = [];
    private readonly Dictionary<Button, CancellationTokenSource> _buttonFeedbackResetTokens = [];
    private readonly Dictionary<string, SecretFieldState> _secretFields = new(StringComparer.Ordinal);
    private DateTimeOffset _deleteConfirmationExpiresAt;
    private object? _deleteModelsDefaultContent;
    private bool _isLoadingSettings;
    private CancellationTokenSource? _managedModelOperation;

    public MainWindow()
    {
        InitializeComponent();
        _deleteModelsDefaultContent = DeleteModelsButton.Content;
        RegisterSecretFields();
    }

    public MainWindow(
        AppServices services,
        CaptureCoordinator captureCoordinator,
        bool settingsMode = false) : this()
    {
        _services = services;
        _captureCoordinator = captureCoordinator;
        _settingsMode = settingsMode;
        ConfigureWindowMode();
        LoadSettings();
        Opened += async (_, _) =>
        {
            await RefreshCredentialStatusAsync();
            await RefreshLocalModelStatusAsync();
            await RefreshManagedModelStatusAsync(attemptConfiguredStart: true);
        };
    }

    public void SetGlobalStatus(string message, bool isError)
    {
        GlobalStatusText.Text = message;
        GlobalStatusText.Foreground = isError
            ? Application.Current?.FindResource("DangerTextBrush") as IBrush
            : Application.Current?.FindResource("SecondaryTextBrush") as IBrush;
        StatusIndicator.Background = Application.Current?.FindResource(
            isError ? "DangerBrush" : "BrandBrush") as IBrush;
        GlobalStatusBorder.Background = Application.Current?.FindResource(
            isError ? "WarningBackgroundBrush" : "SubtleBackgroundBrush") as IBrush;
    }

    private void LoadSettings()
    {
        if (_services is null)
        {
            return;
        }

        _isLoadingSettings = true;
        try
        {
            var settings = _services.Settings;
            OcrProviderCombo.ItemsSource = _services.Providers.OcrProviders
                .Select(provider => new ProviderChoice(provider.Metadata.Id, provider.Metadata.DisplayName))
                .ToArray();
            TranslationProviderCombo.ItemsSource = _services.Providers.TranslationProviders
                .Select(provider => new ProviderChoice(provider.Metadata.Id, provider.Metadata.DisplayName))
                .ToArray();
            OcrProviderCombo.SelectedItem = ((IEnumerable<ProviderChoice>)OcrProviderCombo.ItemsSource)
                .FirstOrDefault(choice => choice.Id == settings.OcrProviderId);
            TranslationProviderCombo.SelectedItem = ((IEnumerable<ProviderChoice>)TranslationProviderCombo.ItemsSource)
                .FirstOrDefault(choice => choice.Id == settings.TranslationProviderId);
            LoadTargetLanguageChoices(settings.TranslationProviderId, settings.TargetLanguage);
            LocalServicePresetCombo.ItemsSource = LocalLlmPresets.All;
            LocalServicePresetCombo.SelectedItem =
                LocalLlmPresets.MatchEndpoint(settings.CustomTranslationEndpoint) ?? LocalLlmPresets.Default;
            CustomEndpointBox.Text = settings.CustomTranslationEndpoint;
            CustomModelBox.Text = settings.CustomTranslationModel;
            InterfaceStyleCombo.ItemsSource = UiStyleChoice.All;
            InterfaceStyleCombo.SelectedItem = UiStyleChoice.All
                .First(choice => choice.Id == settings.InterfaceStyle);
            HotkeyBox.Text = settings.Hotkey;
            StartMinimizedCheckBox.IsChecked = settings.StartMinimized;
            CheckForUpdatesCheckBox.IsChecked = settings.CheckForUpdates;
            ManagedModelExpander.IsVisible = _services.ManagedModels.IsCompleteEdition;
            if (ManagedModelExpander.IsVisible)
            {
                ManagedModelCombo.ItemsSource = ManagedMultimodalModels.All;
                ManagedModelCombo.SelectedItem = ManagedMultimodalModels.TryGet(
                    settings.ManagedModelPackageId,
                    out var managedModel)
                    ? managedModel
                    : ManagedMultimodalModels.Recommended;
                ManagedRuntimeBackendCombo.ItemsSource = ManagedRuntimeBackends.All;
                ManagedRuntimeBackendCombo.SelectedItem = ManagedRuntimeBackends.Get(settings.ManagedRuntimeBackend);
                UpdateManagedModelDescription();
                UpdateManagedRuntimeBackendDescription();
            }
        }
        finally
        {
            _isLoadingSettings = false;
        }

        SetGlobalStatus($"就绪。按 {_services.Settings.Hotkey} 或点击“开始截图”。", isError: false);
    }

    private async void CaptureButton_OnClick(object? sender, RoutedEventArgs e)
    {
        CaptureButton.IsEnabled = false;
        try
        {
            if (_captureCoordinator is not null)
            {
                await ApplyProviderSelectionAsync(showStatus: false);
                await _captureCoordinator.StartCaptureAsync(this);
            }
        }
        finally
        {
            CaptureButton.IsEnabled = true;
        }
    }

    private async void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null)
        {
            return;
        }

        var button = sender as Button;
        BeginButtonOperation(button, "正在保存…");
        SetGlobalStatus("正在保存设置与安全凭据…", isError: false);
        try
        {
            var previousSettings = _services.Settings;
            var updatedSettings = BuildSettingsFromForm();
            _ = GlobalHotkeyGesture.Parse(updatedSettings.Hotkey);
            await SaveAllEnteredSecretsAsync();
            var hotkeyChanged = !string.Equals(
                previousSettings.Hotkey,
                updatedSettings.Hotkey,
                StringComparison.OrdinalIgnoreCase);
            if (hotkeyChanged)
            {
                await SwitchHotkeyAsync(previousSettings.Hotkey, updatedSettings.Hotkey);
            }

            try
            {
                await _services.SaveSettingsAsync(updatedSettings);
            }
            catch
            {
                if (hotkeyChanged)
                {
                    await SwitchHotkeyAsync(updatedSettings.Hotkey, previousSettings.Hotkey);
                }

                throw;
            }
            ClearSecretInputs();
            await RefreshCredentialStatusAsync();
            var secretStatus = _services.SecretStore is PlatformSecretStore { IsPersistent: false }
                ? "Linux 密钥服务不可用，凭据仅保存到本次运行结束。"
                : "敏感凭据已写入系统安全存储。";
            SetGlobalStatus($"设置已保存。{secretStatus}", isError: false);
            FinishButtonOperation(button, "已保存并应用", success: true);
        }
        catch (Exception exception)
        {
            SetGlobalStatus(exception.Message, isError: true);
            FinishButtonOperation(button, "保存失败", success: false);
        }
    }

    private async Task SwitchHotkeyAsync(string previousHotkey, string nextHotkey)
    {
        if (_services is null)
        {
            return;
        }

        await _services.HotkeyService.StopAsync();
        try
        {
            await _services.HotkeyService.StartAsync(nextHotkey);
        }
        catch
        {
            await _services.HotkeyService.StopAsync();
            await _services.HotkeyService.StartAsync(previousHotkey);
            throw;
        }
    }

    private async void ProviderCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || _services is null ||
            OcrProviderCombo.SelectedItem is not ProviderChoice ||
            TranslationProviderCombo.SelectedItem is not ProviderChoice)
        {
            return;
        }

        try
        {
            var translation = (ProviderChoice)TranslationProviderCombo.SelectedItem;
            _isLoadingSettings = true;
            try
            {
                LoadTargetLanguageChoices(translation.Id, _services.Settings.TargetLanguage);
            }
            finally
            {
                _isLoadingSettings = false;
            }
            await ApplyProviderSelectionAsync(showStatus: true);
        }
        catch (Exception exception)
        {
            SetGlobalStatus(exception.Message, isError: true);
        }
    }

    private async void TargetLanguageCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || _services is null ||
            TargetLanguageCombo.SelectedItem is not LanguageChoice language)
        {
            return;
        }

        try
        {
            await _services.SaveSettingsAsync(_services.Settings with { TargetLanguage = language.Code });
            SetGlobalStatus($"目标语言已切换为：{language.Name}。", isError: false);
        }
        catch (Exception exception)
        {
            SetGlobalStatus(exception.Message, isError: true);
        }
    }

    private async void CheckStatusButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null)
        {
            return;
        }

        var button = sender as Button;
        BeginButtonOperation(button, "正在检查…");
        SetGlobalStatus("正在检查引擎…", isError: false);
        try
        {
            var ocr = _services.Providers.GetOcrProvider(
                (OcrProviderCombo.SelectedItem as ProviderChoice)?.Id ?? _services.Settings.OcrProviderId);
            var translation = _services.Providers.GetTranslationProvider(
                (TranslationProviderCombo.SelectedItem as ProviderChoice)?.Id ?? _services.Settings.TranslationProviderId);
            var ocrStatus = await ProbeOcrProviderAsync(ocr);
            var translationStatus = await ProbeTranslationProviderAsync(translation);
            var ready = ocrStatus.IsAvailable && translationStatus.IsAvailable;
            SetGlobalStatus(
                ready
                    ? $"{ocr.Metadata.DisplayName}、{translation.Metadata.DisplayName}均可用。"
                    : $"OCR：{ocrStatus.Message ?? "可用"}  翻译：{translationStatus.Message ?? "可用"}",
                isError: !ready);
            FinishButtonOperation(button, ready ? "状态正常" : "检查未通过", success: ready);
        }
        catch (Exception exception)
        {
            SetGlobalStatus(exception.Message, isError: true);
            FinishButtonOperation(button, "检查失败", success: false);
        }
    }

    private async void ValidateBaiduCredentialsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null)
        {
            return;
        }

        var button = sender as Button;
        BeginButtonOperation(button, "正在验证…");
        SetGlobalStatus("正在安全保存并验证百度凭据…", isError: false);
        try
        {
            await SaveBaiduSecretInputsAsync();
            HideSecretFields(
                SecretKeys.BaiduOcrApiKey,
                SecretKeys.BaiduOcrSecretKey,
                SecretKeys.BaiduTranslateAppId,
                SecretKeys.BaiduTranslateSecret);

            var ocrStatus = await _services.BaiduOcrProvider.GetAvailabilityAsync();
            var translationStatus = await _services.BaiduTranslationProvider.GetAvailabilityAsync();
            var anyConfigured = ocrStatus.IsAvailable || translationStatus.IsAvailable;
            var allValid = true;

            if (ocrStatus.IsAvailable)
            {
                try
                {
                    await _services.BaiduOcrProvider.ValidateCredentialsAsync();
                    SetInlineStatus(BaiduOcrCredentialStatusText, "OCR 凭据：验证通过", "SuccessTextBrush");
                }
                catch (Exception exception)
                {
                    allValid = false;
                    SetInlineStatus(BaiduOcrCredentialStatusText, $"OCR 凭据：{exception.Message}", "DangerTextBrush");
                }
            }
            else
            {
                SetInlineStatus(BaiduOcrCredentialStatusText, "OCR 凭据：未完整配置，未发送验证", "WarningTextBrush");
            }

            if (translationStatus.IsAvailable)
            {
                try
                {
                    await _services.BaiduTranslationProvider.ValidateCredentialsAsync();
                    SetInlineStatus(BaiduTranslationCredentialStatusText, "翻译凭据：验证通过", "SuccessTextBrush");
                }
                catch (Exception exception)
                {
                    allValid = false;
                    SetInlineStatus(BaiduTranslationCredentialStatusText, $"翻译凭据：{exception.Message}", "DangerTextBrush");
                }
            }
            else
            {
                SetInlineStatus(BaiduTranslationCredentialStatusText, "翻译凭据：未完整配置，未发送验证", "WarningTextBrush");
            }

            SetGlobalStatus(
                !anyConfigured
                    ? "尚未填写完整的百度 OCR 或翻译凭据。"
                    : allValid
                        ? "已配置的百度凭据均验证通过。"
                        : "部分百度凭据验证失败，请查看字段下方提示。",
                isError: !anyConfigured || !allValid);
            FinishButtonOperation(
                button,
                anyConfigured && allValid ? "验证通过" : "验证未通过",
                success: anyConfigured && allValid);
        }
        catch (Exception exception)
        {
            SetGlobalStatus(exception.Message, isError: true);
            FinishButtonOperation(button, "验证失败", success: false);
        }
    }

    private async void UseLocalLlamaPresetButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null)
        {
            return;
        }

        var button = sender as Button;
        var presetSaved = false;
        BeginButtonOperation(button, "正在应用并测试…");
        try
        {
            var preset = LocalServicePresetCombo.SelectedItem as LocalLlmPreset ?? LocalLlmPresets.Default;
            CustomEndpointBox.Text = preset.ChatCompletionsEndpoint;
            CustomModelBox.Text = preset.SuggestedModel;
            SelectTranslationProvider("custom-chat");
            await _services.SaveSettingsAsync(BuildSettingsFromForm());
            presetSaved = true;
            SetInlineStatus(CustomTranslationStatusText, $"{preset.DisplayName} 预设已保存，正在发现可用模型…", "SecondaryTextBrush");
            SetGlobalStatus($"{preset.DisplayName} 配置已应用，正在检查服务…", isError: false);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                var models = await _services.CustomTranslationProvider.GetAvailableModelsAsync(timeout.Token);
                if (models.Count > 0)
                {
                    CustomModelBox.Text = models[0];
                    await _services.SaveSettingsAsync(BuildSettingsFromForm());
                }
            }
            catch
            {
                // The connection test below provides the actionable provider-neutral error.
            }
            await TestCustomTranslationConnectionCoreAsync();
            FinishButtonOperation(button, "预设已应用", success: true);
        }
        catch (Exception exception)
        {
            SetInlineStatus(CustomTranslationStatusText, exception.Message, "DangerTextBrush");
            SetGlobalStatus(exception.Message, isError: true);
            FinishButtonOperation(button, presetSaved ? "已应用，连接失败" : "应用失败", success: false);
        }
    }

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
        SetGlobalStatus($"正在从魔搭下载 {model.DisplayName}…", isError: false);
        try
        {
            var progress = new Progress<ManagedModelProgress>(UpdateManagedModelProgress);
            await _services.ManagedModels.DownloadAsync(model, progress, _managedModelOperation.Token);
            await ApplyAndStartManagedModelAsync(model, progress, _managedModelOperation.Token);
            SetGlobalStatus($"{model.DisplayName} 已下载、校验、启动并应用。", isError: false);
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
            SetInlineStatus(ManagedModelStatusText, exception.Message, "DangerTextBrush");
            SetGlobalStatus(exception.Message, isError: true);
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
            SetGlobalStatus($"{model.DisplayName} 已启动并设为 OCR 与翻译模型。", isError: false);
            FinishButtonOperation(ManagedModelStartButton, "已启动并应用", success: true);
        }
        catch (OperationCanceledException)
        {
            SetInlineStatus(ManagedModelStatusText, "启动已取消。", "WarningTextBrush");
            FinishButtonOperation(ManagedModelStartButton, "已取消", success: false);
        }
        catch (Exception exception)
        {
            SetInlineStatus(ManagedModelStatusText, exception.Message, "DangerTextBrush");
            SetGlobalStatus(exception.Message, isError: true);
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
        SetInlineStatus(ManagedModelStatusText, $"{startResult}，正在验证图片识别与翻译…", "SecondaryTextBrush");
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
            SetInlineStatus(ManagedModelStatusText, exception.Message, "DangerTextBrush");
        }
    }

    private void UpdateManagedModelDescription()
    {
        if (ManagedModelCombo.SelectedItem is not ManagedMultimodalModel model)
        {
            return;
        }

        ManagedModelSummaryText.Text = $"{model.Summary} 量化：{model.Quantization} · 许可：{model.License} · 发布：{model.ReleaseDate}";
        ManagedModelHardwareText.Text = model.HardwareHint;
    }

    private string SelectedManagedRuntimeBackendId =>
        (ManagedRuntimeBackendCombo.SelectedItem as ManagedRuntimeBackend)?.Id ?? ManagedRuntimeBackends.Auto.Id;

    private void UpdateManagedRuntimeBackendDescription()
    {
        var backend = ManagedRuntimeBackendCombo.SelectedItem as ManagedRuntimeBackend ?? ManagedRuntimeBackends.Auto;
        ManagedRuntimeBackendHintText.Text = backend.Description;
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
        SetInlineStatus(ManagedModelStatusText, progress.Message + transferred, "SecondaryTextBrush");
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

    private async void TestCustomTranslationButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null)
        {
            return;
        }

        var button = sender as Button;
        BeginButtonOperation(button, "正在测试…");
        try
        {
            await TestCustomTranslationConnectionCoreAsync();
            FinishButtonOperation(button, "连接成功", success: true);
        }
        catch (Exception exception)
        {
            SetInlineStatus(CustomTranslationStatusText, exception.Message, "DangerTextBrush");
            SetGlobalStatus(exception.Message, isError: true);
            FinishButtonOperation(button, "连接失败", success: false);
        }
    }

    private async void TestCustomVisionButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null)
        {
            return;
        }

        var button = sender as Button;
        BeginButtonOperation(button, "正在测试…");
        try
        {
            await TestCustomVisionConnectionCoreAsync();
            FinishButtonOperation(button, "图片可用", success: true);
        }
        catch (Exception exception)
        {
            SetInlineStatus(CustomTranslationStatusText, exception.Message, "DangerTextBrush");
            SetGlobalStatus(exception.Message, isError: true);
            FinishButtonOperation(button, "图片不可用", success: false);
        }
    }

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
            SetInlineStatus(OcrModelStatusText, $"OCR 模型：{exception.Message}", "DangerTextBrush");
            SetGlobalStatus(exception.Message, isError: true);
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
            SetInlineStatus(TranslationModelStatusText, $"翻译模型：{exception.Message}", "DangerTextBrush");
            SetGlobalStatus(exception.Message, isError: true);
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
            SetGlobalStatus(exception.Message, isError: true);
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

    private AppSettings BuildSettingsFromForm()
    {
        var current = _services?.Settings ?? new AppSettings();
        var endpoint = AppSettings.NormalizeChatCompletionsEndpoint(CustomEndpointBox.Text);
        var modelName = CustomModelBox.Text?.Trim() ?? string.Empty;
        var keepManagedRuntime = current.ManagedRuntimeEnabled &&
                                 ManagedMultimodalModels.TryGet(current.ManagedModelPackageId, out var managedModel) &&
                                 string.Equals(endpoint, AppSettings.ManagedModelEndpoint, StringComparison.OrdinalIgnoreCase) &&
                                 string.Equals(modelName, managedModel.ModelAlias, StringComparison.OrdinalIgnoreCase);
        return current with
        {
            OcrProviderId = (OcrProviderCombo.SelectedItem as ProviderChoice)?.Id ?? "local-paddle",
            TranslationProviderId = (TranslationProviderCombo.SelectedItem as ProviderChoice)?.Id ?? "local-argos",
            TargetLanguage = (TargetLanguageCombo.SelectedItem as LanguageChoice)?.Code ?? LanguageCatalog.AutoOpposite,
            CustomTranslationEndpoint = endpoint,
            CustomTranslationModel = modelName,
            ManagedRuntimeEnabled = keepManagedRuntime,
            ManagedRuntimeBackend = SelectedManagedRuntimeBackendId,
            Hotkey = HotkeyBox.Text ?? AppSettings.DefaultHotkey,
            StartMinimized = StartMinimizedCheckBox.IsChecked == true,
            CheckForUpdates = CheckForUpdatesCheckBox.IsChecked != false,
            InterfaceStyle = (InterfaceStyleCombo.SelectedItem as UiStyleChoice)?.Id ?? "modern"
        };
    }

    private void ConfigureWindowMode()
    {
        Title = _settingsMode ? $"{AppEdition.ProductName}设置" : AppEdition.ProductName;
        if (_settingsMode)
        {
            Title = $"{AppEdition.ProductName}设置";
            WindowHeadingText.Text = "设置";
            WindowSubtitleText.Text = "处理、模型、服务、快捷键与外观";
            CaptureHero.IsVisible = false;
            OpenClassicInterfaceButton.IsEnabled = true;
            OpenClassicInterfaceButton.Content = "打开经典界面";
            return;
        }

        OpenClassicInterfaceButton.IsEnabled = false;
        OpenClassicInterfaceButton.Content = "当前为经典界面";
    }

    private async void OpenClassicInterfaceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null || _captureCoordinator is null || !_settingsMode)
        {
            return;
        }

        var classicWindow = new MainWindow(_services, _captureCoordinator);
        await classicWindow.ShowDialog(this);
        LoadSettings();
        await RefreshCredentialStatusAsync();
        await RefreshLocalModelStatusAsync();
    }

    private async Task ApplyProviderSelectionAsync(bool showStatus)
    {
        if (_services is null)
        {
            return;
        }

        var ocr = OcrProviderCombo.SelectedItem as ProviderChoice;
        var translation = TranslationProviderCombo.SelectedItem as ProviderChoice;
        if (ocr is null || translation is null)
        {
            return;
        }

        await _services.SaveSettingsAsync(_services.Settings with
        {
            OcrProviderId = ocr.Id,
            TranslationProviderId = translation.Id,
            TargetLanguage = (TargetLanguageCombo.SelectedItem as LanguageChoice)?.Code ?? LanguageCatalog.AutoOpposite
        });
        if (showStatus)
        {
            SetGlobalStatus($"已应用：{ocr.Name} + {translation.Name}。", isError: false);
        }
    }

    private static async Task<ProviderAvailability> ProbeOcrProviderAsync(IOcrProvider provider)
    {
        try
        {
            var availability = await provider.GetAvailabilityAsync();
            if (availability.IsAvailable && provider is BaiduOcrProvider baidu)
            {
                await baidu.ValidateCredentialsAsync();
            }

            return availability;
        }
        catch (Exception exception)
        {
            return new ProviderAvailability(false, exception.Message);
        }
    }

    private static async Task<ProviderAvailability> ProbeTranslationProviderAsync(ITranslationProvider provider)
    {
        try
        {
            var availability = await provider.GetAvailabilityAsync();
            if (availability.IsAvailable && provider is BaiduTranslationProvider baidu)
            {
                await baidu.ValidateCredentialsAsync();
            }

            return availability;
        }
        catch (Exception exception)
        {
            return new ProviderAvailability(false, exception.Message);
        }
    }

    private async Task TestCustomTranslationConnectionCoreAsync()
    {
        if (_services is null)
        {
            return;
        }

        SetInlineStatus(CustomTranslationStatusText, "正在连接并发送固定测试文本…", "SecondaryTextBrush");
        await PersistSecretFieldAsync(SecretKeys.CustomTranslationApiKey);
        SelectTranslationProvider("custom-chat");
        await _services.SaveSettingsAsync(BuildSettingsFromForm());
        HideSecretFields(SecretKeys.CustomTranslationApiKey);

        var availability = await _services.CustomTranslationProvider.GetAvailabilityAsync();
        if (!availability.IsAvailable)
        {
            throw new ProviderException("custom_unavailable", availability.Message ?? "大模型服务不可用。");
        }

        var result = await _services.CustomTranslationProvider.TranslateAsync(
            new TranslationRequest("Hello", "en", "zh"));
        if (string.IsNullOrWhiteSpace(result.Text))
        {
            throw new ProviderException("custom_empty", "服务已连接，但没有返回测试译文。");
        }

        SetInlineStatus(CustomTranslationStatusText, "连接、模型名与翻译请求均验证通过", "SuccessTextBrush");
        SetGlobalStatus("本地 / 自定义大模型翻译已可用。", isError: false);
    }

    private async Task TestCustomVisionConnectionCoreAsync()
    {
        if (_services is null)
        {
            return;
        }

        SetInlineStatus(CustomTranslationStatusText, "正在发送固定合成图片测试多模态能力…", "SecondaryTextBrush");
        await PersistSecretFieldAsync(SecretKeys.CustomTranslationApiKey);
        await _services.SaveSettingsAsync(BuildSettingsFromForm());
        HideSecretFields(SecretKeys.CustomTranslationApiKey);

        var availability = await _services.LocalVlmOcrProvider.GetAvailabilityAsync();
        if (!availability.IsAvailable)
        {
            throw new ProviderException("custom_vision_unavailable", availability.Message ?? "多模态模型服务不可用。");
        }

        var result = await _services.LocalVlmOcrProvider.RecognizeAsync(
            CreateVisionTestImage(),
            new OcrOptions("en"));
        if (!result.PlainText.Contains("PINGYI", StringComparison.OrdinalIgnoreCase) ||
            !result.PlainText.Contains("2026", StringComparison.Ordinal))
        {
            throw new ProviderException(
                "custom_vision_mismatch",
                "服务可以接收图片，但未正确读出固定测试文字；请确认模型支持视觉并已加载 mmproj。"
            );
        }

        SetInlineStatus(CustomTranslationStatusText, "连接、模型名与多模态图片识别均验证通过", "SuccessTextBrush");
        SetGlobalStatus("本机 / 自定义大模型图片识别已可用。", isError: false);
    }

    private static ImageFrame CreateVisionTestImage()
    {
        const int width = 360;
        const int height = 96;
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(12, 20, 32));
        using var typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold);
        using var font = new SKFont(typeface, 28);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText("PINGYI OCR 2026", 22, 58, SKTextAlign.Left, font, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new ImageFrame(data.ToArray(), width, height, new PingYi.Core.PixelRect(0, 0, width, height));
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

    private async Task RefreshCredentialStatusAsync()
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            var ocrApiKey = await _services.SecretStore.GetAsync(SecretKeys.BaiduOcrApiKey);
            var ocrSecret = await _services.SecretStore.GetAsync(SecretKeys.BaiduOcrSecretKey);
            var translationAppId = await _services.SecretStore.GetAsync(SecretKeys.BaiduTranslateAppId);
            var translationSecret = await _services.SecretStore.GetAsync(SecretKeys.BaiduTranslateSecret);
            var customApiKey = await _services.SecretStore.GetAsync(SecretKeys.CustomTranslationApiKey);
            SetSecretFieldValue(SecretKeys.BaiduOcrApiKey, ocrApiKey);
            SetSecretFieldValue(SecretKeys.BaiduOcrSecretKey, ocrSecret);
            SetSecretFieldValue(SecretKeys.BaiduTranslateAppId, translationAppId);
            SetSecretFieldValue(SecretKeys.BaiduTranslateSecret, translationSecret);
            SetSecretFieldValue(SecretKeys.CustomTranslationApiKey, customApiKey);
            var ocrReady = !string.IsNullOrWhiteSpace(ocrApiKey) && !string.IsNullOrWhiteSpace(ocrSecret);
            var translationReady = !string.IsNullOrWhiteSpace(translationAppId) && !string.IsNullOrWhiteSpace(translationSecret);

            SetInlineStatus(
                BaiduOcrCredentialStatusText,
                ocrReady ? "OCR 凭据：已安全保存" : "OCR 凭据：未配置或缺少一项",
                ocrReady ? "SuccessTextBrush" : "WarningTextBrush");
            SetInlineStatus(
                BaiduTranslationCredentialStatusText,
                translationReady ? "翻译凭据：已安全保存" : "翻译凭据：未配置或缺少一项",
                translationReady ? "SuccessTextBrush" : "WarningTextBrush");
        }
        catch (Exception exception)
        {
            SetInlineStatus(BaiduOcrCredentialStatusText, $"读取凭据失败：{exception.Message}", "DangerTextBrush");
            SetInlineStatus(BaiduTranslationCredentialStatusText, "翻译凭据状态未知", "DangerTextBrush");
        }
    }

    private async Task SaveAllEnteredSecretsAsync()
    {
        await SaveBaiduSecretInputsAsync();
        await PersistSecretFieldAsync(SecretKeys.CustomTranslationApiKey);
    }

    private async Task SaveBaiduSecretInputsAsync()
    {
        await PersistSecretFieldAsync(SecretKeys.BaiduOcrApiKey);
        await PersistSecretFieldAsync(SecretKeys.BaiduOcrSecretKey);
        await PersistSecretFieldAsync(SecretKeys.BaiduTranslateAppId);
        await PersistSecretFieldAsync(SecretKeys.BaiduTranslateSecret);
    }

    private void SelectTranslationProvider(string providerId)
    {
        if (TranslationProviderCombo.ItemsSource is not IEnumerable<ProviderChoice> choices)
        {
            return;
        }

        _isLoadingSettings = true;
        try
        {
            TranslationProviderCombo.SelectedItem = choices.FirstOrDefault(choice => choice.Id == providerId);
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    private void SelectOcrProvider(string providerId)
    {
        if (OcrProviderCombo.ItemsSource is not IEnumerable<ProviderChoice> choices)
        {
            return;
        }

        _isLoadingSettings = true;
        try
        {
            OcrProviderCombo.SelectedItem = choices.FirstOrDefault(choice => choice.Id == providerId);
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    private static void SetInlineStatus(TextBlock textBlock, string message, string brushKey)
    {
        textBlock.Text = message;
        textBlock.Foreground = Application.Current?.FindResource(brushKey) as IBrush;
    }

    private void BeginButtonOperation(Button? button, string message)
    {
        if (button is null)
        {
            return;
        }

        if (!_buttonDefaultContents.ContainsKey(button))
        {
            _buttonDefaultContents[button] = button.Content;
        }

        if (!_buttonDefaultEnabledStates.ContainsKey(button))
        {
            _buttonDefaultEnabledStates[button] = button.IsEnabled;
        }

        CancelButtonFeedbackReset(button);
        SetButtonFeedbackClass(button, "feedback-loading");
        button.Content = message;
        button.IsEnabled = false;
    }

    private void FinishButtonOperation(
        Button? button,
        string message,
        bool success,
        bool isEnabledAfterResult = true)
    {
        if (button is null)
        {
            return;
        }

        CancelButtonFeedbackReset(button);
        SetButtonFeedbackClass(button, success ? "feedback-success" : "feedback-error");
        button.Content = message;
        button.IsEnabled = isEnabledAfterResult;

        var cancellation = new CancellationTokenSource();
        _buttonFeedbackResetTokens[button] = cancellation;
        _ = ResetButtonFeedbackAsync(button, cancellation);
    }

    private async Task ResetButtonFeedbackAsync(Button button, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), cancellation.Token);
            if (!cancellation.IsCancellationRequested)
            {
                SetButtonFeedbackClass(button, className: null);
                if (_buttonDefaultContents.TryGetValue(button, out var defaultContent))
                {
                    button.Content = defaultContent;
                }

                if (_buttonDefaultEnabledStates.TryGetValue(button, out var isEnabled))
                {
                    button.IsEnabled = isEnabled;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A new operation replaced this button's previous feedback.
        }
        finally
        {
            if (_buttonFeedbackResetTokens.TryGetValue(button, out var current) &&
                ReferenceEquals(current, cancellation))
            {
                _buttonFeedbackResetTokens.Remove(button);
            }

            cancellation.Dispose();
        }
    }

    private void CancelButtonFeedbackReset(Button button)
    {
        if (_buttonFeedbackResetTokens.Remove(button, out var cancellation))
        {
            cancellation.Cancel();
        }
    }

    private static void SetButtonFeedbackClass(Button button, string? className)
    {
        button.Classes.Remove("feedback-loading");
        button.Classes.Remove("feedback-success");
        button.Classes.Remove("feedback-error");
        if (className is not null)
        {
            button.Classes.Add(className);
        }
    }

    private void SetModelInstallButtonState(
        Button button,
        TextBlock label,
        PathIcon downloadIcon,
        PathIcon installedIcon,
        bool isInstalled,
        string downloadText,
        string installedText)
    {
        label.Text = isInstalled ? installedText : downloadText;
        AutomationProperties.SetName(button, label.Text);
        downloadIcon.IsVisible = !isInstalled;
        installedIcon.IsVisible = isInstalled;

        if (isInstalled)
        {
            if (!button.Classes.Contains("model-installed"))
            {
                button.Classes.Add("model-installed");
            }
        }
        else
        {
            button.Classes.Remove("model-installed");
        }

        _buttonDefaultEnabledStates[button] = !isInstalled;
        if (!HasButtonFeedback(button))
        {
            button.IsEnabled = !isInstalled;
        }
    }

    private static bool HasButtonFeedback(Button button) =>
        button.Classes.Contains("feedback-loading") ||
        button.Classes.Contains("feedback-success") ||
        button.Classes.Contains("feedback-error");

    private void ClearSecretInputs()
    {
        HideSecretFields(_secretFields.Keys.ToArray());
    }

    private void RegisterSecretFields()
    {
        RegisterSecretField(SecretKeys.BaiduOcrApiKey, "OCR API Key", BaiduOcrApiKeyBox);
        RegisterSecretField(SecretKeys.BaiduOcrSecretKey, "OCR Secret Key", BaiduOcrSecretBox);
        RegisterSecretField(SecretKeys.BaiduTranslateAppId, "翻译 APP ID", BaiduTranslateAppIdBox);
        RegisterSecretField(SecretKeys.BaiduTranslateSecret, "翻译密钥", BaiduTranslateSecretBox);
        RegisterSecretField(SecretKeys.CustomTranslationApiKey, "兼容接口 API Key", CustomApiKeyBox);
    }

    private void RegisterSecretField(string key, string displayName, TextBox textBox)
    {
        _secretFields[key] = new SecretFieldState(key, displayName, textBox);
        RenderSecretField(_secretFields[key]);
    }

    private void SetSecretFieldValue(string key, string? value)
    {
        if (!_secretFields.TryGetValue(key, out var state))
        {
            return;
        }

        state.Value = value?.Trim() ?? string.Empty;
        state.OriginalValue = state.Value;
        state.IsDirty = false;
        state.IsRevealed = false;
        RenderSecretField(state);
    }

    private void ToggleSecretVisibility_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryGetSecretField(sender, out var state))
        {
            return;
        }

        SyncSecretFieldFromEditor(state);
        state.IsRevealed = !state.IsRevealed;
        RenderSecretField(state);
        if (state.IsRevealed)
        {
            state.TextBox.Focus();
            state.TextBox.CaretIndex = state.TextBox.Text?.Length ?? 0;
        }

        SetGlobalStatus(
            state.IsRevealed ? $"{state.DisplayName} 已显示，可直接编辑或粘贴。" : $"{state.DisplayName} 已隐藏。",
            isError: false);
    }

    private async void CopySecretButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryGetSecretField(sender, out var state))
        {
            return;
        }

        SyncSecretFieldFromEditor(state);
        if (string.IsNullOrWhiteSpace(state.Value))
        {
            SetGlobalStatus($"{state.DisplayName} 尚未填写，无法复制。", isError: true);
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            SetGlobalStatus("当前系统剪贴板不可用。", isError: true);
            return;
        }

        await clipboard.SetTextAsync(state.Value);
        SetGlobalStatus($"{state.DisplayName} 已复制到剪贴板。", isError: false);
    }

    private async void PasteSecretButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryGetSecretField(sender, out var state))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        var value = clipboard is null ? null : await clipboard.TryGetTextAsync();
        if (string.IsNullOrWhiteSpace(value))
        {
            SetGlobalStatus("剪贴板中没有可粘贴的文本。", isError: true);
            return;
        }

        state.Value = value.Trim();
        state.IsDirty = !string.Equals(state.Value, state.OriginalValue, StringComparison.Ordinal);
        state.IsRevealed = false;
        RenderSecretField(state);
        SetGlobalStatus($"{state.DisplayName} 已粘贴，点击“保存并应用”后写入系统安全存储。", isError: false);
    }

    private async Task PersistSecretFieldAsync(string key)
    {
        if (_services is null || !_secretFields.TryGetValue(key, out var state))
        {
            return;
        }

        SyncSecretFieldFromEditor(state);
        if (!state.IsDirty)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(state.Value))
        {
            await _services.SecretStore.DeleteAsync(key);
        }
        else
        {
            await _services.SecretStore.SetAsync(key, state.Value);
        }

        state.OriginalValue = state.Value;
        state.IsDirty = false;
        state.IsRevealed = false;
        RenderSecretField(state);
    }

    private void HideSecretFields(params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!_secretFields.TryGetValue(key, out var state))
            {
                continue;
            }

            SyncSecretFieldFromEditor(state);
            state.IsRevealed = false;
            RenderSecretField(state);
        }
    }

    private bool TryGetSecretField(object? sender, out SecretFieldState state)
    {
        if (sender is Button { Tag: string key } && _secretFields.TryGetValue(key, out var found))
        {
            state = found;
            return true;
        }

        state = null!;
        return false;
    }

    private static void SyncSecretFieldFromEditor(SecretFieldState state)
    {
        if (!state.IsRevealed)
        {
            return;
        }

        state.Value = state.TextBox.Text?.Trim() ?? string.Empty;
        state.IsDirty = !string.Equals(state.Value, state.OriginalValue, StringComparison.Ordinal);
    }

    private static void RenderSecretField(SecretFieldState state)
    {
        state.TextBox.IsReadOnly = !state.IsRevealed;
        state.TextBox.Text = state.IsRevealed ? state.Value : SecretDisplay.Mask(state.Value);
    }

    private sealed record ProviderChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private void LoadTargetLanguageChoices(string providerId, string configuredLanguage)
    {
        if (_services is null)
        {
            return;
        }

        var provider = _services.Providers.GetTranslationProvider(providerId);
        var choices = new List<LanguageChoice>
        {
            new(LanguageCatalog.AutoOpposite, "智能中英互换")
        };
        choices.AddRange(
            LanguageCatalog.All
                .Where(language => provider.Metadata.SupportedLanguages.Contains(
                    language.Code,
                    StringComparer.OrdinalIgnoreCase))
                .Select(language => new LanguageChoice(language.Code, language.DisplayName)));

        TargetLanguageCombo.ItemsSource = choices;
        TargetLanguageCombo.SelectedItem = choices.FirstOrDefault(
            choice => string.Equals(choice.Code, configuredLanguage, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
        TranslationLanguageHintText.Text = providerId == "custom-chat"
            ? "本机 / 自定义大模型支持多语言；实际效果取决于所选模型。"
            : "该翻译引擎只显示当前已经适配的语言。";
    }

    private sealed record LanguageChoice(string Code, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record UiStyleChoice(string Id, string Name)
    {
        public static IReadOnlyList<UiStyleChoice> All { get; } =
        [
            new("modern", "新版精简主界面"),
            new("classic", "经典完整界面")
        ];

        public override string ToString() => Name;
    }

    private sealed class SecretFieldState(string key, string displayName, TextBox textBox)
    {
        public string Key { get; } = key;
        public string DisplayName { get; } = displayName;
        public TextBox TextBox { get; } = textBox;
        public string Value { get; set; } = string.Empty;
        public string OriginalValue { get; set; } = string.Empty;
        public bool IsDirty { get; set; }
        public bool IsRevealed { get; set; }
    }
}
