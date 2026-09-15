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
                    SetInlineStatus(BaiduOcrCredentialStatusText, $"OCR 凭据：{UiText.Error(exception)}", "DangerTextBrush");
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
                    SetInlineStatus(BaiduTranslationCredentialStatusText, $"翻译凭据：{UiText.Error(exception)}", "DangerTextBrush");
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
            SetGlobalStatus(UiText.Error(exception), isError: true);
            FinishButtonOperation(button, "验证失败", success: false);
        }
    }

    private async void ValidateGoogleCredentialsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null)
        {
            return;
        }

        var button = sender as Button;
        BeginButtonOperation(button, "正在验证…");
        SetGlobalStatus("正在安全保存并验证 Google Cloud 凭据…", isError: false);
        try
        {
            await PersistSecretFieldAsync(SecretKeys.GoogleCloudApiKey);
            HideSecretFields(SecretKeys.GoogleCloudApiKey);
            var configured = await _services.GoogleOcrProvider.GetAvailabilityAsync();
            if (!configured.IsAvailable)
            {
                SetInlineStatus(GoogleOcrCredentialStatusText, "Google OCR 凭据：尚未配置", "WarningTextBrush");
                SetInlineStatus(GoogleTranslationCredentialStatusText, "Google 翻译凭据：尚未配置", "WarningTextBrush");
                SetGlobalStatus("尚未填写 Google Cloud API Key。", isError: true);
                FinishButtonOperation(button, "验证未通过", success: false);
                return;
            }

            var allValid = true;
            try
            {
                await _services.GoogleOcrProvider.ValidateCredentialsAsync();
                SetInlineStatus(GoogleOcrCredentialStatusText, "Google OCR 凭据：验证通过", "SuccessTextBrush");
            }
            catch (Exception exception)
            {
                allValid = false;
                SetInlineStatus(GoogleOcrCredentialStatusText, $"Google OCR 凭据：{UiText.Error(exception)}", "DangerTextBrush");
            }

            try
            {
                await _services.GoogleTranslationProvider.ValidateCredentialsAsync();
                SetInlineStatus(GoogleTranslationCredentialStatusText, "Google 翻译凭据：验证通过", "SuccessTextBrush");
            }
            catch (Exception exception)
            {
                allValid = false;
                SetInlineStatus(GoogleTranslationCredentialStatusText, $"Google 翻译凭据：{UiText.Error(exception)}", "DangerTextBrush");
            }

            SetGlobalStatus(
                allValid
                    ? "Google Cloud Vision 与 Translation 均验证通过。"
                    : "部分 Google Cloud API 验证失败，请确认项目已启用对应 API 并检查密钥限制。",
                isError: !allValid);
            FinishButtonOperation(button, allValid ? "验证通过" : "验证未通过", success: allValid);
        }
        catch (Exception exception)
        {
            SetGlobalStatus(UiText.Error(exception), isError: true);
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
            SetInlineStatus(CustomTranslationStatusText, UiText.IsEnglish
                ? $"{preset.LocalizedDisplayName} preset saved; discovering available models…"
                : $"{preset.DisplayName} 预设已保存，正在发现可用模型…", "SecondaryTextBrush");
            SetGlobalStatus(UiText.IsEnglish
                ? $"{preset.LocalizedDisplayName} configuration applied; checking the service…"
                : $"{preset.DisplayName} 配置已应用，正在检查服务…", isError: false);
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
            SetInlineStatus(CustomTranslationStatusText, UiText.Error(exception), "DangerTextBrush");
            SetGlobalStatus(UiText.Error(exception), isError: true);
            FinishButtonOperation(button, presetSaved ? "已应用，连接失败" : "应用失败", success: false);
        }
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
            var googleApiKey = await _services.SecretStore.GetAsync(SecretKeys.GoogleCloudApiKey);
            var customApiKey = await _services.SecretStore.GetAsync(SecretKeys.CustomTranslationApiKey);
            SetSecretFieldValue(SecretKeys.BaiduOcrApiKey, ocrApiKey);
            SetSecretFieldValue(SecretKeys.BaiduOcrSecretKey, ocrSecret);
            SetSecretFieldValue(SecretKeys.BaiduTranslateAppId, translationAppId);
            SetSecretFieldValue(SecretKeys.BaiduTranslateSecret, translationSecret);
            SetSecretFieldValue(SecretKeys.GoogleCloudApiKey, googleApiKey);
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
            var googleReady = !string.IsNullOrWhiteSpace(googleApiKey);
            SetInlineStatus(
                GoogleOcrCredentialStatusText,
                googleReady ? "Google OCR 凭据：已安全保存" : "Google OCR 凭据：未配置",
                googleReady ? "SuccessTextBrush" : "WarningTextBrush");
            SetInlineStatus(
                GoogleTranslationCredentialStatusText,
                googleReady ? "Google 翻译凭据：已安全保存" : "Google 翻译凭据：未配置",
                googleReady ? "SuccessTextBrush" : "WarningTextBrush");
        }
        catch (Exception exception)
        {
            SetInlineStatus(
                BaiduOcrCredentialStatusText,
                UiText.IsEnglish
                    ? $"Could not read credentials: {UiText.Error(exception)}"
                    : $"读取凭据失败：{UiText.Error(exception)}",
                "DangerTextBrush");
            SetInlineStatus(BaiduTranslationCredentialStatusText, "翻译凭据状态未知", "DangerTextBrush");
            SetInlineStatus(GoogleOcrCredentialStatusText, "Google OCR 凭据状态未知", "DangerTextBrush");
            SetInlineStatus(GoogleTranslationCredentialStatusText, "Google 翻译凭据状态未知", "DangerTextBrush");
        }
    }

    private async Task SaveAllEnteredSecretsAsync()
    {
        await SaveBaiduSecretInputsAsync();
        await PersistSecretFieldAsync(SecretKeys.GoogleCloudApiKey);
        await PersistSecretFieldAsync(SecretKeys.CustomTranslationApiKey);
    }

    private async Task SaveBaiduSecretInputsAsync()
    {
        await PersistSecretFieldAsync(SecretKeys.BaiduOcrApiKey);
        await PersistSecretFieldAsync(SecretKeys.BaiduOcrSecretKey);
        await PersistSecretFieldAsync(SecretKeys.BaiduTranslateAppId);
        await PersistSecretFieldAsync(SecretKeys.BaiduTranslateSecret);
    }

    private void ClearSecretInputs()
    {
        HideSecretFields(_secretFields.Keys.ToArray());
    }

    private void RegisterSecretFields()
    {
        RegisterSecretField(
            SecretKeys.BaiduOcrApiKey,
            "OCR API Key",
            BaiduOcrApiKeyBox,
            BaiduOcrApiKeyRevealButton);
        RegisterSecretField(
            SecretKeys.BaiduOcrSecretKey,
            "OCR Secret Key",
            BaiduOcrSecretBox,
            BaiduOcrSecretRevealButton);
        RegisterSecretField(
            SecretKeys.BaiduTranslateAppId,
            "翻译 APP ID",
            BaiduTranslateAppIdBox,
            BaiduTranslateAppIdRevealButton);
        RegisterSecretField(
            SecretKeys.BaiduTranslateSecret,
            "翻译密钥",
            BaiduTranslateSecretBox,
            BaiduTranslateSecretRevealButton);
        RegisterSecretField(
            SecretKeys.GoogleCloudApiKey,
            "Google Cloud API Key",
            GoogleCloudApiKeyBox,
            GoogleCloudApiKeyRevealButton);
        RegisterSecretField(
            SecretKeys.CustomTranslationApiKey,
            "兼容接口 API Key",
            CustomApiKeyBox,
            CustomApiKeyRevealButton);
    }

    private void RegisterSecretField(string key, string displayName, TextBox textBox, Button revealButton)
    {
        _secretFields[key] = new SecretFieldState(key, displayName, textBox, revealButton);
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
        state.RevealButton.Content = state.IsRevealed ? "隐藏" : "显示";
        AutomationProperties.SetName(
            state.RevealButton,
            $"{(state.IsRevealed ? "隐藏" : "显示")}{state.DisplayName}");
    }

    private sealed class SecretFieldState(
        string key,
        string displayName,
        TextBox textBox,
        Button revealButton)
    {
        public string Key { get; } = key;
        public string DisplayName { get; } = displayName;
        public TextBox TextBox { get; } = textBox;
        public Button RevealButton { get; } = revealButton;
        public string Value { get; set; } = string.Empty;
        public string OriginalValue { get; set; } = string.Empty;
        public bool IsDirty { get; set; }
        public bool IsRevealed { get; set; }
    }}
