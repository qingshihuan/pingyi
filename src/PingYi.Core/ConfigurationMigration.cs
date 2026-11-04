using System.Text.Json;

namespace PingYi.Core;

/// <summary>Read-only conversion. The store backs up original bytes before the first save.</summary>
public static class ConfigurationMigration
{
    public static AppSettings Upgrade(AppSettings settings, JsonElement? original = null)
    {
        if (settings.SchemaVersion >= 16)
            return settings with { ModelConfiguration = settings.ModelConfiguration ?? new() };

        var config = settings.ModelConfiguration ?? new();
        var profiles = config.AllServices.ToList();
        var endpoint = Read(original, "customTranslationEndpoint", settings.CustomTranslationEndpoint);
        var model = Read(original, "customTranslationModel", settings.CustomTranslationModel).Trim();
        var protocol = Read(original, "customTranslationApiProtocol", settings.CustomTranslationApiProtocol);
        var notice = "已保留原服务与本地密钥。请验证图片输入和文字输出，再选择一个当前模型。 / Services and local keys were preserved. Verify image input and text output, then select one current model.";
        ModelSelection? candidate = null;
        var pendingKey = config.LegacyCredentialPending;
        var keyEndpoint = config.LegacyCredentialEndpoint;
        if (profiles.Count == 0 && !(settings.ManagedRuntimeEnabled && endpoint == AppSettings.ManagedModelEndpoint))
        {
            try
            {
                var profile = new ModelServiceProfile
                {
                    Id = ModelConfiguration.Imported, Name = "原有模型服务 / Imported service",
                    Endpoint = ModelServiceProfile.CanonicalEndpoint(endpoint),
                    Protocol = !new Uri(endpoint).IsLoopback && settings.SchemaVersion < 14 && ModelApiProtocols.IsOfficialOpenAiEndpoint(endpoint)
                        ? ModelApiProtocols.Responses : ModelApiProtocols.Normalize(protocol),
                    DefaultModel = model,
                    Models = string.IsNullOrWhiteSpace(model) ? [] : [new(model)]
                };
                profile.Validate();
                profiles.Add(profile);
                pendingKey = true;
            }
            catch (ProviderException) { notice += " 原接口无效，原始值已保留在迁移备份。 / The invalid original URL remains in the migration backup."; }
        }
        if (pendingKey)
        {
            try { keyEndpoint = ModelServiceProfile.CanonicalEndpoint(endpoint); }
            catch (ProviderException) { pendingKey = false; }
        }

        var bindings = new List<ModelSelection>();
        if (original is { } root && root.TryGetProperty("modelConfiguration", out var oldConfig) &&
            oldConfig.ValueKind == JsonValueKind.Object && oldConfig.TryGetProperty("tasks", out var tasks) && tasks.ValueKind == JsonValueKind.Object)
        {
            foreach (var task in ModelTasks.All)
            {
                if (!tasks.TryGetProperty(task, out var binding) || binding.ValueKind != JsonValueKind.Object) continue;
                var serviceId = Read(binding, "serviceId", "");
                var modelId = Read(binding, "modelId", "");
                var profile = profiles.FirstOrDefault(p => p.Id == serviceId);
                if (profile is not null)
                {
                    if (modelId.Length == 0) modelId = profile.DefaultModel;
                    if (modelId.Length > 0 && !profile.Models.Any(m => m.Id == modelId))
                        profiles[profiles.IndexOf(profile)] = profile with { Models = [.. profile.Models, new(modelId)] };
                    bindings.Add(new(serviceId, modelId));
                }
                else if (serviceId == ModelConfiguration.Managed)
                    bindings.Add(new(serviceId, settings.ManagedModelPackageId));
                else if (serviceId.Length > 0)
                    bindings.Add(new(serviceId, modelId)); // A removed/unknown route prevents automatic selection.
            }
        }
        if (bindings.Count > 0) candidate = bindings.FirstOrDefault(b => profiles.Any(p => p.Id == b.ServiceId) || b.ServiceId == ModelConfiguration.Managed);
        else if (settings.ManagedRuntimeEnabled && ManagedMultimodalModels.TryGet(settings.ManagedModelPackageId, out _))
            candidate = new(ModelConfiguration.Managed, settings.ManagedModelPackageId);
        else if (profiles.FirstOrDefault() is { } first)
            candidate = new(first.Id, first.DefaultModel);

        // Only an unambiguous, previously tested service model may remain selected.
        ModelSelection? current = null;
        if (candidate is not null && bindings.Count > 0 && bindings.All(b => b == candidate) &&
            profiles.FirstOrDefault(p => p.Id == candidate.ServiceId)?.Models.FirstOrDefault(m => m.Id == candidate.ModelId)
                is { TextVerified: true, VisionVerified: true }) current = candidate;
        return settings with
        {
            ModelConfiguration = new()
            {
                Services = profiles.Where(p => p.IsRemote).ToArray(),
                LocalServices = profiles.Where(p => !p.IsRemote).Select(p => p with { Backend = InferBackend(p.Endpoint) }).ToArray(),
                CurrentModel = current, PendingModel = current is null ? candidate : null,
                LegacyCredentialPending = pendingKey, LegacyCredentialEndpoint = keyEndpoint,
                MigrationNotice = notice
            }
        };
    }

    private static string InferBackend(string endpoint) => new Uri(endpoint).Port switch
    { 11434 => "ollama", 1234 => "lm-studio", _ => "openai-compatible" };

    private static string Read(JsonElement? root, string key, string fallback) =>
        root is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback : fallback;
}
