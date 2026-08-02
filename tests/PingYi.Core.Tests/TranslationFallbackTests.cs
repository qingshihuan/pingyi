using PingYi.Core;

namespace PingYi.Core.Tests;

public sealed class TranslationFallbackTests
{
    [Fact]
    public async Task ExecuteAsync_FallsBackToOfflineProviderWhenExternalProviderFails()
    {
        var external = new StubProvider("custom-chat", fail: true, "external");
        var offline = new StubProvider("local-argos", fail: false, "offline");

        var execution = await TranslationFallback.ExecuteAsync(
            external,
            offline,
            new TranslationRequest("Hello", "en", "zh"));

        Assert.True(execution.UsedFallback);
        Assert.Equal("local-argos", execution.Provider.Id);
        Assert.Equal("offline", execution.Result.Text);
        Assert.Equal(1, offline.TranslateCalls);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotFallbackAfterCancellation()
    {
        var external = new StubProvider("custom-chat", fail: false, "external", cancel: true);
        var offline = new StubProvider("local-argos", fail: false, "offline");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TranslationFallback.ExecuteAsync(
                external,
                offline,
                new TranslationRequest("Hello", "en", "zh")));

        Assert.Equal(0, offline.TranslateCalls);
    }

    [Fact]
    public async Task ExecuteAsync_DetectsAutomaticSourceBeforeOfflineFallback()
    {
        var external = new StubProvider("custom-chat", fail: true, "external");
        var offline = new StubProvider("local-argos", fail: false, "offline");

        var execution = await TranslationFallback.ExecuteAsync(
            external,
            offline,
            new TranslationRequest("Hello", LanguageCatalog.Auto, "zh"));

        Assert.True(execution.UsedFallback);
        Assert.Equal("en", offline.LastRequest?.SourceLanguage);
        Assert.Equal("zh", offline.LastRequest?.TargetLanguage);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotSendUnsupportedLanguagePairToOfflineFallback()
    {
        var external = new StubProvider("custom-chat", fail: true, "external");
        var offline = new StubProvider("local-argos", fail: false, "offline");

        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            TranslationFallback.ExecuteAsync(
                external,
                offline,
                new TranslationRequest("Hello", "auto", "ja")));

        Assert.Equal("translation_fallback_language_unsupported", exception.Code);
        Assert.Contains("日语", exception.Message);
        Assert.Equal(0, offline.TranslateCalls);
    }

    private sealed class StubProvider(
        string id,
        bool fail,
        string result,
        bool cancel = false) : ITranslationProvider
    {
        public int TranslateCalls { get; private set; }
        public TranslationRequest? LastRequest { get; private set; }

        public ProviderMetadata Metadata { get; } = new(
            id,
            id,
            id == "local-argos" ? ProviderExecutionLocation.Local : ProviderExecutionLocation.Configurable,
            UploadsImage: false,
            RequiresSecret: false,
            ["zh", "en"]);

        public ValueTask<ProviderAvailability> GetAvailabilityAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ProviderAvailability.Available);

        public Task<TranslationResult> TranslateAsync(
            TranslationRequest request,
            CancellationToken cancellationToken = default)
        {
            TranslateCalls++;
            LastRequest = request;
            if (cancel)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            if (fail)
            {
                throw new ProviderException("failed", "external failed");
            }

            return Task.FromResult(new TranslationResult(result, request.SourceLanguage, request.TargetLanguage));
        }
    }
}
