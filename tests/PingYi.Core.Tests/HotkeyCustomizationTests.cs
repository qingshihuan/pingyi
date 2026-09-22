using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class HotkeyCustomizationTests
{
    [Theory]
    [InlineData("shift + control + d", "Ctrl+Shift+D")]
    [InlineData("Alt+7", "Alt+7")]
    [InlineData("Ctrl+Alt+Shift+g", "Ctrl+Alt+Shift+G")]
    [InlineData("Shift+A", "Shift+A")]
    public void Parser_returns_a_canonical_round_trippable_combination(string input, string expected)
    {
        var gesture = GlobalHotkeyGesture.Parse(input);
        Assert.Equal(expected, gesture.ToString());
        Assert.Equal(gesture, GlobalHotkeyGesture.Parse(gesture.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("D")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl++D")]
    [InlineData("Ctrl+D+")]
    [InlineData("Ctrl+Control+D")]
    [InlineData("Alt+Alt+D")]
    [InlineData("Shift+Shift+D")]
    [InlineData("Super+D")]
    [InlineData("Ctrl+F12")]
    [InlineData("Ctrl+D+G")]
    public void Parser_rejects_invalid_or_ambiguous_combinations(string input) =>
        Assert.Throws<NotSupportedException>(() => GlobalHotkeyGesture.Parse(input));

    [Theory]
    [InlineData(true, 8, "Ctrl+Alt+D", false, "Ctrl+Shift+D", false)]
    [InlineData(true, 9, "Ctrl+Alt+Shift+D", false, "Ctrl+Shift+D", false)]
    [InlineData(true, 9, "shift + Control + alt + d", false, "Ctrl+Shift+D", false)]
    [InlineData(true, 9, "Ctrl+Alt+G", false, "Ctrl+Alt+G", true)]
    [InlineData(true, 9, "Ctrl+Alt+D", false, "Ctrl+Alt+D", true)]
    [InlineData(true, 9, "Ctrl+Alt+Shift+D", true, "Ctrl+Alt+Shift+D", true)]
    [InlineData(true, 10, "Ctrl+Alt+Shift+D", false, "Ctrl+Alt+Shift+D", true)]
    [InlineData(false, 9, "Ctrl+Alt+D", false, "Ctrl+Alt+D", false)]
    [InlineData(false, 9, "Ctrl+Alt+Shift+D", false, "Ctrl+Alt+Shift+D", true)]
    [InlineData(true, 9, null, false, "Ctrl+Shift+D", false)]
    public void Migration_changes_only_eligible_platform_defaults(bool linux, int schema, string? before,
        bool customized, string expected, bool expectedCustomized)
    {
        var source = new AppSettings
        {
            SchemaVersion = schema, Hotkey = before!, HotkeyIsCustomized = customized,
            CustomTranslationModel = "kept", UiLanguage = "en-US", StartMinimized = true
        };
        var result = source.NormalizeForPlatform(linux);
        Assert.Equal(expected, result.Hotkey);
        Assert.Equal(expectedCustomized, result.HotkeyIsCustomized);
        Assert.Equal("kept", result.CustomTranslationModel);
        Assert.Equal("en-US", result.UiLanguage);
        Assert.True(result.StartMinimized);
        Assert.Equal(result, result.NormalizeForPlatform(linux));
    }

    [Fact]
    public async Task Explicit_customization_is_persisted_across_restart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pingyi-hotkey-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"));
            var source = new AppSettings { Hotkey = "Ctrl+Alt+G", HotkeyIsCustomized = true, UiLanguage = "en-US" };
            await store.SaveAsync(source);
            var loaded = await new JsonSettingsStore(Path.Combine(directory, "settings.json")).LoadAsync();
            Assert.Equal("Ctrl+Alt+G", loaded.Hotkey);
            Assert.True(loaded.HotkeyIsCustomized);
            Assert.Equal("en-US", loaded.UiLanguage);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Successful_change_binds_before_persisting()
    {
        var service = new FakeHotkey();
        Exception? registrationError = new Exception();
        await HotkeyBindingChange.ApplyAsync(service, "Ctrl+Shift+D", "alt+g", () =>
        {
            Assert.Equal("Alt+G", service.Active);
            service.Calls.Add("save");
            return Task.CompletedTask;
        }, error => registrationError = error);
        Assert.Equal(new[] { "stop", "start:Alt+G", "save" }, service.Calls);
        Assert.Null(registrationError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conflict_or_save_failure_restores_the_previous_binding(bool failSave)
    {
        var service = new FakeHotkey { Reject = failSave ? null : "Alt+G" };
        var saved = false;
        Exception? status = null;
        await Assert.ThrowsAnyAsync<Exception>(() => HotkeyBindingChange.ApplyAsync(service,
            "Ctrl+Shift+D", "Alt+G", () =>
            {
                if (failSave) throw new IOException("Synthetic disk failure");
                saved = true;
                return Task.CompletedTask;
            }, error => status = error));
        Assert.False(saved);
        Assert.Equal("Ctrl+Shift+D", service.Active);
        Assert.Null(status);
        Assert.Equal(new[] { "stop", "start:Alt+G", "stop", "start:Ctrl+Shift+D" }, service.Calls);
    }

    [Fact]
    public async Task Invalid_input_does_not_release_the_active_binding()
    {
        var service = new FakeHotkey();
        await Assert.ThrowsAsync<NotSupportedException>(() => HotkeyBindingChange.ApplyAsync(service,
            "Ctrl+Shift+D", "Ctrl++G", () => Task.CompletedTask, _ => { }));
        Assert.Empty(service.Calls);
    }

    [Fact]
    public async Task Failed_rollback_remains_visible()
    {
        var service = new FakeHotkey { RejectAll = true };
        Exception? status = null;
        await Assert.ThrowsAsync<ProviderException>(() => HotkeyBindingChange.ApplyAsync(service,
            "Ctrl+Shift+D", "Alt+G", () => Task.CompletedTask, error => status = error));
        Assert.IsType<ProviderException>(status);
        Assert.Null(service.Active);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Ctrl+Alt+G")]
    public async Task Recording_and_cancel_always_restore_without_applying_candidate(string? candidate)
    {
        var service = new FakeHotkey();
        var selected = await HotkeyBindingChange.RecordAsync(service, "Ctrl+Shift+D", () =>
        {
            Assert.Null(service.Active);
            return Task.FromResult(candidate);
        }, _ => { });
        Assert.Equal(candidate, selected);
        Assert.Equal("Ctrl+Shift+D", service.Active);
        Assert.Equal(new[] { "stop", "start:Ctrl+Shift+D" }, service.Calls);
    }

    [Fact]
    public async Task Recording_exception_also_restores_the_active_binding()
    {
        var service = new FakeHotkey();
        await Assert.ThrowsAsync<IOException>(() => HotkeyBindingChange.RecordAsync(service,
            "Ctrl+Shift+D", () => throw new IOException("Synthetic dialog failure"), _ => { }));
        Assert.Equal("Ctrl+Shift+D", service.Active);
    }

    [Fact]
    public async Task Unchanged_binding_can_be_retried_after_an_initial_conflict()
    {
        var service = new FakeHotkey();
        await HotkeyBindingChange.ApplyAsync(service, "Ctrl+Shift+D", "Ctrl+Shift+D",
            () => Task.CompletedTask, _ => { }, retry: true);
        Assert.Equal(new[] { "stop", "start:Ctrl+Shift+D" }, service.Calls);
    }

    private sealed class FakeHotkey : IGlobalHotkeyService
    {
        public List<string> Calls { get; } = [];
        public string? Active { get; private set; } = "Ctrl+Shift+D";
        public string? Reject { get; init; }
        public bool RejectAll { get; init; }
        public event EventHandler? Pressed { add { } remove { } }
        public Task StartAsync(string shortcut, CancellationToken cancellationToken = default)
        {
            Calls.Add("start:" + shortcut);
            if (RejectAll || Reject == shortcut) throw new ProviderException("hotkey_conflict", "Synthetic occupied binding");
            Active = shortcut;
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("stop"); Active = null; return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
