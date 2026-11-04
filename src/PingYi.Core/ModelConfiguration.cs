using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace PingYi.Core;

public static class ModelTasks
{
    public const string Ocr = "ocr", Translation = "translation", Description = "description", Prompt = "prompt";
    public static IReadOnlyList<string> All { get; } = [Ocr, Translation, Description, Prompt];
    public static string ForPurpose(CapturePurpose purpose) => purpose switch
    {
        CapturePurpose.DescribeImage => Description,
        CapturePurpose.ReconstructPrompt => Prompt,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose))
    };
}

/// <summary>Unknown is not unsupported or verified. Catalog discovery alone proves neither capability.</summary>
public sealed record ModelCatalogEntry(string Id, bool TextVerified = false, bool VisionVerified = false);

public sealed record ModelServiceProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Model service";
    public string Endpoint { get; init; } = AppSettings.DefaultCustomTranslationEndpoint;
    public string Protocol { get; init; } = ModelApiProtocols.ChatCompletions;
    public string DefaultModel { get; init; } = "";
    public ModelCatalogEntry[] Models { get; init; } = [];

    [JsonIgnore] public string CredentialRef => "model-service-" + Id + "-" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalEndpoint(Endpoint))))[..24].ToLowerInvariant();
    [JsonIgnore] public bool IsRemote => !new Uri(CanonicalEndpoint(Endpoint)).IsLoopback;

    public static string CanonicalEndpoint(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ProviderException("profile_endpoint_invalid", "服务地址无效，不得包含凭据、查询参数或片段。 / Invalid service URL; credentials, queries and fragments are not allowed.");
        if (!AppSettings.IsChatCompletionsTransportAllowed(uri))
            throw new ProviderException("profile_https_required", "远程服务必须使用 HTTPS。 / Remote services require HTTPS.");
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/responses", StringComparison.OrdinalIgnoreCase)) path = path[..^10];
        else if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) path = path[..^17];
        if (path.Length == 0) path = "/v1";
        return new UriBuilder(uri) { Path = path + "/chat/completions" }.Uri.AbsoluteUri;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 64 || Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ProviderException("profile_id_invalid", "服务标识无效。 / Invalid service ID.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 120 || Name.Any(char.IsControl))
            throw new ProviderException("profile_name_invalid", "请填写有效的服务名称。 / Enter a valid service name.");
        _ = CanonicalEndpoint(Endpoint);
        if (Protocol is not (ModelApiProtocols.ChatCompletions or ModelApiProtocols.Responses) ||
            !IsRemote && Protocol != ModelApiProtocols.ChatCompletions)
            throw new ProviderException("profile_protocol_invalid", "本机服务使用 Chat Completions；请选择有效协议。 / Local services use Chat Completions; select a valid protocol.");
        if (Models is null || Models.Length > 5000 || Models.Any(m => m is null || string.IsNullOrWhiteSpace(m.Id) || m.Id.Length > 512 || m.Id.Any(char.IsControl)))
            throw new ProviderException("profile_models_invalid", "模型目录无效。 / Invalid model catalog.");
        if (DefaultModel is null || DefaultModel.Length > 512 || DefaultModel.Any(char.IsControl))
            throw new ProviderException("profile_model_invalid", "模型 ID 无效。 / Invalid model ID.");
    }

    public bool Equals(ModelServiceProfile? other) => other is not null && Id == other.Id && Name == other.Name &&
        Endpoint == other.Endpoint && Protocol == other.Protocol && DefaultModel == other.DefaultModel &&
        (Models ?? []).SequenceEqual(other.Models ?? []);
    public override int GetHashCode() => HashCode.Combine(Id, Name, Endpoint, Protocol, DefaultModel, Models?.Length ?? 0);
    public override string ToString() => Name;
}

/// <summary>Empty service ID follows the old configuration. No automatic cross-cloud fallback.</summary>
public sealed record ModelTaskBinding(string ServiceId = "", string ModelId = "", bool AllowOfflineFallback = true);

public sealed record ModelTaskRoutes
{
    public ModelTaskBinding Ocr { get; init; } = new();
    public ModelTaskBinding Translation { get; init; } = new();
    public ModelTaskBinding Description { get; init; } = new(AllowOfflineFallback: false);
    public ModelTaskBinding Prompt { get; init; } = new(AllowOfflineFallback: false);
    public ModelTaskBinding Get(string task) => task switch
    {
        ModelTasks.Ocr => Ocr, ModelTasks.Translation => Translation,
        ModelTasks.Description => Description, ModelTasks.Prompt => Prompt,
        _ => throw new ArgumentOutOfRangeException(nameof(task))
    };
    public ModelTaskRoutes With(string task, ModelTaskBinding binding) => task switch
    {
        ModelTasks.Ocr => this with { Ocr = binding }, ModelTasks.Translation => this with { Translation = binding },
        ModelTasks.Description => this with { Description = binding }, ModelTasks.Prompt => this with { Prompt = binding },
        _ => throw new ArgumentOutOfRangeException(nameof(task))
    };
}

public sealed record ModelConfiguration
{
    public const string Managed = "$managed", Offline = "$offline", Imported = "imported-api";
    public ModelServiceProfile[] Services { get; init; } = [];
    public ModelTaskRoutes Tasks { get; init; } = new();
    public bool LegacyCredentialPending { get; init; }
    [JsonIgnore] public bool HasOverrides => ModelTasks.All.Any(t => Tasks.Get(t).ServiceId.Length > 0);
    public void Validate()
    {
        if (Services is null || Services.Length > 32 || Services.Any(s => s is null) || Tasks is null ||
            Services.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != Services.Length)
            throw new ProviderException("configuration_invalid", "服务配置无效或存在重复标识。 / Invalid configuration or duplicate service IDs.");
        foreach (var service in Services) service.Validate();
        foreach (var task in ModelTasks.All)
        {
            var binding = Tasks.Get(task);
            if (binding is null || binding.ServiceId is null || binding.ModelId is null || binding.ModelId.Length > 512 || binding.ModelId.Any(char.IsControl))
                throw new ProviderException("task_binding_invalid", "任务绑定无效。 / Invalid task binding.");
            if (binding.ServiceId == Offline && task is not (ModelTasks.Ocr or ModelTasks.Translation))
                throw new ProviderException("task_requires_vision", "图片描述与提示词反推需要视觉模型。 / This task requires a vision model.");
            if (binding.ServiceId.Length > 0 && binding.ServiceId is not (Managed or Offline) && !Services.Any(s => s.Id == binding.ServiceId))
                throw new ProviderException("task_service_missing", "任务引用的服务不存在，不能静默改用其他服务。 / Referenced service is missing; no silent rerouting.");
        }
    }
    public bool Equals(ModelConfiguration? other) => other is not null && Tasks == other.Tasks &&
        LegacyCredentialPending == other.LegacyCredentialPending && (Services ?? []).SequenceEqual(other.Services ?? []);
    public override int GetHashCode() => HashCode.Combine(Tasks, LegacyCredentialPending, Services?.Length ?? 0);
}

public static class ConfigurationMigration
{
    public static AppSettings Upgrade(AppSettings settings)
    {
        if (settings.ModelConfiguration is not null) return settings;
        var profile = new ModelServiceProfile
        {
            Id = ModelConfiguration.Imported, Name = "原有模型服务 / Imported service",
            Endpoint = ModelServiceProfile.CanonicalEndpoint(settings.CustomTranslationEndpoint),
            Protocol = settings.CustomTranslationApiProtocol,
            DefaultModel = settings.CustomTranslationModel
        };
        return settings with { ModelConfiguration = new ModelConfiguration { Services = [profile], LegacyCredentialPending = true } };
    }
}

public sealed record ResolvedModelRoute(AppSettings Settings, string CredentialRef, string Label, bool IsManaged, bool IsExplicit);

public static class ConfigurationRouting
{
    public static ResolvedModelRoute Resolve(AppSettings settings, string task)
    {
        var binding = settings.ModelConfiguration?.Tasks.Get(task) ?? new ModelTaskBinding();
        var label = task == ModelTasks.Ocr ? settings.OcrProviderId : settings.TranslationProviderId;
        if (binding.ServiceId.Length == 0)
            return new(settings, SecretKeys.CustomTranslationApiKey, label,
                settings.ManagedRuntimeEnabled && settings.CustomTranslationEndpoint == AppSettings.ManagedModelEndpoint &&
                (task is ModelTasks.Description or ModelTasks.Prompt ||
                 task == ModelTasks.Ocr && settings.OcrProviderId == "local-vlm-ocr" ||
                 task == ModelTasks.Translation && settings.TranslationProviderId == "custom-chat"), false);
        if (binding.ServiceId == ModelConfiguration.Offline)
        {
            if (task is not (ModelTasks.Ocr or ModelTasks.Translation))
                throw new ProviderException("task_requires_vision", "此任务不能使用轻量引擎。 / This task requires a vision model.");
            return new(settings with { OcrProviderId = "local-paddle", TranslationProviderId = "local-argos" },
                "", "轻量离线 / Lightweight offline", false, true);
        }
        if (binding.ServiceId == ModelConfiguration.Managed)
        {
            if (!settings.ManagedRuntimeEnabled || !ManagedMultimodalModels.TryGet(settings.ManagedModelPackageId, out var model))
                throw new ProviderException("task_managed_unconfigured", "请先在运行环境中安装并配置本机模型。 / Configure the managed local model first.");
            return new(settings with { CustomTranslationEndpoint = AppSettings.ManagedModelEndpoint,
                CustomTranslationModel = model.ModelAlias, CustomTranslationApiProtocol = ModelApiProtocols.ChatCompletions,
                OcrProviderId = "local-vlm-ocr", TranslationProviderId = "custom-chat" },
                "", "本机托管模型 / Managed local model", true, true);
        }
        var profile = settings.ModelConfiguration?.Services.FirstOrDefault(s => s.Id == binding.ServiceId)
            ?? throw new ProviderException("task_service_missing", "任务服务已丢失。 / Task service is missing.");
        profile.Validate();
        var modelId = string.IsNullOrWhiteSpace(binding.ModelId) ? profile.DefaultModel : binding.ModelId;
        if (string.IsNullOrWhiteSpace(modelId)) throw new ProviderException("task_model_missing", "请为任务选择模型。 / Select a model for this task.");
        return new(settings with { CustomTranslationEndpoint = ModelServiceProfile.CanonicalEndpoint(profile.Endpoint),
            CustomTranslationModel = modelId, CustomTranslationApiProtocol = profile.Protocol,
            OcrProviderId = "local-vlm-ocr", TranslationProviderId = "custom-chat", ManagedRuntimeEnabled = false },
            profile.CredentialRef, profile.Name + " · " + new Uri(profile.Endpoint).GetLeftPart(UriPartial.Authority) + " · " + modelId,
            false, true);
    }
    public static bool AllowOfflineFallback(AppSettings settings, string task) =>
        settings.ModelConfiguration?.Tasks.Get(task).AllowOfflineFallback ?? true;
    public static string Fingerprint(AppSettings settings, string task)
    {
        var route = Resolve(settings, task);
        return string.Join('|', task, route.Settings.OcrProviderId, route.Settings.TranslationProviderId,
            route.Settings.CustomTranslationEndpoint, route.Settings.CustomTranslationModel,
            route.Settings.CustomTranslationApiProtocol, route.CredentialRef);
    }
}
