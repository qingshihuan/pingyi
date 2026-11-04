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
        new() { Id = id, Name = id, Endpoint = endpoint, DefaultModel = "model-a", Models = [new("model-a", true, true)] };
    private static AppSettings Configured(params ModelServiceProfile[] profiles) => new()
    {
        ModelConfiguration = new() { Services = profiles, CurrentModel = new(profiles[0].Id, "model-a") }
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
        Assert.Null(await new ProfileSecretStore(store, a.CredentialRef).GetAsync("unrelated-legacy-key"));
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
            CustomTranslationEndpoint = AppSettings.ManagedModelEndpoint, CustomTranslationModel = model.ModelAlias,
            ModelConfiguration = new() { CurrentModel = new(ModelConfiguration.Managed, model.Id), VerifiedManagedModelId = model.Id } };
        var secrets = new MemorySecrets(); secrets.Values[SecretKeys.CustomTranslationApiKey] = "must-stay-away-from-localhost";
        using var client = new HttpClient(new Handler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(Json("""{"choices":[{"message":{"content":"hello"}}]}"""));
        }));
        await ConfiguredProviders.Translation(client, secrets, settings)
            .TranslateAsync(new("hello", "en", "zh"));
    }

    [Fact]
    public async Task Legacy_key_import_is_idempotent_and_keeps_original_key_for_inherited_tasks()
    {
        var store = new MemorySecrets(); store.Values[SecretKeys.CustomTranslationApiKey] = "old-test-key";
        var original = ConfigurationMigration.Upgrade(new() { SchemaVersion = 14, CustomTranslationEndpoint = "https://old.example/v1" });
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
        var settings = ConfigurationMigration.Upgrade(new() { SchemaVersion = 14, CustomTranslationEndpoint = "https://old.example/v1" });
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
        secrets.Values[b.CredentialRef] = "b-old"; secrets.Values["unrelated-legacy-key"] = "google-old";
        var store = new MemorySettings { FailSave = true };
        await Assert.ThrowsAsync<IOException>(() => ConfigurationSecrets.CommitAsync(store, secrets, before, after,
            new Dictionary<string, string> { [a.CredentialRef] = "a-new" }));
        Assert.Equal("a-old", secrets.Values[a.CredentialRef]); Assert.Equal("b-old", secrets.Values[b.CredentialRef]);
        Assert.Equal("google-old", secrets.Values["unrelated-legacy-key"]); Assert.Null(store.Saved);
        store.FailSave = false;
        await ConfigurationSecrets.CommitAsync(store, secrets, before, after, new Dictionary<string, string> { [a.CredentialRef] = "a-new" });
        Assert.Equal("a-new", secrets.Values[a.CredentialRef]); Assert.False(secrets.Values.ContainsKey(b.CredentialRef));
        Assert.Equal("google-old", secrets.Values["unrelated-legacy-key"]); Assert.Equal(after, store.Saved);
    }

    [Fact]
    public async Task Unrelated_secret_write_is_rejected_before_any_vault_change()
    {
        var settings = Configured(Profile()); var secrets = new MemorySecrets();
        await Assert.ThrowsAsync<ProviderException>(() => ConfigurationSecrets.CommitAsync(new MemorySettings(), secrets,
            settings, settings, new Dictionary<string, string> { ["unrelated-legacy-key"] = "wrong" }));
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
            Assert.Equal(legacy, await File.ReadAllTextAsync(path + ".pre-v16.bak"));
            var a = Profile() with { Models = [new("model-a", true, true)] };
            var changed = loaded with { ModelConfiguration = new() { Services = [a], CurrentModel = new(a.Id, "model-a") } };
            await store.SaveAsync(changed);
            Assert.Equal(changed, await store.LoadAsync());
            Assert.Equal(legacy, await File.ReadAllTextAsync(path + ".pre-v16.bak"));
            Assert.DoesNotContain("credentialRef", await File.ReadAllTextAsync(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            await File.WriteAllTextAsync(path, """{"schemaVersion":999}""");
            await Assert.ThrowsAsync<ProviderException>(() => store.LoadAsync());
            await Assert.ThrowsAsync<ProviderException>(() => store.SaveAsync(changed));
            Assert.Equal("""{"schemaVersion":999}""", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(directory, true); }
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
