namespace PingYi.Core;

public static class ConfigurationModes
{
    public const string Hybrid = "hybrid";
    public const string LocalFirst = "local-first";
    public const string CloudFirst = "cloud-first";
}

public static class ModelServiceKinds
{
    public const string Local = "local";
    public const string Remote = "remote";
    public const string BuiltIn = "built-in";
}

public static class ModelTaskIds
{
    public const string Ocr = "ocr";
    public const string Translation = "translation";
    public const string ImageDescription = "image-description";
    public const string PromptReconstruction = "prompt-reconstruction";
    public const string QrCode = "qr-code";
}

public static class ServiceCredentialRefs
{
    public static string ForService(string serviceId) => "service:" + serviceId.Trim() + ":api-key";
}

public sealed record ModelServiceProfile
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = ModelServiceKinds.Remote;
    public string Protocol { get; init; } = ModelApiProtocols.ChatCompletions;
    // Store the canonical Chat Completions endpoint; Responses derives /responses from it.
    public string Endpoint { get; init; } = string.Empty;
    public string CredentialRef { get; init; } = string.Empty;
    public string DefaultModelId { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
    public IReadOnlyList<string> Capabilities { get; init; } = ["text-generation"];
    public string? ProviderId { get; init; }
}

public sealed record TaskModelRoute
{
    public string ServiceId { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string? FallbackServiceId { get; init; }
    public string? FallbackModelId { get; init; }
    public bool AllowFallback { get; init; }
    public bool RequireImageInput { get; init; }
}

public sealed record TaskRoutingConfiguration
{
    public TaskModelRoute Ocr { get; init; } = new();
    public TaskModelRoute Translation { get; init; } = new();
    public TaskModelRoute ImageDescription { get; init; } = new();
    public TaskModelRoute PromptReconstruction { get; init; } = new();
    public TaskModelRoute QrCode { get; init; } = new()
    {
        ServiceId = "builtin:qr",
        ModelId = "zxing"
    };

    public TaskModelRoute GetRoute(string taskId) => taskId switch
    {
        ModelTaskIds.Ocr => Ocr,
        ModelTaskIds.Translation => Translation,
        ModelTaskIds.ImageDescription => ImageDescription,
        ModelTaskIds.PromptReconstruction => PromptReconstruction,
        ModelTaskIds.QrCode => QrCode,
        _ => throw new ArgumentOutOfRangeException(nameof(taskId), taskId, "Unknown task route.")
    };

    public TaskRoutingConfiguration WithRoute(string taskId, TaskModelRoute route) => taskId switch
    {
        ModelTaskIds.Ocr => this with { Ocr = route },
        ModelTaskIds.Translation => this with { Translation = route },
        ModelTaskIds.ImageDescription => this with { ImageDescription = route },
        ModelTaskIds.PromptReconstruction => this with { PromptReconstruction = route },
        ModelTaskIds.QrCode => this with { QrCode = route },
        _ => throw new ArgumentOutOfRangeException(nameof(taskId), taskId, "Unknown task route.")
    };
}

public sealed record ModelCatalogEntry
{
    public string ServiceId { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public string DiscoverySource { get; init; } = "manual";
    public DateTimeOffset? LastDiscoveredUtc { get; init; }
    public bool Available { get; init; } = true;
}

public sealed record RuntimeConfiguration
{
    public string Backend { get; init; } = ManagedRuntimeBackends.Default.Id;
    public string Device { get; init; } = RuntimeDeviceChoice.Automatic;
    public string ModelPackageId { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public bool AllowMirrors { get; init; }
    public string MirrorPrefixes { get; init; } = string.Empty;
}

public sealed record AppConfiguration
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public string Mode { get; init; } = ConfigurationModes.Hybrid;
    public IReadOnlyList<ModelServiceProfile> Services { get; init; } = [];
    public IReadOnlyList<ModelCatalogEntry> Models { get; init; } = [];
    public TaskRoutingConfiguration Tasks { get; init; } = new();
    public RuntimeConfiguration Runtime { get; init; } = new();

    public static AppConfiguration Empty { get; } = new();

    public AppConfiguration Normalize(AppSettings legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        var hasProfiles = Services.Count > 0;
        var services = Services
            .Where(service => !string.IsNullOrWhiteSpace(service.Id))
            .GroupBy(service => service.Id.Trim(), StringComparer.Ordinal)
            .Select(group =>
            {
                var service = group.Last();
                return service with
                {
                    Id = group.Key,
                    Name = string.IsNullOrWhiteSpace(service.Name) ? group.Key : service.Name.Trim(),
                    Kind = service.Kind is ModelServiceKinds.Local or ModelServiceKinds.BuiltIn
                        ? service.Kind : ModelServiceKinds.Remote,
                    Protocol = ModelApiProtocols.Normalize(service.Protocol),
                    Endpoint = service.Endpoint?.Trim() ?? string.Empty,
                    CredentialRef = string.IsNullOrWhiteSpace(service.CredentialRef)
                        ? ServiceCredentialRefs.ForService(group.Key) : service.CredentialRef.Trim(),
                    DefaultModelId = service.DefaultModelId?.Trim() ?? string.Empty,
                    Capabilities = (service.Capabilities ?? []).Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                };
            })
            .ToList();

        if (services.Count == 0)
            services.Add(CreateLegacyService(legacy));

        // Keep the compatibility profile synchronized with the legacy settings fields
        // while new profiles remain independent.
        services = services.Select(service => service.Id == "legacy-custom"
            ? service with
            {
                Name = Uri.TryCreate(legacy.CustomTranslationEndpoint, UriKind.Absolute, out var legacyUri) &&
                       legacyUri.IsLoopback ? "Existing local model service" : "Existing custom API",
                Kind = Uri.TryCreate(legacy.CustomTranslationEndpoint, UriKind.Absolute, out legacyUri) &&
                       legacyUri.IsLoopback ? ModelServiceKinds.Local : ModelServiceKinds.Remote,
                Endpoint = AppSettings.NormalizeChatCompletionsEndpoint(legacy.CustomTranslationEndpoint),
                Protocol = ModelApiProtocols.Normalize(legacy.CustomTranslationApiProtocol),
                DefaultModelId = legacy.CustomTranslationModel?.Trim() ?? string.Empty,
                CredentialRef = SecretKeys.CustomTranslationApiKey
            }
            : service).ToList();

        var fallbackTasks = CreateLegacyTasks(legacy);
        var sourceTasks = !hasProfiles ? fallbackTasks : Tasks ?? fallbackTasks;
        static TaskModelRoute NormalizeRoute(TaskModelRoute? route, TaskModelRoute fallback)
        {
            var value = route ?? fallback;
            return value with
            {
                ServiceId = string.IsNullOrWhiteSpace(value.ServiceId) ? fallback.ServiceId : value.ServiceId.Trim(),
                ModelId = value.ModelId?.Trim() ?? string.Empty,
                FallbackServiceId = string.IsNullOrWhiteSpace(value.FallbackServiceId)
                    ? fallback.FallbackServiceId : value.FallbackServiceId.Trim(),
                FallbackModelId = value.FallbackModelId?.Trim() ?? string.Empty
            };
        }

        static TaskModelRoute NormalizeLegacyModelRoute(TaskModelRoute route, AppSettings settings) =>
            route.ServiceId == "legacy-custom"
                ? route with { ModelId = settings.CustomTranslationModel?.Trim() ?? string.Empty }
                : route;

        var tasks = new TaskRoutingConfiguration
        {
            Ocr = NormalizeLegacyModelRoute(NormalizeRoute(sourceTasks.Ocr, fallbackTasks.Ocr), legacy),
            Translation = NormalizeLegacyModelRoute(NormalizeRoute(sourceTasks.Translation, fallbackTasks.Translation), legacy),
            ImageDescription = NormalizeLegacyModelRoute(NormalizeRoute(sourceTasks.ImageDescription, fallbackTasks.ImageDescription), legacy),
            PromptReconstruction = NormalizeLegacyModelRoute(NormalizeRoute(sourceTasks.PromptReconstruction, fallbackTasks.PromptReconstruction), legacy),
            QrCode = NormalizeRoute(sourceTasks.QrCode, fallbackTasks.QrCode)
        };

        var runtime = !hasProfiles
            ? new RuntimeConfiguration
            {
                Backend = legacy.ManagedRuntimeBackend,
                Device = legacy.ManagedRuntimeDevice,
                ModelPackageId = legacy.ManagedModelPackageId,
                Enabled = legacy.ManagedRuntimeEnabled,
                AllowMirrors = legacy.RuntimeAllowMirrors,
                MirrorPrefixes = legacy.RuntimeMirrorPrefixes
            }
            : Runtime ?? new RuntimeConfiguration();
        return this with
        {
            Version = CurrentVersion,
            Mode = Mode is ConfigurationModes.LocalFirst or ConfigurationModes.CloudFirst
                ? Mode : ConfigurationModes.Hybrid,
            Services = services,
            Models = (Models ?? []).Where(model => !string.IsNullOrWhiteSpace(model.ServiceId) &&
                    !string.IsNullOrWhiteSpace(model.ModelId))
                .GroupBy(model => (model.ServiceId.Trim(), model.ModelId.Trim()))
                .Select(group => group.Last() with
                {
                    ServiceId = group.Key.Item1,
                    ModelId = group.Key.Item2,
                    Capabilities = (group.Last().Capabilities ?? []).Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                }).ToArray(),
            Tasks = tasks,
            Runtime = runtime with
            {
                Backend = ManagedRuntimeBackends.Normalize(runtime.Backend),
                Device = RuntimeDeviceChoice.Normalize(runtime.Device),
                ModelPackageId = ManagedMultimodalModels.TryGet(runtime.ModelPackageId, out _)
                    ? runtime.ModelPackageId.Trim() : string.Empty,
                MirrorPrefixes = runtime.MirrorPrefixes?.Trim() ?? string.Empty
            }
        };
    }

    public static AppConfiguration FromLegacy(AppSettings settings) => new AppConfiguration
    {
        Mode = ConfigurationModes.Hybrid,
        Services = [CreateLegacyService(settings)],
        Tasks = CreateLegacyTasks(settings),
        Runtime = new RuntimeConfiguration
        {
            Backend = settings.ManagedRuntimeBackend,
            Device = settings.ManagedRuntimeDevice,
            ModelPackageId = settings.ManagedModelPackageId,
            Enabled = settings.ManagedRuntimeEnabled,
            AllowMirrors = settings.RuntimeAllowMirrors,
            MirrorPrefixes = settings.RuntimeMirrorPrefixes
        }
    };

    private static ModelServiceProfile CreateLegacyService(AppSettings settings)
    {
        var endpoint = AppSettings.NormalizeChatCompletionsEndpoint(settings.CustomTranslationEndpoint);
        var local = Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed) && parsed.IsLoopback;
        var capabilities = settings.OcrProviderId == "local-vlm-ocr"
            ? new[] { "text-generation", "image-input" }
            : new[] { "text-generation" };
        return new ModelServiceProfile
        {
            Id = "legacy-custom",
            Name = local ? "Existing local model service" : "Existing custom API",
            Kind = local ? ModelServiceKinds.Local : ModelServiceKinds.Remote,
            Protocol = ModelApiProtocols.Normalize(settings.CustomTranslationApiProtocol),
            Endpoint = endpoint,
            CredentialRef = SecretKeys.CustomTranslationApiKey,
            DefaultModelId = settings.CustomTranslationModel,
            Capabilities = capabilities
        };
    }

    private static TaskRoutingConfiguration CreateLegacyTasks(AppSettings settings)
    {
        const string legacyService = "legacy-custom";
        var ocrService = settings.OcrProviderId == "local-vlm-ocr"
            ? legacyService : "provider:" + settings.OcrProviderId;
        var translationService = settings.TranslationProviderId == "custom-chat"
            ? legacyService : "provider:" + settings.TranslationProviderId;
        return new TaskRoutingConfiguration
        {
            Ocr = new TaskModelRoute
            {
                ServiceId = ocrService,
                ModelId = settings.OcrProviderId == "local-vlm-ocr" ? settings.CustomTranslationModel : string.Empty,
                FallbackServiceId = "provider:local-paddle",
                AllowFallback = true,
                RequireImageInput = settings.OcrProviderId == "local-vlm-ocr"
            },
            Translation = new TaskModelRoute
            {
                ServiceId = translationService,
                ModelId = settings.TranslationProviderId == "custom-chat" ? settings.CustomTranslationModel : string.Empty,
                FallbackServiceId = "provider:local-argos",
                AllowFallback = true
            },
            ImageDescription = new TaskModelRoute
            {
                ServiceId = legacyService,
                ModelId = settings.CustomTranslationModel,
                RequireImageInput = true
            },
            PromptReconstruction = new TaskModelRoute
            {
                ServiceId = legacyService,
                ModelId = settings.CustomTranslationModel,
                RequireImageInput = true
            },
            QrCode = new TaskModelRoute { ServiceId = "builtin:qr", ModelId = "zxing" }
        };
    }
}
