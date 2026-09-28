using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class AutomaticPreferenceMigrationTests
{
    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"schemaVersion\":9,\"uiLanguage\":\"en-US\"}", true)]
    [InlineData("{\"schemaVersion\":11}", true)]
    [InlineData("{\"schemaVersion\":11,\"automaticCaptureEnabled\":true}", true)]
    [InlineData("{\"schemaVersion\":11,\"automaticCaptureEnabled\":false}", false)]
    public async Task Absent_flag_enables_automatic_tasks_but_explicit_false_survives_restart(string json, bool expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pingyi-auto-pref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(file, json);
            var store = new JsonSettingsStore(file);
            var settings = await store.LoadAsync();
            Assert.Equal(expected, settings.AutomaticCaptureEnabled);
            await store.SaveAsync(settings);
            Assert.Equal(expected, (await new JsonSettingsStore(file).LoadAsync()).AutomaticCaptureEnabled);
        }
        finally { Directory.Delete(directory, true); }
    }
}
