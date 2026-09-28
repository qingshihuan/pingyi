namespace PingYi.Core;

public sealed record BrowserRequest
{
    public string Operation { get; init; } = "status";
    public string Edition { get; init; } = "standard";
    public string? Text { get; init; }
    public string? Image { get; init; }
    public string SourceLanguage { get; init; } = "auto";
    public string TargetLanguage { get; init; } = "zh";
    public string? DetectedLanguage { get; init; }
    public string? RouteKey { get; init; }
    public bool AllowRemoteText { get; init; }
    public bool AllowRemoteImage { get; init; }
}

public sealed record BrowserStatus(string TranslationProvider, string OcrProvider,
    bool RemoteText, bool RemoteImage, string RouteKey, string TargetLanguage,
    IReadOnlyList<LanguageDefinition> Languages);

public sealed record BrowserResponse(bool Ok, string? Error = null,
    BrowserStatus? Status = null, string? Original = null, string? Translation = null,
    string? SourceLanguage = null, string? TargetLanguage = null);
