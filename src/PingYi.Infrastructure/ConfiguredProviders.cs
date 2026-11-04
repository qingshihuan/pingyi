using PingYi.Core;

namespace PingYi.Infrastructure;

/// <summary>Resolve once per operation; neither an editor change nor a provider switch can redirect a request.</summary>
public static class ConfiguredProviders
{
    public static ITranslationProvider Translation(HttpClient client, ISecretStore secrets, AppSettings settings)
    {
        var route = ConfigurationRouting.Resolve(settings, ModelTasks.Translation);
        var provider = new ChatCompatibleTranslationProvider(client, new ProfileSecretStore(secrets, Credential(route)), () => route.Settings);
        return route.IsExplicit ? new RoutedTranslation(provider, Metadata(provider.Metadata, route)) : provider;
    }
    public static IOcrProvider Ocr(HttpClient client, ISecretStore secrets, AppSettings settings)
    {
        var route = ConfigurationRouting.Resolve(settings, ModelTasks.Ocr);
        var scoped = new ProfileSecretStore(secrets, Credential(route));
        var provider = new ChatCompatibleOcrProvider(client, scoped, () => route.Settings,
            new ChatCompatibleTranslationProvider(client, scoped, () => route.Settings));
        return route.IsExplicit ? new RoutedOcr(provider, Metadata(provider.Metadata, route)) : provider;
    }
    public static IImageAnalysisProvider Image(HttpClient client, ISecretStore secrets, AppSettings settings, CapturePurpose purpose)
    {
        var route = ConfigurationRouting.Resolve(settings, ModelTasks.ForPurpose(purpose));
        return new ChatCompatibleImageAnalysisProvider(client, new ProfileSecretStore(secrets, Credential(route)), route.Settings);
    }
    private static string Credential(ResolvedModelRoute route) => route.IsManaged ? "" : route.CredentialRef;
    private static ProviderMetadata Metadata(ProviderMetadata metadata, ResolvedModelRoute route) => metadata with
    {
        DisplayName = route.Label,
        Location = new Uri(route.Settings.CustomTranslationEndpoint).IsLoopback ? ProviderExecutionLocation.Local : ProviderExecutionLocation.Cloud
    };
    private sealed class RoutedTranslation(ITranslationProvider inner, ProviderMetadata metadata) : ITranslationProvider
    {
        public ProviderMetadata Metadata => metadata;
        public ValueTask<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) => inner.GetAvailabilityAsync(cancellationToken);
        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default) => inner.TranslateAsync(request, cancellationToken);
    }
    private sealed class RoutedOcr(IOcrProvider inner, ProviderMetadata metadata) : IOcrProvider
    {
        public ProviderMetadata Metadata => metadata;
        public ValueTask<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) => inner.GetAvailabilityAsync(cancellationToken);
        public Task<OcrResult> RecognizeAsync(ImageFrame image, OcrOptions options, CancellationToken cancellationToken = default) => inner.RecognizeAsync(image, options, cancellationToken);
    }
}
