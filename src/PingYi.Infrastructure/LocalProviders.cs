using PingYi.Core;
using System.Text.Json.Nodes;

namespace PingYi.Infrastructure;

public sealed class ArgosTranslationProvider(EngineProcessClient engine) : ITranslationProvider
{
    public ProviderMetadata Metadata { get; } = new(
        "local-argos",
        "本地 Argos",
        ProviderExecutionLocation.Local,
        UploadsImage: false,
        RequiresSecret: false,
        ["zh", "en"]);

    public async ValueTask<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await engine.CallAsync("health", cancellationToken: cancellationToken);
            var installed = result.TryGetProperty("argos", out var value) && value.GetBoolean();
            if (!installed)
            {
                return new ProviderAvailability(false, "尚未安装 Argos Translate 引擎。");
            }

            var modelsReady = result.TryGetProperty("translationModelsReady", out var ready) && ready.GetBoolean();
            return modelsReady
                ? ProviderAvailability.Available
                : new ProviderAvailability(false, "尚未安装中英离线翻译模型，请在设置中下载。");
        }
        catch (Exception exception)
        {
            return new ProviderAvailability(false, exception.Message);
        }
    }

    public async Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await engine.CallAsync(
            "translate",
            new JsonObject
            {
                ["text"] = request.Text,
                ["sourceLanguage"] = request.SourceLanguage,
                ["targetLanguage"] = request.TargetLanguage
            },
            cancellationToken);

        return new TranslationResult(
            result.GetProperty("text").GetString() ?? string.Empty,
            request.SourceLanguage,
            request.TargetLanguage);
    }

    public Task InstallModelsAsync(CancellationToken cancellationToken = default) =>
        engine.CallAsync(
            "install_translation_models",
            cancellationToken: cancellationToken,
            timeout: TimeSpan.FromMinutes(30));
}
