using PingYi.Core;

namespace PingYi.App;

internal static class VisionErrors
{
    public static string Describe(Exception exception)
    {
        if (!UiText.IsEnglish || exception is not ProviderException provider) return UiText.Error(exception);
        return provider.Code switch
        {
            "managed_runtime_busy" => "The local model is still running but its health check did not respond. It was not restarted. Retry shortly.",
            "image_analysis_configuration" => UiText.Get("String.VisionConfiguration"),
            "image_analysis_configuration_changed" => UiText.Get("String.VisionConfigurationChanged"),
            "image_analysis_http" => "The vision endpoint rejected the request. Check image_url support, model vision components, credentials and quota. No OCR fallback was used.",
            "image_analysis_empty" or "image_analysis_schema" => "The vision model returned no usable content. Check the model and compatible response format, then retry.",
            "image_analysis_timeout" => "Image analysis timed out. Try a smaller image or vision model, or check the compute backend.",
            "image_analysis_connection" => "Cannot connect to the vision service. Start the service and retry.",
            "image_analysis_image_invalid" => "The image is empty, invalid or too large. Capture a smaller area.",
            "image_analysis_response_large" => "The vision response exceeded the safety limit and was not read further.",
            _ => UiText.Error(exception)
        };
    }
}
