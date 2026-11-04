using System.Net;
using System.Text;
using System.Text.Json;
using PingYi.Core;
using PingYi.Infrastructure;
using SkiaSharp;

namespace PingYi.Core.Tests;

public sealed class ModelConfigurationTests
{
    private const string SelectedGpu = "vulkan|Vulkan2|0123456789ABCDEF01234567";
    private static ModelServiceProfile Profile(string id = "service-a", string endpoint = "https://a.example/v1/chat/completions") =>
        new() { Id = id, Name = id, Endpoint = endpoint, DefaultModel = "model-a" };
    private static AppSettings Configured(params ModelServiceProfile[] profiles) => new()
    {
        ModelConfiguration = new() { Services = profiles, Tasks = new() { Translation = new(profiles[0].Id), Ocr = new(profiles[0].Id) } }
    };

    [Theory]
    [InlineData("https://host.example", "https://host.example/v1/chat/completions")]
    [InlineData("https://host.example/v1", "https://host.example/v1/chat/completions")]
    [InlineData("https://host.example/chat/completions", "https://host.example/chat/completions")]
    [InlineData("https://host.example/responses", "https://host.example/chat/completions")]
    [InlineData("https://host.example/prefix/v4/responses", "https://host.example/prefix/v4/chat/completions")]
    [InlineData("https://host.example/compatible-mode/v1/", "https://host.example/compatible-mode/v1/chat/completions")]
    [InlineData("http://127.0.0.1:8080/v1", "http://127.0.0.1:8080/v1/chat/completions")]
    public void Endpoint_normalization_preserves_explicit_resource_prefixes(string input, string expected) =>
        Assert.Equal(expected, ModelServiceProfile.CanonicalEndpoint(input));

    [Theory]
    [InlineData("http://remote.example/v1")]
    [InlineData("https://user:secret@remote.example/v1")]
    [InlineData("https://remote.example/v1?api_key=test")]
    [InlineData("https://remote.example/v1#secret")]
    [InlineData("file:///tmp/model")]
    public void Invalid_or_insecure_destinations_are_rejected(string value) =>
        Assert.Throws<ProviderException>(() => ModelServiceProfile.CanonicalEndpoint(value));

    [Fact]
    public void Migration_is_idempotent_and_does_not_change_existing_task_runtime_or_hotkey_preferences()
    {
        var original = new AppSettings { SchemaVersion = 14, Hotkey = "Ctrl+Alt+G", OcrProviderId = "local-paddle",
            TranslationProviderId = "custom-chat", CustomTranslationEndpoint = "https://api.example/chat/completions",
            CustomTranslationModel = "old-model", CustomTranslationApiProtocol = ModelApiProtocols.Responses,
            ManagedRuntimeBackend = "vulkan", ManagedRuntimeDevice = SelectedGpu, UiLanguage = "en-US" };
        var migrated = ConfigurationMigration.Upgrade(original);
        Assert.Equal(original, migrated with { ModelConfiguration = null });
        Assert.False(migrated.ModelConfiguration!.HasOverrides);
        Assert.Equal("old-model", Assert.Single(migrated.ModelConfiguration.Services).DefaultModel);
        Assert.Same(migrated, ConfigurationMigration.Upgrade(migrated));
        Assert.Equal(SecretKeys.CustomTranslationApiKey, ConfigurationRouting.Resolve(migrated, ModelTasks.Translation).CredentialRef);
    }

    [Fact]
    public void Owned_runtime_is_not_imported_as_an_unmanaged_external_server()
    {
        var model = ManagedMultimodalModels.Recommended;
        var original = new AppSettings { ManagedRuntimeEnabled = true, ManagedModelPackageId = model.Id,
            CustomTranslationEndpoint = AppSettings.ManagedModelEndpoint, CustomTranslationModel = model.ModelAlias };
        var migrated = ConfigurationMigration.Upgrade(original);
        Assert.Empty(migrated.ModelConfiguration!.Services);
        Assert.True(RuntimePolicy.UsesManagedRuntime(migrated));
        Assert.True(ConfigurationRouting.Resolve(migrated, ModelTasks.Ocr).IsManaged);
    }

    [Fact]
    public void Credential_scope_depends_on_profile_and_destination_not_display_name()
    {
        var profile = Profile();
        Assert.Equal(profile.CredentialRef, (profile with { Name = "renamed" }).CredentialRef);
        Assert.NotEqual(profile.CredentialRef, (profile with { Id = "service-b" }).CredentialRef);
        Assert.NotEqual(profile.CredentialRef, (profile with { Endpoint = "https://other.example/v1" }).CredentialRef);
    }

    [Fact]
    public async Task Scoped_credentials_do_not_fall_back_to_shared_legacy_or_other_provider_keys()
    {
        var store = new MemorySecrets();
        store.Values[SecretKeys.CustomTranslationApiKey] = "legacy-secret";
        var a = Profile(); var b = Profile("service-b");
        store.Values[a.CredentialRef] = "key-a";
        Assert.Equal("key-a", await new ProfileSecretStore(store, a.CredentialRef).GetAsync(SecretKeys.CustomTranslationApiKey));
        Assert.Null(await new ProfileSecretStore(store, b.CredentialRef).GetAsync(SecretKeys.CustomTranslationApiKey));
        Assert.Null(await new ProfileSecretStore(store, a.CredentialRef).GetAsync(SecretKeys.GoogleCloudApiKey));
        Assert.Equal("draft", await new ProfileSecretStore(store, a.CredentialRef, "draft").GetAsync(SecretKeys.CustomTranslationApiKey));
        Assert.Equal("key-a", store.Values[a.CredentialRef]);
    }

    [Fact]
    public async Task Invalid_keys_are_rejected_without_echoing_key_material_or_writing_any_secret()
    {
        var profile = Profile(); var settings = Configured(profile); var secrets = new MemorySecrets();
        const string badKey = "private-test-key\r\nX-Injected: value";
        var error = await Assert.ThrowsAsync<ProviderException>(() => new ProfileSecretStore(secrets, profile.CredentialRef, badKey)
            .GetAsync(SecretKeys.CustomTranslationApiKey));
        Assert.DoesNotContain("private-test-key", error.ToString());
        await Assert.ThrowsAsync<ProviderException>(() => ConfigurationSecrets.CommitAsync(new MemorySettings(), secrets,
            settings, settings, new Dictionary<string, string> { [profile.CredentialRef] = badKey }));
        Assert.Empty(secrets.Values);
    }

    [Fact]
    public async Task Owned_runtime_never_receives_the_legacy_cloud_key()
    {
        var model = ManagedMultimodalModels.Recommended;
        var settings = new AppSettings { ManagedRuntimeEnabled = true, ManagedModelPackageId = model.Id,
            CustomTranslationEndpoint = AppSettings.ManagedModelEndpoint, CustomTranslationModel = model.ModelAlias };
        var secrets = new MemorySecrets(); secrets.Values[SecretKeys.CustomTranslationApiKey] = "must-stay-away-from-localhost";
        using var client = new HttpClient(new Handler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(Json("""{"choices":[{"message":{"content":"hello"}}]}"""));
        }));
        await ConfiguredProviders.Translation(client, secrets, settings, new ProviderRegistry([], []))
            .TranslateAsync(new("hello", "en", "zh"));
    }

    [Fact]
    public async Task Legacy_key_import_is_idempotent_and_keeps_original_key_for_inherited_tasks()
    {
        var store = new MemorySecrets(); store.Values[SecretKeys.CustomTranslationApiKey] = "old-test-key";
        var original = ConfigurationMigration.Upgrade(new() { CustomTranslationEndpoint = "https://old.example/v1" });
        var profile = Assert.Single(original.ModelConfiguration!.Services);
        var migrated = await ConfigurationSecrets.ImportLegacyAsync(original, store);
        Assert.Equal("old-test-key", store.Values[profile.CredentialRef]);
        Assert.Equal("old-test-key", store.Values[SecretKeys.CustomTranslationApiKey]);
        Assert.False(migrated.ModelConfiguration!.LegacyCredentialPending);
        store.Values[profile.CredentialRef] = "new-test-key";
        await ConfigurationSecrets.ImportLegacyAsync(original, store);
        Assert.Equal("new-test-key", store.Values[profile.CredentialRef]);
    }

    [Fact]
    public async Task Legacy_import_cannot_send_the_old_key_to_an_edited_destination()
    {
        var store = new MemorySecrets(); store.Values[SecretKeys.CustomTranslationApiKey] = "legacy-test-key";
        var settings = ConfigurationMigration.Upgrade(new() { CustomTranslationEndpoint = "https://old.example/v1" });
        var edited = settings.ModelConfiguration!.Services[0] with { Endpoint = "https://new.example/v1" };
        settings = settings with { ModelConfiguration = settings.ModelConfiguration with { Services = [edited] } };
        await ConfigurationSecrets.ImportLegacyAsync(settings, store);
        Assert.False(store.Values.ContainsKey(edited.CredentialRef));
    }

    [Fact]
    public async Task Failed_configuration_save_restores_keys_and_does_not_touch_unrelated_credentials()
    {
        var a = Profile(); var b = Profile("service-b", "https://b.example/v1");
        var before = Configured(a, b);
        var after = before with { ModelConfiguration = before.ModelConfiguration! with { Services = [a] } };
        var secrets = new MemorySecrets(); secrets.Values[a.CredentialRef] = "a-old";
        secrets.Values[b.CredentialRef] = "b-old"; secrets.Values[SecretKeys.GoogleCloudApiKey] = "google-old";
        var store = new MemorySettings { FailSave = true };
        await Assert.ThrowsAsync<IOException>(() => ConfigurationSecrets.CommitAsync(store, secrets, before, after,
            new Dictionary<string, string> { [a.CredentialRef] = "a-new" }));
        Assert.Equal("a-old", secrets.Values[a.CredentialRef]); Assert.Equal("b-old", secrets.Values[b.CredentialRef]);
        Assert.Equal("google-old", secrets.Values[SecretKeys.GoogleCloudApiKey]); Assert.Null(store.Saved);
        store.FailSave = false;
        await ConfigurationSecrets.CommitAsync(store, secrets, before, after, new Dictionary<string, string> { [a.CredentialRef] = "a-new" });
        Assert.Equal("a-new", secrets.Values[a.CredentialRef]); Assert.False(secrets.Values.ContainsKey(b.CredentialRef));
        Assert.Equal("google-old", secrets.Values[SecretKeys.GoogleCloudApiKey]); Assert.Equal(after, store.Saved);
    }

    [Fact]
    public async Task Unrelated_secret_write_is_rejected_before_any_vault_change()
    {
        var settings = Configured(Profile()); var secrets = new MemorySecrets();
        await Assert.ThrowsAsync<ProviderException>(() => ConfigurationSecrets.CommitAsync(new MemorySettings(), secrets,
            settings, settings, new Dictionary<string, string> { [SecretKeys.GoogleCloudApiKey] = "wrong" }));
        Assert.Empty(secrets.Values);
    }

    [Fact]
    public async Task Store_backs_up_original_bytes_once_and_round_trips_all_profiles_without_keys()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pingyi-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            const string legacy = """{"schemaVersion":14,"customTranslationEndpoint":"https://old.example/v1/chat/completions","customTranslationModel":"old","hotkey":"Ctrl+Alt+G"}""";
            await File.WriteAllTextAsync(path, legacy);
            var store = new JsonSettingsStore(path);
            var loaded = await store.LoadAsync();
            Assert.Equal("Ctrl+Alt+G", loaded.Hotkey);
            await store.SaveAsync(loaded);
            Assert.Equal(legacy, await File.ReadAllTextAsync(path + ".pre-v15.bak"));
            var a = Profile() with { Models = [new("model-a", true, true)] };
            var changed = loaded with { ModelConfiguration = new() { Services = [a], Tasks = new() { Translation = new(a.Id, "model-a", false) } } };
            await store.SaveAsync(changed);
            Assert.Equal(changed, await store.LoadAsync());
            Assert.Equal(legacy, await File.ReadAllTextAsync(path + ".pre-v15.bak"));
            Assert.DoesNotContain("credentialRef", await File.ReadAllTextAsync(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            await File.WriteAllTextAsync(path, """{"schemaVersion":999}""");
            await Assert.ThrowsAsync<ProviderException>(() => store.LoadAsync());
            await Assert.ThrowsAsync<ProviderException>(() => store.SaveAsync(changed));
            Assert.Equal("""{"schemaVersion":999}""", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Each_task_resolves_independently_and_qr_has_no_model_route()
    {
        var a = Profile(); var b = Profile("service-b", "https://b.example/v1");
        var settings = Configured(a, b) with { ModelConfiguration = new() { Services = [a, b], Tasks = new()
        { Ocr = new(ModelConfiguration.Offline), Translation = new(a.Id, "text-a"), Description = new(b.Id, "vision-b", false), Prompt = new(a.Id, "vision-a", false) } } };
        settings.ModelConfiguration!.Validate();
        Assert.Equal("local-paddle", ConfigurationRouting.Resolve(settings, ModelTasks.Ocr).Settings.OcrProviderId);
        Assert.Equal("text-a", ConfigurationRouting.Resolve(settings, ModelTasks.Translation).Settings.CustomTranslationModel);
        Assert.Equal(b.CredentialRef, ConfigurationRouting.Resolve(settings, ModelTasks.Description).CredentialRef);
        Assert.Equal("vision-a", ConfigurationRouting.Resolve(settings, ModelTasks.Prompt).Settings.CustomTranslationModel);
        Assert.False(RuntimePolicy.UsesManagedRuntime(settings));
        Assert.Throws<ArgumentOutOfRangeException>(() => ModelTasks.ForPurpose(CapturePurpose.DecodeQrCode));
        Assert.Throws<ProviderException>(() => (settings.ModelConfiguration with { Tasks = new() { Translation = new("missing") } }).Validate());
        Assert.Throws<ProviderException>(() => (settings.ModelConfiguration with { Tasks = new() { Description = new(ModelConfiguration.Offline) } }).Validate());
    }

    [Fact]
    public void Managed_model_can_coexist_with_remote_translation_without_replacing_runtime_selection()
    {
        var model = ManagedMultimodalModels.Recommended; var remote = Profile();
        var settings = Configured(remote) with { ManagedRuntimeEnabled = true, ManagedModelPackageId = model.Id,
            ManagedRuntimeBackend = "vulkan", ManagedRuntimeDevice = SelectedGpu, CustomTranslationEndpoint = "https://legacy.example/v1",
            ModelConfiguration = new() { Services = [remote], Tasks = new() { Ocr = new(ModelConfiguration.Managed), Translation = new(remote.Id) } } };
        settings = settings.Normalize();
        Assert.True(settings.ManagedRuntimeEnabled); Assert.True(RuntimePolicy.UsesManagedRuntime(settings));
        Assert.True(ConfigurationRouting.Resolve(settings, ModelTasks.Ocr).IsManaged);
        Assert.Equal("vulkan", settings.ManagedRuntimeBackend); Assert.Equal(SelectedGpu, settings.ManagedRuntimeDevice);
        Assert.False(ConfigurationRouting.Resolve(settings, ModelTasks.Translation).IsManaged);
    }

    [Fact]
    public void Ocr_retry_cache_tracks_actual_recognition_route_not_global_or_translation_fields()
    {
        var a = Profile(); var settings = Configured(a); var cache = new OcrRetryCache();
        var image = new ImageFrame([1], 1, 1, new(0, 0, 1, 1)); var recognized = new OcrResult([], "test", "en");
        cache.Remember(image, settings, recognized, "test");
        var translationChanged = settings with { ModelConfiguration = settings.ModelConfiguration! with
            { Tasks = settings.ModelConfiguration.Tasks with { Translation = new(a.Id, "other-model") } } };
        Assert.True(cache.TryGetForTranslationRetry(image, translationChanged, out var cached, out _)); Assert.Same(recognized, cached);
        var ocrChanged = settings with { ModelConfiguration = settings.ModelConfiguration! with
            { Tasks = settings.ModelConfiguration.Tasks with { Ocr = new(a.Id, "other-vision") } } };
        Assert.False(cache.TryGetForTranslationRetry(image, ocrChanged, out _, out _));
        var switched = ProcessingModes.Apply(settings, "lite");
        Assert.Empty(switched.ModelConfiguration!.Tasks.Ocr.ServiceId);
        Assert.Single(switched.ModelConfiguration.Services);
    }

    [Fact]
    public async Task Actual_text_and_vision_factories_send_each_service_only_its_own_key_model_and_protocol()
    {
        var a = Profile() with { Protocol = ModelApiProtocols.Responses, DefaultModel = "text-model" };
        var b = Profile("service-b", "https://b.example/v1/chat/completions") with { DefaultModel = "vision-model" };
        var settings = Configured(a, b) with { ModelConfiguration = new() { Services = [a, b], Tasks = new()
            { Translation = new(a.Id), Ocr = new(b.Id), Description = new(a.Id, "describe-model", false), Prompt = new(b.Id, "prompt-model", false) } } };
        var secrets = new MemorySecrets(); secrets.Values[a.CredentialRef] = "key-a"; secrets.Values[b.CredentialRef] = "key-b";
        secrets.Values[SecretKeys.CustomTranslationApiKey] = "must-not-send-legacy";
        var calls = 0;
        using var client = new HttpClient(new Handler(async request =>
        {
            calls++; var host = request.RequestUri!.Host;
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var body = doc.RootElement; var model = body.GetProperty("model").GetString();
            Assert.Equal(host == "a.example" ? "key-a" : "key-b", request.Headers.Authorization!.Parameter);
            if (host == "a.example")
            {
                Assert.Equal("/v1/responses", request.RequestUri.AbsolutePath);
                Assert.False(body.GetProperty("store").GetBoolean());
                Assert.Contains(model, new[] { "text-model", "describe-model" });
                return Json("""{"output":[{"type":"message","content":[{"type":"output_text","text":"response"}]}]}""");
            }
            Assert.Contains(model, new[] { "vision-model", "prompt-model" });
            Assert.Equal("/v1/chat/completions", request.RequestUri.AbsolutePath);
            Assert.Equal("image_url", body.GetProperty("messages")[1].GetProperty("content")[1].GetProperty("type").GetString());
            return Json("""{"choices":[{"message":{"content":"image result"}}]}""");
        }));
        var registry = new ProviderRegistry([], []);
        await ConfiguredProviders.Translation(client, secrets, settings, registry).TranslateAsync(new("hello", "en", "zh"));
        using var bitmap = new SKBitmap(16, 16); using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var frame = new ImageFrame(png.ToArray(), 16, 16, new(0, 0, 16, 16));
        var ocr = ConfiguredProviders.Ocr(client, secrets, settings, registry);
        Assert.Equal(ProviderExecutionLocation.Cloud, ocr.Metadata.Location);
        Assert.Contains("b.example", ocr.Metadata.DisplayName);
        await ocr.RecognizeAsync(frame, new());
        await ConfiguredProviders.Image(client, secrets, settings, CapturePurpose.DescribeImage).AnalyzeAsync(frame, new(CapturePurpose.DescribeImage));
        await ConfiguredProviders.Image(client, secrets, settings, CapturePurpose.ReconstructPrompt).AnalyzeAsync(frame, new(CapturePurpose.ReconstructPrompt));
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task Browser_uses_profile_destinations_for_consent_and_invalidates_authorization_on_route_change()
    {
        var a = Profile(); var b = Profile("service-b", "https://b.example/v1");
        var settings = Configured(a, b); var calls = 0;
        using var client = new HttpClient(new Handler(_ => { calls++; return Task.FromResult(Json("""{"choices":[{"message":{"content":"test"}}]}""")); }));
        var registry = new ProviderRegistry([], []); var secrets = new MemorySecrets();
        var service = new BrowserTranslationService(registry, _ => Task.CompletedTask,
            s => ConfiguredProviders.Translation(client, secrets, s, registry), s => ConfiguredProviders.Ocr(client, secrets, s, registry));
        var status = service.GetStatus(settings);
        Assert.True(status.RemoteText); Assert.True(status.RemoteImage);
        var request = new BrowserRequest { Operation = "translate", Text = "hello", RouteKey = status.RouteKey };
        Assert.Equal("remote_text_consent_required", (await service.HandleAsync(request, settings, default)).Error);
        Assert.Equal(0, calls);
        var changed = settings with { ModelConfiguration = settings.ModelConfiguration! with
            { Tasks = settings.ModelConfiguration.Tasks with { Translation = new(b.Id) } } };
        Assert.Equal("settings_changed", (await service.HandleAsync(request with { AllowRemoteText = true }, changed, default)).Error);
        Assert.Equal(0, calls);
        Assert.True((await service.HandleAsync(request with { AllowRemoteText = true }, settings, default)).Ok);
        Assert.Equal(1, calls);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request);
    }
    private sealed class MemorySecrets : ISecretStore
    {
        public Dictionary<string, string> Values { get; } = new();
        public Task<string?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value, CancellationToken token = default) { token.ThrowIfCancellationRequested(); Values[key] = value; return Task.CompletedTask; }
        public Task DeleteAsync(string key, CancellationToken token = default) { token.ThrowIfCancellationRequested(); Values.Remove(key); return Task.CompletedTask; }
    }
    private sealed class MemorySettings : ISettingsStore
    {
        public bool FailSave { get; set; }
        public AppSettings? Saved { get; private set; }
        public Task<AppSettings> LoadAsync(CancellationToken token = default) => Task.FromResult(Saved ?? new AppSettings());
        public Task SaveAsync(AppSettings value, CancellationToken token = default)
        { if (FailSave) throw new IOException("Synthetic write failure"); Saved = value; return Task.CompletedTask; }
    }
}
