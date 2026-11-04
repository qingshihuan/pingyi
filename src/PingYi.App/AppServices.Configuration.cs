using PingYi.Core;
using PingYi.Infrastructure;
using SkiaSharp;

namespace PingYi.App;

public sealed partial class AppServices
{
    public ITranslationProvider TaskTranslator(AppSettings snapshot) =>
        ConfiguredProviders.Translation(_inferenceClient, SecretStore, snapshot, Providers);
    public IOcrProvider TaskOcr(AppSettings snapshot) =>
        ConfiguredProviders.Ocr(_inferenceClient, SecretStore, snapshot, Providers);
    public IImageAnalysisProvider TaskImageAnalyzer(AppSettings snapshot, CapturePurpose purpose) =>
        ConfiguredProviders.Image(_imageAnalysisClient, SecretStore, snapshot, purpose);

    internal async Task InitializeProfileCredentialsAsync(CancellationToken token = default)
    {
        await _settingsTransitionGate.WaitAsync(token);
        try { Settings = await ConfigurationSecrets.ImportLegacyAsync(Settings, SecretStore, token); }
        finally { _settingsTransitionGate.Release(); }
    }

    public async Task SaveConfigurationAsync(AppSettings settings, IReadOnlyDictionary<string, string> editedKeys,
        ModelConfiguration? expectedConfiguration, CancellationToken token = default)
    {
        if (IsRuntimeMaintenance) throw new ProviderException("runtime_maintenance", "后端维护中，请完成后再保存。 / Finish runtime maintenance before saving.");
        var normalized = ConfigurationMigration.Upgrade(settings.Normalize());
        normalized.ModelConfiguration!.Validate();
        await _settingsTransitionGate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(IsShuttingDown, this);
            if (Settings.ModelConfiguration != expectedConfiguration)
                throw new ProviderException("configuration_changed", "配置已在其他操作中改变，请重新打开设置后编辑。 / Configuration changed; reopen settings before editing.");
            var previous = Settings;
            await ConfigurationSecrets.CommitAsync(SettingsStore, SecretStore, previous, normalized, editedKeys, token);
            Settings = normalized;
            if (RuntimePolicy.UsesManagedRuntime(Settings)) WarmManagedRuntimeIfConfigured();
            else if (RuntimePolicy.UsesManagedRuntime(previous))
            {
                var startup = InvalidateManagedRuntimeStartup();
                CancelAndRelease(startup.Cancellation, startup.Task);
                await ManagedModels.StopAsync(_lifetime.Token);
            }
        }
        finally { _settingsTransitionGate.Release(); }
    }

    internal async Task<IReadOnlyList<string>> DiscoverProfileModelsAsync(ModelServiceProfile profile, string? draftKey, CancellationToken token)
    {
        profile.Validate();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        var snapshot = ProfileSnapshot(profile, profile.DefaultModel);
        return await new ChatCompatibleTranslationProvider(_inferenceClient,
            new ProfileSecretStore(SecretStore, profile.CredentialRef, draftKey), () => snapshot).GetAvailableModelsAsync(deadline.Token);
    }

    internal async Task<ModelCatalogEntry> TestProfileModelAsync(ModelServiceProfile profile, string modelId,
        string? draftKey, bool vision, CancellationToken token)
    {
        profile.Validate();
        if (string.IsNullOrWhiteSpace(modelId)) throw new ProviderException("profile_model_missing", "请先选择模型。 / Select a model first.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var snapshot = ProfileSnapshot(profile, modelId);
        var secrets = new ProfileSecretStore(SecretStore, profile.CredentialRef, draftKey);
        var translator = new ChatCompatibleTranslationProvider(_inferenceClient, secrets, () => snapshot);
        var entry = profile.Models.FirstOrDefault(m => m.Id == modelId) ?? new ModelCatalogEntry(modelId);
        if (!vision)
        {
            var result = await translator.TranslateAsync(new TranslationRequest("Hello", "en", "zh"), deadline.Token);
            if (string.IsNullOrWhiteSpace(result.Text)) throw new ProviderException("profile_text_empty", "测试未返回文字。 / The test returned no text.");
            return entry with { TextVerified = true };
        }
        using var bitmap = new SKBitmap(360, 96);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        using (var font = new SKFont(SKTypeface.Default, 28))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawText("PINGYI OCR 2026", 18, 58, SKTextAlign.Left, font, paint);
        }
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var ocr = new ChatCompatibleOcrProvider(_inferenceClient, secrets, () => snapshot, translator);
        var recognized = await ocr.RecognizeAsync(new(png.ToArray(), 360, 96, new(0, 0, 360, 96)), new("en"), deadline.Token);
        if (!recognized.PlainText.Contains("PINGYI", StringComparison.OrdinalIgnoreCase) || !recognized.PlainText.Contains("2026", StringComparison.Ordinal))
            throw new ProviderException("profile_vision_unverified", "模型未正确读出固定测试图片，视觉能力仍未验证。 / Synthetic image test did not verify vision capability.");
        return entry with { VisionVerified = true };
    }

    private static AppSettings ProfileSnapshot(ModelServiceProfile profile, string modelId) => new()
    {
        CustomTranslationEndpoint = ModelServiceProfile.CanonicalEndpoint(profile.Endpoint),
        CustomTranslationApiProtocol = profile.Protocol, CustomTranslationModel = modelId,
        ManagedRuntimeEnabled = false
    };
}
