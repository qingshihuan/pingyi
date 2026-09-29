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
        CancellationToken cancellationToken = default,
        TimeSpan? primaryTimeout = null, TimeSpan? fallbackTimeout = null)
    {
        try
        {
            using var primaryDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            primaryDeadline.CancelAfter(primaryTimeout ?? TimeSpan.FromMinutes(2));
            var availability = await primary.GetAvailabilityAsync(primaryDeadline.Token);
            if (!availability.IsAvailable)
            {
                throw new ProviderException(
                    "translation_unavailable",
                    availability.Message ?? "翻译引擎不可用。");
            }

            var result = await primary.TranslateAsync(request, primaryDeadline.Token);
            return new TranslationExecution(result, primary.Metadata, UsedFallback: false);
        }
        catch (Exception primaryFailure) when (
            !cancellationToken.IsCancellationRequested &&
            primary.Metadata.Id != offlineFallback.Metadata.Id)
        {
            // An internal HTTP/phase timeout is not a user cancellation. Keep diagnostics authored.
            var reportedFailure = primaryFailure is OperationCanceledException
                ? new ProviderException("translation_primary_timeout", "主翻译引擎等待超时，尝试轻量回退。") : primaryFailure;
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
                    $"{reportedFailure.Message}；{offlineFallback.Metadata.DisplayName} 不支持 {source} → {target}，无法离线回退。",
                    reportedFailure);
            }

            try
            {
                using var fallbackDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                fallbackDeadline.CancelAfter(fallbackTimeout ?? TimeSpan.FromMinutes(2));
                var fallbackAvailability = await offlineFallback.GetAvailabilityAsync(fallbackDeadline.Token);
                if (!fallbackAvailability.IsAvailable)
                {
                    throw new ProviderException(
                        "translation_fallback_unavailable",
                        fallbackAvailability.Message ?? "本地离线翻译不可用。");
                }

                var result = await offlineFallback.TranslateAsync(fallbackRequest, fallbackDeadline.Token);
                return new TranslationExecution(result, offlineFallback.Metadata, UsedFallback: true);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ProviderException("translation_fallback_timeout", "主翻译引擎与轻量回退均未及时完成，可复制原文后重试。", reportedFailure);
            }
            catch (Exception fallbackFailure) when (fallbackFailure is not OperationCanceledException)
            {
                throw new ProviderException(
                    "translation_primary_and_fallback_failed",
                    $"{reportedFailure.Message}；离线回退也不可用：{fallbackFailure.Message}",
                    reportedFailure);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderException("translation_timeout", "翻译引擎等待超时，可复制原文后重试。");
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
