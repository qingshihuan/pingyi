using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Diagnostics;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public partial class SettingsWindow : Window
{
    private readonly AppServices? _services;
    private readonly Dictionary<Button, object?> _buttonDefaultContents = [];
    private readonly Dictionary<Button, bool> _buttonDefaultEnabledStates = [];
    private readonly Dictionary<Button, CancellationTokenSource> _buttonFeedbackResetTokens = [];
    private bool _isLoadingSettings;
    private bool _settingsClosed;
    private CancellationTokenSource? _managedModelOperation;
    private Uri? _latestReleasePage;
    private ModelConfiguration? _baselineConfiguration;
    internal ConfigurationCenterView ConfigurationCenter { get; } = new();
    private sealed record CurrentChoice(ModelSelection? Selection, string Label)
    { public override string ToString() => Label; }
    private sealed record LanguageChoice(string Code, string Name)
    { public override string ToString() => Name; }

    public SettingsWindow()
    {
        InitializeComponent();
        UiText.Attach(this);
        ApiEditorHost.Content = ConfigurationCenter;
        InitializeLinuxHelp();
        InitializeRuntimeStatusPage();
        InitializeLanguageSelection();
        LoadSettings(new AppSettings { UiLanguage = UiText.CurrentLanguage });
        SettingsTabs.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.Source, SettingsTabs) && CurrentModelSettingsTab.IsSelected)
                TryRefreshCurrentModels();
        };
        UiText.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) =>
        {
            _settingsClosed = true;
            UiText.LanguageChanged -= OnLanguageChanged;
            ConfigurationCenter.Cancel(); _managedModelOperation?.Cancel();
            foreach (var button in _buttonFeedbackResetTokens.Keys.ToArray()) CancelButtonFeedbackReset(button);
        };
        RefreshWindowTitle(); RefreshSettingsLanguage();
    }
    public SettingsWindow(AppServices services) : this()
    {
        _services = services;
        InitializeStatusUpdates(services);
        ConfigureLocalAutomatically = services.ConfigureLocalModelAutomaticallyAsync;
        _persistLanguage = services.SaveUiLanguageAsync;
        ConfigurationCenter.Discover = services.DiscoverProfileModelsAsync;
        ConfigurationCenter.Verify = services.TestProfileModelAsync;
        LoadSettings(services.Settings);
        InitializeRuntimeHardware();
        if (services.ConfigurationCredentialWarning is { } warning) SetGlobalStatus(warning, true);
        Opened += async (_, _) => { await RefreshManagedModelStatusAsync(false); await RefreshRuntimeStatusAsync(); };
    }
    public void SetGlobalStatus(string message, bool isError)
    {
        GlobalStatusText.Text = UiText.T(message);
        ThemeResources.Use(GlobalStatusText, TextBlock.ForegroundProperty, isError ? "DangerTextBrush" : "SecondaryTextBrush");
        ThemeResources.Use(StatusIndicator, Border.BackgroundProperty, isError ? "DangerBrush" : "BrandBrush");
    }
    internal void LoadSettings(AppSettings settings)
    {
        _isLoadingSettings = true;
        try
        {
            _baselineConfiguration = settings.ModelConfiguration;
            ConfigurationCenter.Load(settings);
            _selectedLanguage = settings.UiLanguage; InitializeLanguageSelection();
            HotkeyBox.Text = settings.Hotkey;
            StartMinimizedCheckBox.IsChecked = settings.StartMinimized;
            CheckForUpdatesCheckBox.IsChecked = settings.CheckForUpdates;
            LoadTargetLanguageChoices(settings.TargetLanguage);
            ManagedModelExpander.IsVisible = true;
            ManagedModelCombo.ItemsSource = ManagedMultimodalModels.All;
            ManagedModelCombo.SelectedItem = ManagedMultimodalModels.TryGet(settings.ManagedModelPackageId, out var model) ? model : ManagedMultimodalModels.Recommended;
            ManagedRuntimeBackendCombo.ItemsSource = ManagedRuntimeBackends.All;
            ManagedRuntimeBackendCombo.SelectedItem = ManagedRuntimeBackends.Get(settings.ManagedRuntimeBackend);
            UpdateManagedModelDescription(); UpdateManagedRuntimeBackendDescription();
            RefreshCurrentModels(settings.ModelConfiguration?.CurrentModel);
            MigrationNoticeText.Text = settings.ModelConfiguration?.MigrationNotice;
        }
        finally { _isLoadingSettings = false; }
    }
    internal ModelConfiguration BuildModelConfiguration()
    {
        var api = ConfigurationCenter.BuildDraft();
        var config = (_baselineConfiguration ?? new()) with
        {
            Services = api.Services, LocalServices = api.LocalServices,
            CurrentModel = (CurrentModelCombo.SelectedItem as CurrentChoice)?.Selection
        };
        config.Validate();
        return config;
    }
    private void RefreshCurrentModels(ModelSelection? preferred = null)
    {
        var selection = preferred ?? (CurrentModelCombo.SelectedItem as CurrentChoice)?.Selection;
        var api = ConfigurationCenter.BuildDraft();
        var choices = new List<CurrentChoice> { new(null, CaptureUiText.Pick("尚未选择模型", "No model selected")) };
        foreach (var profile in api.AllServices)
            foreach (var entry in profile.Models.Where(m => m.TextVerified && m.VisionVerified))
                choices.Add(new(new(profile.Id, entry.Id), profile.Name + " · " + entry.Id));
        var config = _services?.Settings.ModelConfiguration ?? _baselineConfiguration;
        if (config is not null && ManagedMultimodalModels.TryGet(config.VerifiedManagedModelId, out var model))
            choices.Add(new(new(ModelConfiguration.Managed, model.Id), CaptureUiText.Pick("本地托管 · ", "Managed local · ") + model.LocalizedDisplayName));
        CurrentModelCombo.ItemsSource = choices;
        CurrentModelCombo.SelectedItem = choices.FirstOrDefault(c => c.Selection == selection) ?? choices[0];
    }
    private void TryRefreshCurrentModels()
    {
        try { RefreshCurrentModels(); }
        catch (Exception error) { SetGlobalStatus(UiText.Error(error), true); }
    }
    private void RefreshCurrentModels_OnClick(object? sender, RoutedEventArgs e) => TryRefreshCurrentModels();
    private AppSettings BuildSettingsFromForm() => (_services?.Settings ?? new AppSettings()) with
    {
        ModelConfiguration = BuildModelConfiguration(), InitialSetupCompleted = true,
        TargetLanguage = (TargetLanguageCombo.SelectedItem as LanguageChoice)?.Code ?? LanguageCatalog.AutoOpposite,
        Hotkey = HotkeyBox.Text ?? AppSettings.DefaultHotkey,
        StartMinimized = StartMinimizedCheckBox.IsChecked == true,
        CheckForUpdates = CheckForUpdatesCheckBox.IsChecked == true,
        UiLanguage = _services?.Settings.UiLanguage ?? _selectedLanguage
    };
    private async void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null || _managedModelOperation is not null || _runtimeHardware?.IsBusy == true) return;
        BeginButtonOperation(SaveSettingsButton, CaptureUiText.Pick("正在保存…", "Saving…"));
        try
        {
            var previous = _services.Settings;
            var updated = BuildSettingsFromForm();
            _ = GlobalHotkeyGesture.Parse(updated.Hotkey);
            var keys = new Dictionary<string, string>(ConfigurationCenter.PendingKeys, StringComparer.Ordinal);
            var changed = !string.Equals(previous.Hotkey, updated.Hotkey, StringComparison.OrdinalIgnoreCase);
            if (changed) await SwitchHotkeyAsync(previous.Hotkey, updated.Hotkey);
            try { await _services.SaveConfigurationAsync(updated, keys, _baselineConfiguration); }
            catch { if (changed) await SwitchHotkeyAsync(updated.Hotkey, previous.Hotkey); throw; }
            LoadSettings(_services.Settings);
            SetGlobalStatus(CaptureUiText.Pick("设置已保存；密钥沿用现有本地存储。", "Settings saved using the existing local key store."), false);
            FinishButtonOperation(SaveSettingsButton, CaptureUiText.Pick("已保存", "Saved"), true);
        }
        catch (Exception error)
        { SetGlobalStatus(UiText.Error(error), true); FinishButtonOperation(SaveSettingsButton, CaptureUiText.Pick("保存失败", "Save failed"), false); }
    }
    private void LoadTargetLanguageChoices(string selected)
    {
        var choices = new List<LanguageChoice> { new(LanguageCatalog.AutoOpposite, CaptureUiText.Pick("自动（外语→中文，中文→英文）", "Automatic (foreign → Chinese, Chinese → English)")) };
        choices.AddRange(LanguageCatalog.All.Select(l => new LanguageChoice(l.Code, UiText.IsEnglish ? l.EnglishDisplayName : l.DisplayName)));
        TargetLanguageCombo.ItemsSource = choices;
        TargetLanguageCombo.SelectedItem = choices.FirstOrDefault(l => l.Code == selected) ?? choices[0];
    }
    private async Task SwitchHotkeyAsync(string previousHotkey, string nextHotkey)
    {
        if (_services is null) return;
        await _services.HotkeyService.StopAsync();
        try
        {
            await _services.HotkeyService.StartAsync(nextHotkey);
            _services.HotkeyRegistrationError = null;
        }
        catch
        {
            await _services.HotkeyService.StopAsync();
            try { await _services.HotkeyService.StartAsync(previousHotkey); _services.HotkeyRegistrationError = null; }
            catch (Exception rollbackError) { _services.HotkeyRegistrationError = rollbackError; }
            throw;
        }
    }

    private async void CheckUpdatesNowButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null) return;
        CheckUpdatesNowButton.IsEnabled = false;
        OpenLatestReleaseButton.IsVisible = false;
        UpdateStatusText.Text = UiText.T("正在检查…");
        try
        {
            var update = await _services.UpdateService.CheckAsync();
            _latestReleasePage = update.ReleasePage;
            if (update.IsUpdateAvailable)
            {
                UpdateStatusText.Text = UiText.IsEnglish ? $"Version {update.LatestVersion} is available." : $"发现新版本 {update.LatestVersion}。";
                OpenLatestReleaseButton.IsVisible = true;
            }
            else UpdateStatusText.Text = UiText.IsEnglish ? $"You are up to date ({update.CurrentVersion})." : $"当前已是最新版本（{update.CurrentVersion}）。";
        }
        catch (Exception exception) { UpdateStatusText.Text = UiText.Error(exception); }
        finally { CheckUpdatesNowButton.IsEnabled = true; }
    }

    private void OpenLatestReleaseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_latestReleasePage is null) return;
        Process.Start(new ProcessStartInfo(_latestReleasePage.AbsoluteUri) { UseShellExecute = true });
    }
    private static void SetInlineStatus(TextBlock textBlock, string message, string brushKey)
    {
        textBlock.Text = UiText.T(message);
        ThemeResources.Use(textBlock, TextBlock.ForegroundProperty, brushKey);
    }
    private void BeginButtonOperation(Button? button, string message)
    {
        if (button is null) return;
        if (!_buttonDefaultContents.ContainsKey(button)) _buttonDefaultContents[button] = button.Content;
        if (!_buttonDefaultEnabledStates.ContainsKey(button)) _buttonDefaultEnabledStates[button] = button.IsEnabled;
        CancelButtonFeedbackReset(button);
        SetButtonFeedbackClass(button, "feedback-loading");
        button.Content = message; button.IsEnabled = false;
    }
    private void FinishButtonOperation(Button? button, string message, bool success, bool isEnabledAfterResult = true)
    {
        if (button is null) return;
        CancelButtonFeedbackReset(button);
        SetButtonFeedbackClass(button, success ? "feedback-success" : "feedback-error");
        button.Content = message; button.IsEnabled = isEnabledAfterResult;
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
                if (_buttonDefaultContents.TryGetValue(button, out var defaultContent)) button.Content = defaultContent;
                if (_buttonDefaultEnabledStates.TryGetValue(button, out var isEnabled)) button.IsEnabled = isEnabled;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_buttonFeedbackResetTokens.TryGetValue(button, out var current) && ReferenceEquals(current, cancellation)) _buttonFeedbackResetTokens.Remove(button);
            cancellation.Dispose();
        }
    }
    private void CancelButtonFeedbackReset(Button button)
    {
        if (_buttonFeedbackResetTokens.Remove(button, out var cancellation)) cancellation.Cancel();
    }
    private static void SetButtonFeedbackClass(Button button, string? className)
    {
        button.Classes.Remove("feedback-loading"); button.Classes.Remove("feedback-success"); button.Classes.Remove("feedback-error");
        if (className is not null) button.Classes.Add(className);
    }
    private sealed record UiLanguageChoice(string Id, string Name)
    {
        public static IReadOnlyList<UiLanguageChoice> Create() => UiText.IsEnglish
            ? [new("auto", "Follow system"), new("zh-CN", "简体中文"), new("en-US", "English")]
            : [new("auto", "跟随系统 / Follow system"), new("zh-CN", "简体中文"), new("en-US", "English")];
        public override string ToString() => UiText.T(Name);
    }
}
