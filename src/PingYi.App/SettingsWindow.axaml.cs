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

public partial class SettingsWindow : Window
{
    private readonly AppServices? _services;
    private readonly Dictionary<Button, object?> _buttonDefaultContents = [];
    private readonly Dictionary<Button, bool> _buttonDefaultEnabledStates = [];
    private readonly Dictionary<Button, CancellationTokenSource> _buttonFeedbackResetTokens = [];
    private readonly Dictionary<string, SecretFieldState> _secretFields = new(StringComparer.Ordinal);
    private DateTimeOffset _deleteConfirmationExpiresAt;
    private object? _deleteModelsDefaultContent;
    private bool _isLoadingSettings;
    private CancellationTokenSource? _managedModelOperation;
    private Uri? _latestReleasePage;

    public SettingsWindow()
    {
        InitializeComponent();
        InitializeLinuxHelp();
        UiText.Attach(this);
        _deleteModelsDefaultContent = DeleteModelsButton.Content;
        RegisterSecretFields();
        InitializeLanguageSelection();
        UiText.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => UiText.LanguageChanged -= OnLanguageChanged;
        RefreshWindowTitle();
    }

    public SettingsWindow(AppServices services) : this()
    {
        _services = services;
        _persistLanguage = services.SaveUiLanguageAsync;
        LoadSettings();
        Opened += async (_, _) =>
        {
            await RefreshCredentialStatusAsync();
            await RefreshLocalModelStatusAsync();
            await RefreshManagedModelStatusAsync(attemptConfiguredStart: false);
        };
    }

    public void SetGlobalStatus(string message, bool isError)
    {
        GlobalStatusText.Text = UiText.T(message);
        ThemeResources.Use(GlobalStatusText, TextBlock.ForegroundProperty,
            isError ? "DangerTextBrush" : "SecondaryTextBrush");
        ThemeResources.Use(StatusIndicator, Border.BackgroundProperty,
            isError ? "DangerBrush" : "BrandBrush");
        ThemeResources.Use(GlobalStatusBorder, Border.BackgroundProperty,
            isError ? "WarningBackgroundBrush" : "SubtleBackgroundBrush");
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
            _selectedLanguage = settings.UiLanguage;
            OcrProviderCombo.ItemsSource = _services.Providers.OcrProviders
                .Select(provider => new ProviderChoice(
                    provider.Metadata.Id,
                    UiText.ProviderName(provider.Metadata.Id, provider.Metadata.DisplayName)))
                .ToArray();
            TranslationProviderCombo.ItemsSource = _services.Providers.TranslationProviders
                .Select(provider => new ProviderChoice(
                    provider.Metadata.Id,
                    UiText.ProviderName(provider.Metadata.Id, provider.Metadata.DisplayName)))
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
            var languageChoices = UiLanguageChoice.Create();
            UiLanguageCombo.ItemsSource = languageChoices;
            UiLanguageCombo.SelectedItem = languageChoices
                .First(choice => choice.Id == settings.UiLanguage);
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

        RefreshChoiceTemplates();
        SetGlobalStatus(UiText.Get("String.SettingsReady"), isError: false);
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
            SetGlobalStatus(LinuxDesktopUi.DescribeError(exception), isError: true);
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
            _services.HotkeyRegistrationError = null;
        }
        catch
        {
            await _services.HotkeyService.StopAsync();
            try
            {
                await _services.HotkeyService.StartAsync(previousHotkey);
                _services.HotkeyRegistrationError = null;
            }
            catch (Exception rollbackError) { _services.HotkeyRegistrationError = rollbackError; }
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
            SetGlobalStatus(LinuxDesktopUi.DescribeError(exception), isError: true);
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
            SetGlobalStatus(
                language.Code == LanguageCatalog.AutoOpposite
                    ? "已启用自动翻译：外语译成简体中文，中文译成英文。"
                    : $"目标语言已切换为：{language.Name}。",
                isError: false);
        }
        catch (Exception exception)
        {
            SetGlobalStatus(LinuxDesktopUi.DescribeError(exception), isError: true);
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
            var ocrName = UiText.ProviderName(ocr.Metadata.Id, ocr.Metadata.DisplayName);
            var translationName = UiText.ProviderName(translation.Metadata.Id, translation.Metadata.DisplayName);
            SetGlobalStatus(
                ready
                    ? UiText.IsEnglish
                        ? $"{ocrName} and {translationName} are available."
                        : $"{ocrName}、{translationName}均可用。"
                    : UiText.IsEnglish
                        ? $"OCR: {UiText.T(ocrStatus.Message ?? "可用")}  Translation: {UiText.T(translationStatus.Message ?? "可用")}" 
                        : $"OCR：{ocrStatus.Message ?? "可用"}  翻译：{translationStatus.Message ?? "可用"}",
                isError: !ready);
            FinishButtonOperation(button, ready ? "状态正常" : "检查未通过", success: ready);
        }
        catch (Exception exception)
        {
            SetGlobalStatus(LinuxDesktopUi.DescribeError(exception), isError: true);
            FinishButtonOperation(button, "检查失败", success: false);
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
            CheckForUpdates = CheckForUpdatesCheckBox.IsChecked == true,
            UiLanguage = _services?.Settings.UiLanguage ?? _selectedLanguage
        };
    }

    private async void CheckUpdatesNowButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null)
        {
            return;
        }

        CheckUpdatesNowButton.IsEnabled = false;
        OpenLatestReleaseButton.IsVisible = false;
        UpdateStatusText.Text = UiText.T("正在检查…");
        try
        {
            var update = await _services.UpdateService.CheckAsync();
            _latestReleasePage = update.ReleasePage;
            if (update.IsUpdateAvailable)
            {
                UpdateStatusText.Text = UiText.IsEnglish
                    ? $"Version {update.LatestVersion} is available."
                    : $"发现新版本 {update.LatestVersion}。";
                OpenLatestReleaseButton.IsVisible = true;
            }
            else
            {
                UpdateStatusText.Text = UiText.IsEnglish
                    ? $"You are up to date ({update.CurrentVersion})."
                    : $"当前已是最新版本（{update.CurrentVersion}）。";
            }
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = UiText.Error(exception);
        }
        finally
        {
            CheckUpdatesNowButton.IsEnabled = true;
        }
    }

    private void OpenLatestReleaseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_latestReleasePage is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo(_latestReleasePage.AbsoluteUri) { UseShellExecute = true });
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
        textBlock.Text = UiText.T(message);
        ThemeResources.Use(textBlock, TextBlock.ForegroundProperty, brushKey);
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

    private sealed record ProviderChoice(string Id, string Name)
    {
        public override string ToString() => UiText.T(Name);
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
            new(LanguageCatalog.AutoOpposite, UiText.T("自动（推荐）"))
        };
        choices.AddRange(
            LanguageCatalog.All
                .Where(language => provider.Metadata.SupportedLanguages.Contains(
                    language.Code,
                    StringComparer.OrdinalIgnoreCase))
                .Select(language => new LanguageChoice(
                    language.Code,
                    UiText.IsEnglish ? language.EnglishDisplayName : language.DisplayName)));

        TargetLanguageCombo.ItemsSource = choices;
        TargetLanguageCombo.SelectedItem = choices.FirstOrDefault(
            choice => string.Equals(choice.Code, configuredLanguage, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
        TranslationLanguageHintText.Text = providerId == "custom-chat"
            ? "自动识别原文；外语译成简体中文，中文译成英文。也可手动指定目标语言；实际效果取决于所选模型。"
            : "自动识别原文；外语译成简体中文，中文译成英文。列表仅显示该引擎已适配的目标语言。";
    }

    private sealed record LanguageChoice(string Code, string Name)
    {
        public override string ToString() => UiText.T(Name);
    }

    private sealed record UiLanguageChoice(string Id, string Name)
    {
        public static IReadOnlyList<UiLanguageChoice> Create() => UiText.IsEnglish
            ? [new("auto", "Follow system"), new("zh-CN", "简体中文"), new("en-US", "English")]
            : [new("auto", "跟随系统 / Follow system"), new("zh-CN", "简体中文"), new("en-US", "English")];

        public override string ToString() => UiText.T(Name);
    }


}
