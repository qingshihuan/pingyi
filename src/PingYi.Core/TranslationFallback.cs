namespace PingYi.Core;

public sealed record TranslationExecution(
    TranslationResult Result,
    ProviderMetadata Provider,
    bool UsedFallback);

public static class TranslationFallback
{
    public static async Task<TranslationExecution> ExecuteAsync(
        ITranslationProvider primary,
        ITranslationProvider offlineFallback,
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var availability = await primary.GetAvailabilityAsync(cancellationToken);
            if (!availability.IsAvailable)
            {
                throw new ProviderException(
                    "translation_unavailable",
                    availability.Message ?? "翻译引擎不可用。");
            }

            var result = await primary.TranslateAsync(request, cancellationToken);
            return new TranslationExecution(result, primary.Metadata, UsedFallback: false);
        }
        catch (Exception primaryFailure) when (
            primaryFailure is not OperationCanceledException &&
            !cancellationToken.IsCancellationRequested &&
            primary.Metadata.Id != offlineFallback.Metadata.Id)
        {
            var fallbackRequest = ResolveFallbackRequest(offlineFallback.Metadata, request);
            if (fallbackRequest is null)
            {
                var source = request.SourceLanguage == LanguageCatalog.Auto
                    ? "自动检测语言"
                    : LanguageCatalog.GetDisplayName(request.SourceLanguage);
                var target = request.TargetLanguage == LanguageCatalog.AutoOpposite
                    ? "自动翻译"
                    : LanguageCatalog.GetDisplayName(request.TargetLanguage);
                throw new ProviderException(
                    "translation_fallback_language_unsupported",
                    $"{primaryFailure.Message}；{offlineFallback.Metadata.DisplayName} 不支持 {source} → {target}，无法离线回退。",
                    primaryFailure);
            }

            try
            {
                var fallbackAvailability = await offlineFallback.GetAvailabilityAsync(cancellationToken);
                if (!fallbackAvailability.IsAvailable)
                {
                    throw new ProviderException(
                        "translation_fallback_unavailable",
                        fallbackAvailability.Message ?? "本地离线翻译不可用。");
                }

                var result = await offlineFallback.TranslateAsync(fallbackRequest, cancellationToken);
                return new TranslationExecution(result, offlineFallback.Metadata, UsedFallback: true);
            }
            catch (Exception fallbackFailure) when (fallbackFailure is not OperationCanceledException)
            {
                throw new ProviderException(
                    "translation_primary_and_fallback_failed",
                    $"{primaryFailure.Message}；离线回退也不可用：{fallbackFailure.Message}",
                    primaryFailure);
            }
        }
    }

    private static TranslationRequest? ResolveFallbackRequest(
        ProviderMetadata provider,
        TranslationRequest request)
    {
        var sourceLanguage = request.SourceLanguage == LanguageCatalog.Auto
            ? TextProcessing.DetectLanguageForOfflineFallback(request.Text)
            : request.SourceLanguage;
        return provider.SupportedLanguages.Contains(sourceLanguage, StringComparer.OrdinalIgnoreCase) &&
               provider.SupportedLanguages.Contains(request.TargetLanguage, StringComparer.OrdinalIgnoreCase)
            ? request with { SourceLanguage = sourceLanguage }
            : null;
    }
}
