using PingYi.Core;
using PingYi.Infrastructure;
namespace PingYi.Core.Tests;

public sealed class AutomaticCaptureTests
{
    [Fact]
    public void Decoded_qr_stays_local_and_other_images_use_the_current_model()
    {
        Assert.Equal(CapturePurpose.DecodeQrCode, AutomaticCapture.Decide(1).Purpose);
        Assert.Equal(CapturePurpose.TranslateText, AutomaticCapture.Decide(0).Purpose);
        Assert.Throws<ArgumentOutOfRangeException>(() => AutomaticCapture.Decide(-1));
    }
    [Fact]
    public async Task Fresh_install_requires_model_setup_and_partial_legacy_config_stays_unselected()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pingyi-auto-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "settings.json");
            var store = new JsonSettingsStore(file);
            Assert.False((await store.LoadAsync()).InitialSetupCompleted);
            await File.WriteAllTextAsync(file, """{"schemaVersion":9,"uiLanguage":"en-US"}""");
            var old = await store.LoadAsync();
            Assert.Null(old.ModelConfiguration!.CurrentModel);
            Assert.True(old.InitialSetupCompleted);
            Assert.Equal("en-US", old.UiLanguage);
            await store.SaveAsync(old with { AutomaticCaptureEnabled = false });
            Assert.False((await store.LoadAsync()).AutomaticCaptureEnabled);
        }
        finally { Directory.Delete(directory, true); }
    }
}
