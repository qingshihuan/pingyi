namespace PingYi.Core;

// State is evidence, not the currently selected processing scheme. Unknown is never green.
public enum ModeReadinessState { Unknown, Available, OnDemand, Unconfigured, NeedsAttention, Unverified }
public sealed record ModeReadiness(ModeReadinessState State, string Reason)
{
    public bool IsReady => State is ModeReadinessState.Available or ModeReadinessState.OnDemand;
    public static ModeReadiness Unknown { get; } = new(ModeReadinessState.Unknown, "not-checked");
}
public sealed record BasicModeEvidence(bool Configured, bool Managed, bool ModelFilesPresent,
    bool RuntimeInstalled, bool Running, bool? LocalServiceReachable);
public sealed record CloudModeEvidence(bool CredentialsReadable, bool GoogleKey,
    bool BaiduOcrKey, bool BaiduOcrSecret, bool BaiduAppId, bool BaiduTranslationSecret,
    bool RemoteEndpointConfigured);

public static class ModeReadinessPolicy
{
    public static ModeReadiness Lightweight(bool? ocr, bool? translation) =>
        ocr is null || translation is null ? ModeReadiness.Unknown :
        ocr == true && translation == true ? new(ModeReadinessState.Available, "lite-ready") :
        new(ModeReadinessState.NeedsAttention, "lite-missing");

    public static ModeReadiness Basic(BasicModeEvidence value)
    {
        if (!value.Configured) return new(ModeReadinessState.Unconfigured, "basic-unconfigured");
        if (!value.Managed) return value.LocalServiceReachable switch
        {
            true => new(ModeReadinessState.Unverified, "local-vision-unverified"),
            false => new(ModeReadinessState.NeedsAttention, "local-unreachable"),
            _ => ModeReadiness.Unknown
        };
        if (!value.ModelFilesPresent) return new(ModeReadinessState.NeedsAttention, "model-missing");
        if (!value.RuntimeInstalled) return new(ModeReadinessState.NeedsAttention, "runtime-missing");
        return value.Running ? new(ModeReadinessState.Available, "basic-running")
            : new(ModeReadinessState.OnDemand, "basic-on-demand");
    }

    public static ModeReadiness Cloud(CloudModeEvidence value)
    {
        // Configured credentials are not proof of internet reachability, quota or valid credentials.
        if (value.RemoteEndpointConfigured || value.GoogleKey ||
            (value.BaiduOcrKey && value.BaiduOcrSecret && value.BaiduAppId && value.BaiduTranslationSecret))
            return new(ModeReadinessState.Unverified, "cloud-unverified");
        if (!value.CredentialsReadable) return new(ModeReadinessState.Unknown, "credentials-unreadable");
        if (value.BaiduOcrKey || value.BaiduOcrSecret || value.BaiduAppId || value.BaiduTranslationSecret)
            return new(ModeReadinessState.NeedsAttention, "cloud-incomplete");
        return new(ModeReadinessState.Unconfigured, "cloud-unconfigured");
    }

    public static bool HasRemoteEndpoint(AppSettings settings) =>
        AppSettings.TryParseChatCompletionsEndpoint(settings.CustomTranslationEndpoint, out var uri) &&
        !uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) &&
        !string.IsNullOrWhiteSpace(settings.CustomTranslationModel);

    public static bool HasLocalConfiguration(AppSettings settings) =>
        AppSettings.TryParseChatCompletionsEndpoint(settings.CustomTranslationEndpoint, out var uri) &&
        uri.IsLoopback && string.IsNullOrEmpty(uri.UserInfo) && !string.IsNullOrWhiteSpace(settings.CustomTranslationModel) &&
        (RuntimePolicy.HasConfiguredManagedRuntime(settings) ||
         (settings.InitialSetupCompleted &&
          (settings.OcrProviderId == "local-vlm-ocr" || settings.TranslationProviderId == "custom-chat" ||
           settings.CustomTranslationEndpoint != AppSettings.DefaultCustomTranslationEndpoint ||
           settings.CustomTranslationModel != AppSettings.DefaultCustomTranslationModel)));
}
