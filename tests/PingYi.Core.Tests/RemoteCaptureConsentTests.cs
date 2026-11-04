using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class RemoteCaptureConsentTests
{
    private static AppSettings Configured(string id = "synthetic-service", string endpoint = "https://example.test/v1",
        string model = "synthetic-vision", string protocol = ModelApiProtocols.ChatCompletions)
    {
        var profile = new ModelServiceProfile { Id = id, Endpoint = endpoint, Protocol = protocol,
            Models = [new(model, true, true)], DefaultModel = model };
        return new() { ModelConfiguration = new() { Services = [profile], CurrentModel = new(id, model) } };
    }

    [Fact]
    public async Task Remembered_approval_survives_restart_and_can_be_cleared_without_changing_model_settings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pingyi-consent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var before = Configured();
            var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"));
            await store.SaveAsync(before);
            // Existing schema 16 files omit the new field; loading must not rewrite them.
            var path = Path.Combine(directory, "settings.json");
            var legacy = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            legacy.Remove("remoteCaptureApprovals");
            var original = legacy.ToJsonString();
            await File.WriteAllTextAsync(path, original);
            Assert.False(RemoteCaptureConsent.IsRemembered(await store.LoadAsync()));
            Assert.Equal(original, await File.ReadAllTextAsync(path));
            var approved = RemoteCaptureConsent.Remember(before);
            Assert.Matches("^[0-9a-f]{64}$", approved.RemoteCaptureApprovals);
            await store.SaveAsync(approved);
            var reloaded = await new JsonSettingsStore(Path.Combine(directory, "settings.json")).LoadAsync();
            Assert.True(RemoteCaptureConsent.IsRemembered(reloaded));
            Assert.Equal(before.ModelConfiguration, reloaded.ModelConfiguration);
            Assert.Equal(before.Hotkey, reloaded.Hotkey);
            Assert.Equal(16, reloaded.SchemaVersion);
            await store.SaveAsync(reloaded with { RemoteCaptureApprovals = "" });
            Assert.False(RemoteCaptureConsent.IsRemembered(await store.LoadAsync()));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("other-service", "https://example.test/v1", "synthetic-vision", ModelApiProtocols.ChatCompletions)]
    [InlineData("synthetic-service", "https://other.test/v1", "synthetic-vision", ModelApiProtocols.ChatCompletions)]
    [InlineData("synthetic-service", "https://example.test/other", "synthetic-vision", ModelApiProtocols.ChatCompletions)]
    [InlineData("synthetic-service", "https://example.test/v1", "other-model", ModelApiProtocols.ChatCompletions)]
    [InlineData("synthetic-service", "https://example.test/v1", "synthetic-vision", ModelApiProtocols.Responses)]
    public void Approval_does_not_move_to_another_service_endpoint_protocol_or_model(string id, string endpoint, string model, string protocol)
    {
        var remembered = RemoteCaptureConsent.Remember(Configured());
        var changed = Configured(id, endpoint, model, protocol) with { RemoteCaptureApprovals = remembered.RemoteCaptureApprovals };
        Assert.False(RemoteCaptureConsent.IsRemembered(changed));
    }

    [Fact]
    public void Multiple_targets_are_remembered_independently_and_invalid_records_grant_nothing()
    {
        var first = RemoteCaptureConsent.Remember(Configured());
        var second = RemoteCaptureConsent.Remember(Configured(model: "another-model") with { RemoteCaptureApprovals = first.RemoteCaptureApprovals });
        Assert.True(RemoteCaptureConsent.IsRemembered(second));
        Assert.True(RemoteCaptureConsent.IsRemembered(first with { RemoteCaptureApprovals = second.RemoteCaptureApprovals }));
        Assert.False(RemoteCaptureConsent.IsRemembered(Configured() with { RemoteCaptureApprovals = "true\nhttps://example.test" }));
    }

    [Theory]
    [InlineData(RemoteCaptureConsentChoice.Deny)]
    [InlineData(RemoteCaptureConsentChoice.AllowOnce)]
    [InlineData(RemoteCaptureConsentChoice.AlwaysAllow)]
    public async Task Only_the_remember_button_persists_an_approval(RemoteCaptureConsentChoice choice)
    {
        var current = Configured();
        var writes = 0;
        var approved = await RemoteCaptureConsent.AuthorizeAsync(current, () => current, _ => Task.FromResult(choice),
            (expected, _) => { writes++; current = RemoteCaptureConsent.Remember(expected); return Task.FromResult(current); }, default);
        Assert.Equal(choice == RemoteCaptureConsentChoice.AlwaysAllow ? 1 : 0, writes);
        if (choice == RemoteCaptureConsentChoice.Deny) Assert.Null(approved);
        else Assert.Equal(current, approved);
        Assert.Equal(choice == RemoteCaptureConsentChoice.AlwaysAllow, RemoteCaptureConsent.IsRemembered(current));
    }

    [Fact]
    public async Task Remembered_target_skips_the_dialog_and_does_not_rewrite_settings()
    {
        var current = RemoteCaptureConsent.Remember(Configured());
        Assert.Equal(current, await RemoteCaptureConsent.AuthorizeAsync(current, () => current,
            _ => throw new InvalidOperationException("Must not prompt again"),
            (_, _) => throw new InvalidOperationException("Must not rewrite settings"), default));
    }

    [Fact]
    public async Task A_destination_change_while_the_dialog_is_open_does_not_save_or_authorize()
    {
        var expected = Configured(); var current = expected;
        await Assert.ThrowsAsync<ProviderException>(() => RemoteCaptureConsent.AuthorizeAsync(expected, () => current,
            _ => { current = Configured(endpoint: "https://changed.test/v1"); return Task.FromResult(RemoteCaptureConsentChoice.AlwaysAllow); },
            (_, _) => throw new InvalidOperationException("Must not persist a changed route"), default));
    }

    [Fact]
    public async Task A_failed_save_does_not_return_an_authorized_snapshot()
    {
        var expected = Configured();
        await Assert.ThrowsAsync<IOException>(() => RemoteCaptureConsent.AuthorizeAsync(expected, () => expected,
            _ => Task.FromResult(RemoteCaptureConsentChoice.AlwaysAllow),
            (_, _) => throw new IOException("Synthetic write failure"), default));
        Assert.False(RemoteCaptureConsent.IsRemembered(expected));
    }

    [Fact]
    public async Task Cancellation_after_a_choice_does_not_persist_or_authorize()
    {
        var expected = Configured();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RemoteCaptureConsent.AuthorizeAsync(expected, () => expected,
            _ => { cancellation.Cancel(); return Task.FromResult(RemoteCaptureConsentChoice.AlwaysAllow); },
            (_, _) => throw new InvalidOperationException("Must not save after cancellation"), cancellation.Token));
    }
}
