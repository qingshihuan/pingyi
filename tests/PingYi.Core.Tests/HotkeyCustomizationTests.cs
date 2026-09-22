using PingYi.Core;

namespace PingYi.Core.Tests;

public sealed class HotkeyCustomizationTests
{
    [Theory]
    [InlineData("Ctrl+Shift+D", "Ctrl+Shift+D")]
    [InlineData(" shift + control + d ", "Ctrl+Shift+D")]
    [InlineData("Alt+Ctrl+9", "Ctrl+Alt+9")]
    [InlineData("Shift+Alt+Ctrl+G", "Ctrl+Alt+Shift+G")]
    public void Shortcut_is_canonical(string input, string expected) =>
        Assert.Equal(expected, HotkeyDefinition.Normalize(input));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("D")] [InlineData("Ctrl")]
    [InlineData("Ctrl++D")] [InlineData("Ctrl+Control+D")] [InlineData("Ctrl+Shift+Shift+D")]
    [InlineData("Ctrl+D+G")] [InlineData("Super+D")] [InlineData("Ctrl+F12")]
    [InlineData("Ctrl+中")] [InlineData("Ctrl+D+")]
    public void Unsupported_shortcuts_are_rejected(string? input) => Assert.False(HotkeyDefinition.TryParse(input, out _));

    [Theory]
    [InlineData(true, 8, "Ctrl+Alt+D", false, "Ctrl+Shift+D")]
    [InlineData(true, 9, "Ctrl+Alt+Shift+D", false, "Ctrl+Shift+D")]
    [InlineData(true, 9, "Shift+Control+Alt+d", false, "Ctrl+Shift+D")]
    [InlineData(true, 9, "Ctrl+Alt+D", false, "Ctrl+Alt+D")]
    [InlineData(true, 9, "Ctrl+Alt+G", false, "Ctrl+Alt+G")]
    [InlineData(true, 9, "Ctrl+Alt+Shift+D", true, "Ctrl+Alt+Shift+D")]
    [InlineData(true, 10, "Ctrl+Alt+Shift+D", false, "Ctrl+Alt+Shift+D")]
    [InlineData(false, 9, "Ctrl+Alt+D", false, "Ctrl+Alt+D")]
    [InlineData(false, 9, "Ctrl+Alt+Shift+D", false, "Ctrl+Alt+Shift+D")]
    public void Upgrade_changes_only_the_applicable_legacy_default(bool linux, int schema,
        string before, bool customized, string after)
    {
        var settings = new AppSettings { SchemaVersion = schema, Hotkey = before, HotkeyIsCustomized = customized,
            UiLanguage = "en-US", CustomTranslationModel = "keep-model", StartMinimized = true, CheckForUpdates = true };
        var result = settings.NormalizeForPlatform(linux);
        Assert.Equal(after, result.Hotkey);
        Assert.Equal(10, result.SchemaVersion);
        Assert.Equal("en-US", result.UiLanguage);
        Assert.Equal("keep-model", result.CustomTranslationModel);
        Assert.True(result.StartMinimized);
        Assert.True(result.CheckForUpdates);
        Assert.Equal(result, result.NormalizeForPlatform(linux));
    }

    [Fact]
    public async Task Rebind_registers_before_save()
    {
        var service = new FakeHotkey();
        Exception? reported = new Exception();
        await HotkeyRebinding.ApplyAsync(service, "Ctrl+Shift+D", "shift+ctrl+g", () =>
        {
            Assert.Equal("Ctrl+Shift+G", service.Active);
            service.Calls.Add("save");
            return Task.CompletedTask;
        }, e => reported = e);
        Assert.Equal(new[] { "stop", "start:Ctrl+Shift+G", "save" }, service.Calls);
        Assert.Null(reported);
    }

    [Fact]
    public async Task Rejected_binding_does_not_save_and_restores_previous_key()
    {
        var service = new FakeHotkey { Reject = "Ctrl+Shift+G" };
        bool saved = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => HotkeyRebinding.ApplyAsync(service,
            "Ctrl+Shift+D", "Ctrl+Shift+G", () => { saved = true; return Task.CompletedTask; }, _ => { }));
        Assert.False(saved);
        Assert.Equal("Ctrl+Shift+D", service.Active);
    }

    [Fact]
    public async Task Failed_persistence_restores_previous_binding()
    {
        var service = new FakeHotkey();
        await Assert.ThrowsAsync<IOException>(() => HotkeyRebinding.ApplyAsync(service,
            "Ctrl+Shift+D", "Ctrl+Shift+G", () => throw new IOException("read-only settings"), _ => { }));
        Assert.Equal("Ctrl+Shift+D", service.Active);
    }

    [Fact]
    public async Task Failed_rollback_is_reported_instead_of_claiming_success()
    {
        var service = new FakeHotkey { Reject = "Ctrl+Shift+D" };
        Exception? reported = null;
        var error = await Assert.ThrowsAsync<AggregateException>(() => HotkeyRebinding.ApplyAsync(service,
            "Ctrl+Shift+D", "Ctrl+Shift+G", () => throw new IOException(), e => reported = e));
        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.IsType<InvalidOperationException>(reported);
        Assert.Null(service.Active);
    }

    [Fact]
    public async Task Invalid_input_never_stops_the_existing_binding()
    {
        var service = new FakeHotkey();
        await Assert.ThrowsAsync<NotSupportedException>(() => HotkeyRebinding.ApplyAsync(service,
            "Ctrl+Shift+D", "Ctrl++D", () => Task.CompletedTask, _ => { }));
        Assert.Empty(service.Calls);
    }

    private sealed class FakeHotkey : IGlobalHotkeyService
    {
        public string? Active { get; private set; }
        public string? Reject { get; init; }
        public List<string> Calls { get; } = [];
        public event EventHandler? Pressed { add { } remove { } }
        public Task StartAsync(string shortcut, CancellationToken token = default)
        {
            Calls.Add("start:" + shortcut);
            if (shortcut == Reject) throw new InvalidOperationException("occupied");
            Active = shortcut;
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken token = default)
        {
            Calls.Add("stop"); Active = null; return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
