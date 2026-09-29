using PingYi.Core;

namespace PingYi.Core.Tests;

public sealed class InferenceTimeoutTests
{
    [Fact]
    public async Task Slow_primary_gets_independent_fallback_budget()
    {
        var primary = new Provider("primary", Timeout.InfiniteTimeSpan);
        var fallback = new Provider("fallback", TimeSpan.FromMilliseconds(30));
        var result = await TranslationFallback.ExecuteAsync(primary, fallback, new("Hello", "en", "zh"),
            primaryTimeout: TimeSpan.FromMilliseconds(25), fallbackTimeout: TimeSpan.FromSeconds(2));
        Assert.True(result.UsedFallback);
        Assert.Equal(1, fallback.Calls);
    }
    [Fact]
    public async Task Http_internal_cancellation_is_not_user_cancellation()
    {
        var primary = new Provider("primary", TimeSpan.Zero, true);
        var fallback = new Provider("fallback", TimeSpan.Zero);
        var result = await TranslationFallback.ExecuteAsync(primary, fallback, new("Hello", "en", "zh"));
        Assert.True(result.UsedFallback);
    }
    [Fact]
    public async Task User_cancel_stops_without_fallback()
    {
        var primary = new Provider("primary", Timeout.InfiniteTimeSpan);
        var fallback = new Provider("fallback", TimeSpan.Zero);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TranslationFallback.ExecuteAsync(primary, fallback, new("Hello", "en", "zh"), cancel.Token));
        Assert.Equal(0, fallback.Calls);
    }
    [Fact]
    public async Task Same_provider_timeout_is_a_visible_error_not_a_silent_cancel()
    {
        var provider = new Provider("fallback", Timeout.InfiniteTimeSpan);
        var error = await Assert.ThrowsAsync<ProviderException>(() => TranslationFallback.ExecuteAsync(provider, provider, new("Hello", "en", "zh"), primaryTimeout: TimeSpan.FromMilliseconds(25)));
        Assert.Equal("translation_timeout", error.Code);
    }
    [Fact]
    public async Task Fallback_timeout_is_reported()
    {
        var primary = new Provider("primary", TimeSpan.Zero, true);
        var fallback = new Provider("fallback", Timeout.InfiniteTimeSpan);
        var error = await Assert.ThrowsAsync<ProviderException>(() => TranslationFallback.ExecuteAsync(primary, fallback, new("Hello", "en", "zh"), fallbackTimeout: TimeSpan.FromMilliseconds(25)));
        Assert.Equal("translation_fallback_timeout", error.Code);
    }
    private sealed class Provider(string id, TimeSpan delay, bool timeout = false) : ITranslationProvider
    {
        public int Calls { get; private set; }
        public ProviderMetadata Metadata { get; } = new(id, id, ProviderExecutionLocation.Local, false, false, ["en", "zh"]);
        public ValueTask<ProviderAvailability> GetAvailabilityAsync(CancellationToken token = default) => ValueTask.FromResult(ProviderAvailability.Available);
        public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken token = default)
        {
            Calls++;
            if (timeout) throw new TaskCanceledException("Synthetic internal HTTP timeout");
            await Task.Delay(delay, token);
            return new("translated", request.SourceLanguage, request.TargetLanguage);
        }
    }
}
