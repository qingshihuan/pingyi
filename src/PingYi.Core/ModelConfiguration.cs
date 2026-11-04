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

/// <summary>Unknown is not unsupported or verified. Discovery alone proves neither capability.</summary>
public sealed record ModelCatalogEntry(string Id, bool TextVerified = false, bool VisionVerified = false);

public sealed record ModelServiceProfile
{
    // Explicit optional constructor arguments preserve omitted fields with source-generated JSON.
    [JsonConstructor]
    public ModelServiceProfile(string? id = null, string name = "Model service",
        string endpoint = AppSettings.DefaultCustomTranslationEndpoint,
        string protocol = ModelApiProtocols.ChatCompletions, string defaultModel = "",
        string backend = "openai-compatible", ModelCatalogEntry[]? models = null)
    {
        Id = id ?? Guid.NewGuid().ToString("N"); Name = name; Endpoint = endpoint; Protocol = protocol;
        DefaultModel = defaultModel; Backend = backend; Models = models ?? [];
    }
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Model service";
    public string Endpoint { get; init; } = AppSettings.DefaultCustomTranslationEndpoint;
    public string Protocol { get; init; } = ModelApiProtocols.ChatCompletions;
    public string DefaultModel { get; init; } = "";
    public string Backend { get; init; } = "openai-compatible";
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
        var explicitResource = path.EndsWith("/responses", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase);
        if (path.EndsWith("/responses", StringComparison.OrdinalIgnoreCase)) path = path[..^10];
        else if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) path = path[..^17];
        if (path.Length == 0 && !explicitResource) path = "/v1";
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
        if (Models is null || Models.Length > 5000 || Models.Any(m => m is null || string.IsNullOrWhiteSpace(m.Id) || m.Id.Length > 512 || m.Id.Any(char.IsControl)) ||
            Models.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != Models.Length)
            throw new ProviderException("profile_models_invalid", "模型目录无效。 / Invalid model catalog.");
        if (DefaultModel is null || DefaultModel.Length > 512 || DefaultModel.Any(char.IsControl))
            throw new ProviderException("profile_model_invalid", "模型 ID 无效。 / Invalid model ID.");
    }

    public bool Equals(ModelServiceProfile? other) => other is not null && Id == other.Id && Name == other.Name &&
        Endpoint == other.Endpoint && Protocol == other.Protocol && DefaultModel == other.DefaultModel && Backend == other.Backend &&
        (Models ?? []).SequenceEqual(other.Models ?? []);
    public override int GetHashCode() => HashCode.Combine(Id, Name, Endpoint, Protocol, DefaultModel, Models?.Length ?? 0);
    public override string ToString() => Name;
}

/// <summary>One immutable selection is shared by every model task.</summary>
public sealed record ModelSelection(string ServiceId, string ModelId = "");

public sealed record ModelConfiguration
{
    [JsonConstructor]
    public ModelConfiguration(ModelServiceProfile[]? services = null, ModelServiceProfile[]? localServices = null,
        ModelSelection? currentModel = null, ModelSelection? pendingModel = null, string verifiedManagedModelId = "",
        bool legacyCredentialPending = false, string legacyCredentialEndpoint = "", string migrationNotice = "")
    {
        Services = services ?? []; LocalServices = localServices ?? []; CurrentModel = currentModel;
        PendingModel = pendingModel; VerifiedManagedModelId = verifiedManagedModelId;
        LegacyCredentialPending = legacyCredentialPending; LegacyCredentialEndpoint = legacyCredentialEndpoint;
        MigrationNotice = migrationNotice;
    }
    public const string Managed = "$managed", Imported = "imported-api";
    public ModelServiceProfile[] Services { get; init; } = [];
    public ModelServiceProfile[] LocalServices { get; init; } = [];
    public ModelSelection? CurrentModel { get; init; }
    public ModelSelection? PendingModel { get; init; }
    public string VerifiedManagedModelId { get; init; } = "";
    public bool LegacyCredentialPending { get; init; }
    public string LegacyCredentialEndpoint { get; init; } = "";
    public string MigrationNotice { get; init; } = "";
    [JsonIgnore] public IEnumerable<ModelServiceProfile> AllServices => Services.Concat(LocalServices);

    public void Validate()
    {
        if (Services is null || LocalServices is null || AllServices.Count() > 32 || AllServices.Any(s => s is null) ||
            AllServices.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != AllServices.Count())
            throw new ProviderException("configuration_invalid", "服务配置无效或标识重复。 / Invalid configuration or duplicate service IDs.");
        foreach (var service in AllServices) service.Validate();
        if (Services.Any(s => !s.IsRemote) || LocalServices.Any(s => s.IsRemote))
            throw new ProviderException("service_location_invalid", "云端 API 和本机服务必须分别管理。 / Manage cloud APIs and local services separately.");
        if (CurrentModel is not null) ValidateSelection(CurrentModel);
    }

    public void ValidateSelection(ModelSelection selection)
    {
        if (selection.ServiceId == Managed)
        {
            if (string.IsNullOrWhiteSpace(VerifiedManagedModelId) || selection.ModelId != VerifiedManagedModelId ||
                !ManagedMultimodalModels.TryGet(VerifiedManagedModelId, out _))
                throw new ProviderException("model_capability_unverified", "请先验证本地模型的图片输入和文字输出。 / Verify the local model's image input and text output first.");
            return;
        }
        var service = AllServices.FirstOrDefault(s => s.Id == selection.ServiceId)
            ?? throw new ProviderException("model_service_missing", "当前模型的服务不存在，请重新选择。 / Current model service is missing; select it again.");
        var entry = service.Models.FirstOrDefault(m => m.Id == selection.ModelId);
        if (entry is not { TextVerified: true, VisionVerified: true })
            throw new ProviderException("model_capability_unverified", "当前模型必须通过文字输出和图片输入测试；纯文本或未验证模型不能设为当前模型。 / The current model must pass text and image tests; text-only or unverified models cannot be selected.");
    }
    public bool Equals(ModelConfiguration? other) => other is not null && CurrentModel == other.CurrentModel &&
        PendingModel == other.PendingModel && VerifiedManagedModelId == other.VerifiedManagedModelId &&
        LegacyCredentialPending == other.LegacyCredentialPending && LegacyCredentialEndpoint == other.LegacyCredentialEndpoint &&
        MigrationNotice == other.MigrationNotice && (Services ?? []).SequenceEqual(other.Services ?? []) &&
        (LocalServices ?? []).SequenceEqual(other.LocalServices ?? []);
    public override int GetHashCode() => HashCode.Combine(CurrentModel, PendingModel, VerifiedManagedModelId, Services?.Length, LocalServices?.Length);
}

public sealed record ResolvedModelRoute(AppSettings Settings, string CredentialRef, string Label, bool IsManaged, bool IsExplicit = true);

public static class ConfigurationRouting
{
    public static ResolvedModelRoute Resolve(AppSettings settings, string task)
    {
        if (!ModelTasks.All.Contains(task)) throw new ArgumentOutOfRangeException(nameof(task));
        var config = settings.ModelConfiguration ?? new();
        config.Validate();
        var selection = config.CurrentModel ?? throw new ProviderException("current_model_missing",
            "请在设置中选择已验证的多模态当前模型。 / Select a verified multimodal current model in Settings.");
        if (selection.ServiceId == ModelConfiguration.Managed)
        {
            if (!settings.ManagedRuntimeEnabled || settings.ManagedModelPackageId != selection.ModelId ||
                !ManagedMultimodalModels.TryGet(selection.ModelId, out var model))
                throw new ProviderException("model_managed_unconfigured", "请先在本地模型管理中配置该模型。 / Configure this model under Local models first.");
            return new(settings with { CustomTranslationEndpoint = AppSettings.ManagedModelEndpoint,
                CustomTranslationModel = model.ModelAlias, CustomTranslationApiProtocol = ModelApiProtocols.ChatCompletions },
                "", model.LocalizedDisplayName, true);
        }
        var profile = config.AllServices.Single(s => s.Id == selection.ServiceId);
        return new(settings with { CustomTranslationEndpoint = ModelServiceProfile.CanonicalEndpoint(profile.Endpoint),
            CustomTranslationModel = selection.ModelId, CustomTranslationApiProtocol = profile.Protocol,
            ManagedRuntimeEnabled = false }, profile.CredentialRef,
            profile.Name + " · " + new Uri(profile.Endpoint).GetLeftPart(UriPartial.Authority) + " · " + selection.ModelId, false);
    }
    public static string Fingerprint(AppSettings settings, string task)
    {
        var route = Resolve(settings, task);
        return string.Join('|', route.Settings.CustomTranslationEndpoint, route.Settings.CustomTranslationModel,
            route.Settings.CustomTranslationApiProtocol, route.CredentialRef);
    }
}
