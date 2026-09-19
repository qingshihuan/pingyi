using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class SettingsCompatibilityTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":9,\"uiLanguage\":\"en-US\",\"hotkey\":\"Ctrl+Alt+Shift+D\",\"checkForUpdates\":false}")]
    [InlineData("{\"schemaVersion\":8,\"customTranslationModel\":null}")]
    [InlineData("{\"hotkey\":null,\"ocrProviderId\":null,\"translationProviderId\":null,\"sourceLanguage\":null,\"targetLanguage\":null,\"customTranslationEndpoint\":null,\"customTranslationModel\":null,\"managedModelPackageId\":null,\"managedRuntimeBackend\":null,\"uiLanguage\":null}")]
    public async Task Load_PartialOrNullSettings_RestoresDefaultsAndRoundTrips(string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pingyi-config-compat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var file = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(file, json, cancellationToken);
            var store = new JsonSettingsStore(file);
            var settings = await store.LoadAsync(cancellationToken);

            Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
            Assert.Equal(AppSettings.DefaultCustomTranslationModel, settings.CustomTranslationModel);
            Assert.Equal(AppSettings.DefaultCustomTranslationEndpoint, settings.CustomTranslationEndpoint);
            Assert.Equal("local-paddle", settings.OcrProviderId);
            Assert.Equal("local-argos", settings.TranslationProviderId);
            Assert.Equal("auto", settings.SourceLanguage);
            Assert.Equal("auto-opposite", settings.TargetLanguage);
            Assert.Equal("auto", settings.ManagedRuntimeBackend);
            Assert.Equal(string.Empty, settings.ManagedModelPackageId);
            Assert.False(settings.ManagedRuntimeEnabled);
            Assert.False(settings.CheckForUpdates);
            Assert.False(string.IsNullOrWhiteSpace(settings.Hotkey));

            await store.SaveAsync(settings, cancellationToken);
            Assert.Equal(settings, await store.LoadAsync(cancellationToken));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Normalize_NullModel_UsesDefaultOnBothPlatforms(bool linux)
    {
        var settings = new AppSettings { CustomTranslationModel = null! }.NormalizeForPlatform(linux);
        Assert.Equal(AppSettings.DefaultCustomTranslationModel, settings.CustomTranslationModel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Load_ExplicitEmptyModel_PreservesCustomServerAndPreferences(string model)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pingyi-config-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var file = Path.Combine(directory, "settings.json");
            var json = "{\"schemaVersion\":8,\"hotkey\":\"Ctrl+Alt+G\",\"uiLanguage\":\"en-US\","
                + "\"customTranslationEndpoint\":\"https://example.com/v1/chat/completions\","
                + "\"translationProviderId\":\"custom-chat\",\"startMinimized\":true,"
                + "\"checkForUpdates\":true,\"customTranslationModel\":\"" + model + "\"}";
            await File.WriteAllTextAsync(file, json, cancellationToken);
            var store = new JsonSettingsStore(file);
            var settings = await store.LoadAsync(cancellationToken);

            Assert.Equal(string.Empty, settings.CustomTranslationModel);
            Assert.Equal("https://example.com/v1/chat/completions", settings.CustomTranslationEndpoint);
            Assert.Equal("custom-chat", settings.TranslationProviderId);
            Assert.Equal("Ctrl+Alt+G", settings.Hotkey);
            Assert.Equal("en-US", settings.UiLanguage);
            Assert.True(settings.StartMinimized);
            Assert.True(settings.CheckForUpdates);
            await store.SaveAsync(settings, cancellationToken);
            Assert.Equal(settings, await store.LoadAsync(cancellationToken));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
