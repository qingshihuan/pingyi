using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public sealed partial class AppServices
{
    public ITranslationProvider TaskTranslator(AppSettings snapshot) =>
        ConfiguredProviders.Translation(_inferenceClient, SecretStore, snapshot);
    public IOcrProvider TaskOcr(AppSettings snapshot) =>
        ConfiguredProviders.Ocr(_inferenceClient, SecretStore, snapshot);
    public IImageAnalysisProvider TaskImageAnalyzer(AppSettings snapshot, CapturePurpose purpose) =>
        ConfiguredProviders.Image(_imageAnalysisClient, SecretStore, snapshot, purpose);

    public async Task SaveConfigurationAsync(AppSettings settings, IReadOnlyDictionary<string, string> editedKeys,
        ModelConfiguration? expectedConfiguration, CancellationToken token = default)
    {
        if (IsRuntimeMaintenance) throw new ProviderException("runtime_maintenance", "后端维护中，请完成后再保存。 / Finish runtime maintenance before saving.");
        var normalized = ConfigurationMigration.Upgrade(settings).Normalize();
        normalized.ModelConfiguration!.Validate();
        var pendingKeys = new Dictionary<string, string>(editedKeys, StringComparer.Ordinal);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        token = lifetime.Token;
        await _settingsTransitionGate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(IsShuttingDown, this);
            if (Settings.ModelConfiguration != expectedConfiguration)
                throw new ProviderException("configuration_changed", "配置已在其他操作中改变，请重新打开设置后编辑。 / Configuration changed; reopen settings before editing.");
            var previous = Settings;
            await ConfigurationSecrets.CommitAsync(SettingsStore, SecretStore, previous, normalized, pendingKeys, token);
            Settings = normalized;
            // Saving configuration is not a request to load a multi-gigabyte model.
            // Remaining managed tasks start it on demand. Retire an unused owned runtime.
            if (!RuntimePolicy.UsesAnyManagedRuntime(Settings) && RuntimePolicy.UsesAnyManagedRuntime(previous))
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
            await ModelCapabilityProbe.VerifyTextAsync(translator, deadline.Token);
            return entry with { TextVerified = true };
        }
        var ocr = new ChatCompatibleOcrProvider(_inferenceClient, secrets, () => snapshot, translator);
        await ModelCapabilityProbe.VerifyVisionAsync(ocr, deadline.Token);
        return entry with { VisionVerified = true };
    }

    private static AppSettings ProfileSnapshot(ModelServiceProfile profile, string modelId) => new()
    {
        CustomTranslationEndpoint = ModelServiceProfile.CanonicalEndpoint(profile.Endpoint),
        CustomTranslationApiProtocol = profile.Protocol, CustomTranslationModel = modelId,
        ManagedRuntimeEnabled = false
    };
}
