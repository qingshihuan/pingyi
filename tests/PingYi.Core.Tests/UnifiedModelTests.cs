using System.Net;
using System.Text;
using System.Text.Json;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class UnifiedModelTests
{
    [Fact]
    public async Task Partially_specified_service_keeps_safe_defaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pingyi-partial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path, """{"schemaVersion":16,"modelConfiguration":{"localServices":[{"id":"local","name":"Test","endpoint":"http://127.0.0.1:18765/v1/chat/completions","models":[{"id":"text-only-test"}]}]}}""");
            var loaded = await new JsonSettingsStore(path).LoadAsync();
            Assert.Equal(ModelApiProtocols.ChatCompletions, Assert.Single(loaded.ModelConfiguration!.LocalServices).Protocol);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Schema15_file_without_new_fields_preserves_profiles_and_verified_shared_selection()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pingyi-v15-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path, """{"schemaVersion":15,"modelConfiguration":{"services":[{"id":"kept","name":"Kept","endpoint":"http://127.0.0.1:11434/v1/chat/completions","defaultModel":"vision","models":[{"id":"vision","textVerified":true,"visionVerified":true}]}],"tasks":{"ocr":{"serviceId":"kept"},"translation":{"serviceId":"kept"}}}}""");
            var store = new JsonSettingsStore(path);
            var loaded = await store.LoadAsync();
            Assert.Equal(new ModelSelection("kept", "vision"), loaded.ModelConfiguration!.CurrentModel);
            Assert.Empty(loaded.ModelConfiguration.Services);
            Assert.Equal("kept", Assert.Single(loaded.ModelConfiguration.LocalServices).Id);
            await store.SaveAsync(loaded);
            Assert.Equal(loaded, await store.LoadAsync());
        }
        finally { Directory.Delete(directory, true); }
    }
    private static ModelServiceProfile Profile(bool text = true, bool vision = true) => new()
    { Id = "selected", Endpoint = "https://model.example/v1", DefaultModel = "multimodal", Models = [new("multimodal", text, vision)] };
    private static AppSettings Settings(ModelServiceProfile profile) => new()
    { ModelConfiguration = new() { Services = [profile], CurrentModel = new(profile.Id, "multimodal") } };

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)]
    public void Text_only_and_unverified_models_cannot_be_current(bool text, bool vision)
    {
        var settings = Settings(Profile(text, vision));
        foreach (var task in ModelTasks.All)
            Assert.Equal("model_capability_unverified", Assert.Throws<ProviderException>(() => ConfigurationRouting.Resolve(settings, task)).Code);
    }
    [Fact]
    public void No_selection_fails_closed_and_managed_selection_requires_its_verified_package()
    {
        Assert.Equal("current_model_missing", Assert.Throws<ProviderException>(() => ConfigurationRouting.Resolve(new(), ModelTasks.Translation)).Code);
        var model = ManagedMultimodalModels.Recommended;
        var settings = new AppSettings { ModelConfiguration = new() { CurrentModel = new(ModelConfiguration.Managed, model.Id) } };
        Assert.Equal("model_capability_unverified", Assert.Throws<ProviderException>(() => ConfigurationRouting.Resolve(settings, ModelTasks.Ocr)).Code);
    }
    [Theory]
    [InlineData(ModelApiProtocols.ChatCompletions)] [InlineData(ModelApiProtocols.Responses)]
    public async Task Translation_and_image_recognition_send_the_same_model_endpoint_protocol_and_key(string protocol)
    {
        var profile = Profile() with { Protocol = protocol };
        var settings = Settings(profile);
        var calls = 0;
        using var client = new HttpClient(new Handler(async (request, token) =>
        {
            calls++;
            Assert.Equal(protocol == ModelApiProtocols.Responses ? "/v1/responses" : "/v1/chat/completions", request.RequestUri!.AbsolutePath);
            Assert.Equal("scoped-test-key", request.Headers.Authorization?.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("multimodal", body.RootElement.GetProperty("model").GetString());
            if (calls == 2) Assert.Contains("data:image/png;base64", body.RootElement.GetRawText());
            return new(HttpStatusCode.OK) { Content = new StringContent(protocol == ModelApiProtocols.Responses
                ? """{"output":[{"type":"message","content":[{"type":"output_text","text":"Hello"}]}]}"""
                : """{"choices":[{"message":{"content":"Hello"}}]}""", Encoding.UTF8, "application/json") };
        }));
        var keys = new Secrets(profile.CredentialRef);
        await ConfiguredProviders.Translation(client, keys, settings).TranslateAsync(new("Hello", "en", "zh"));
        await ConfiguredProviders.Ocr(client, keys, settings).RecognizeAsync(new([1, 2, 3], 1, 1, new(0, 0, 1, 1)), new("en"));
        Assert.Equal(2, calls);
        Assert.Single(ModelTasks.All.Select(t => ConfigurationRouting.Fingerprint(settings, t)).Distinct());
    }
    [Fact]
    public async Task Failure_never_switches_model_or_service_and_cancellation_propagates()
    {
        var count = 0;
        using var client = new HttpClient(new Handler((_, _) => { count++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)); }));
        var profile = Profile(); var settings = Settings(profile);
        await Assert.ThrowsAsync<ProviderException>(() => ConfiguredProviders.Translation(client, new Secrets(profile.CredentialRef), settings)
            .TranslateAsync(new("Hello", "en", "zh")));
        Assert.Equal(1, count);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConfiguredProviders.Ocr(client, new Secrets(profile.CredentialRef), settings)
            .RecognizeAsync(new([1], 1, 1, new(0, 0, 1, 1)), new("en"), canceled.Token));
        Assert.Equal(1, count);
    }
    [Fact]
    public async Task Fixed_text_cannot_falsely_pass_image_capability_probe()
    {
        var error = await Assert.ThrowsAsync<ProviderException>(() => ModelCapabilityProbe.VerifyVisionAsync(new TextOnly(), default));
        Assert.Equal("profile_vision_unverified", error.Code);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Legacy_profiles_split_without_rekeying_and_conflicting_routes_require_selection(bool conflict)
    {
        var cloud = Profile();
        var local = Profile() with { Id = "local", Endpoint = "http://127.0.0.1:11434/v1" };
        using var raw = JsonDocument.Parse("{\"modelConfiguration\":{\"tasks\":{\"ocr\":{\"serviceId\":\"selected\",\"modelId\":\"multimodal\"},\"translation\":{\"serviceId\":\"" + (conflict ? "local" : "selected") + "\",\"modelId\":\"multimodal\"}}}}");
        var old = new AppSettings { SchemaVersion = 15, Hotkey = "Ctrl+Alt+G", ModelConfiguration = new() { Services = [cloud, local] } };
        var upgraded = ConfigurationMigration.Upgrade(old, raw.RootElement).Normalize();
        var config = upgraded.ModelConfiguration!;
        Assert.Equal(cloud.CredentialRef, Assert.Single(config.Services).CredentialRef);
        Assert.Equal(local.CredentialRef, Assert.Single(config.LocalServices).CredentialRef);
        Assert.Equal("ollama", config.LocalServices[0].Backend);
        Assert.Equal("Ctrl+Alt+G", upgraded.Hotkey);
        Assert.Equal(conflict, config.CurrentModel is null);
        Assert.Equal(upgraded, ConfigurationMigration.Upgrade(upgraded));
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
    private sealed class Secrets(string expected) : ISecretStore
    {
        public Task<string?> GetAsync(string key, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); Assert.Equal(expected, key); return Task.FromResult<string?>("scoped-test-key"); }
        public Task SetAsync(string key, string value, CancellationToken token = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken token = default) => throw new NotSupportedException();
    }
    private sealed class TextOnly : IOcrProvider
    {
        public ProviderMetadata Metadata => new("test", "test", ProviderExecutionLocation.Local, false, false, ["en"]);
        public ValueTask<ProviderAvailability> GetAvailabilityAsync(CancellationToken token = default) => ValueTask.FromResult(ProviderAvailability.Available);
        public Task<OcrResult> RecognizeAsync(ImageFrame image, OcrOptions options, CancellationToken token = default) => Task.FromResult(new OcrResult([], "Hello", "en"));
    }
}
