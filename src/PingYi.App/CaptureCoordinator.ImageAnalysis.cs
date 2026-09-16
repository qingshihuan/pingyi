using Avalonia.Threading;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public sealed partial class CaptureCoordinator
{
    private Task RetryAsImageAnalysisAsync(ResultWindow window, CapturePurpose purpose)
    {
        window.SetPurpose(purpose);
        return RetrySelectionAsync(window);
    }

    private async Task<ProviderAvailability> WaitForModelAsync(OperationContext operation, ResultWindow window,
        CancellationToken token, bool forAnalysis = false)
    {
        var active = true;
        var progress = new Progress<ManagedModelProgress>(value =>
        {
            // Queued UI callbacks must not replace OCR/analysis feedback after startup ends.
            if (!active || !IsCurrent(operation)) return;
            window.UpdateLoadingStatus(value.Phase == "fallback"
                ? UiText.Get("String.ModelCpuFallback")
                : value.ElapsedSeconds is { } seconds
                    ? string.Format(UiText.Get("String.ModelLoadingElapsed"), (int)seconds)
                    : UiText.Get("String.ModelLoadingWait"));
        });
        try { return await services.WaitForManagedRuntimeAsync(token, forAnalysis, progress); }
        finally { active = false; }
    }

    private async Task ProcessImageAnalysisAsync(OperationContext operation, ResultWindow window, ImageFrame image)
    {
        var snapshot = services.Settings;
        var purpose = window.Purpose;
        var language = UiText.CurrentLanguage;
        try
        {
            EnsureCurrent(operation);
            if (!AppSettings.TryParseChatCompletionsEndpoint(snapshot.CustomTranslationEndpoint, out var endpoint) ||
                string.IsNullOrWhiteSpace(snapshot.CustomTranslationModel))
                throw new ProviderException("image_analysis_configuration", UiText.Get("String.VisionConfiguration"));
            if (!AppSettings.IsChatCompletionsTransportAllowed(endpoint))
                throw new ProviderException("custom_endpoint_insecure_transport", UiText.Get("String.VisionHttps"));
            var privacy = endpoint.IsLoopback ? UiText.Get("String.VisionLocal")
                : string.Format(UiText.Get("String.VisionRemote"), endpoint.GetLeftPart(UriPartial.Authority));
            window.SetLoading(UiText.Get("String.AnalysisPreparing"), privacy);
            if (!endpoint.IsLoopback && !await ImageAnalysisConsentWindow.ConfirmAsync(window, endpoint,
                    snapshot.CustomTranslationModel, operation.Token))
            {
                EnsureCurrent(operation);
                window.SetAnalysisCancelled(UiText.Get("String.VisionNotSent"));
                return;
            }
            EnsureCurrent(operation);
            if (RuntimePolicy.HasConfiguredManagedRuntime(snapshot))
            {
                window.UpdateLoadingStatus(UiText.Get("String.ModelLoadingWait"));
                await WithTimeoutAsync(token => WaitForModelAsync(operation, window, token, true), ManagedRuntimeTimeout, operation.Token,
                    "managed_runtime_timeout", UiText.Get("String.ModelStartTimeout"));
            }
            EnsureCurrent(operation);
            // Consent applies only to this endpoint/model. A settings change cannot redirect an upload.
            if (services.Settings.CustomTranslationEndpoint != snapshot.CustomTranslationEndpoint ||
                services.Settings.CustomTranslationModel != snapshot.CustomTranslationModel)
                throw new ProviderException("image_analysis_configuration_changed", UiText.Get("String.VisionConfigurationChanged"));
            window.UpdateLoadingStatus(UiText.Get(purpose == CapturePurpose.DescribeImage
                ? "String.AnalysisDescribing" : "String.AnalysisPrompting"));
            var result = await services.CreateImageAnalyzer(snapshot).AnalyzeAsync(image,
                new ImageAnalysisOptions(purpose, language), operation.Token);
            EnsureCurrent(operation);
            window.SetAnalysisResult(result);
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            EnsureCurrent(operation);
            window.SetError(UiText.Error(exception));
        }
    }
}
