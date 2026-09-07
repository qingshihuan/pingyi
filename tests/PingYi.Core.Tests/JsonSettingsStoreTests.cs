using System.Reflection;
using System.Text.Json;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PingYi-settings-" + Guid.NewGuid().ToString("N"));
    private string SettingsFile => Path.Combine(_directory, "settings.json");

    [Fact]
    public async Task MissingSettings_ReturnDefaultsWithoutCreatingFiles()
    {
        var settings = await new JsonSettingsStore(SettingsFile).LoadAsync();

        Assert.Equal(new AppSettings().CustomTranslationModel, settings.CustomTranslationModel);
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripUnicodeSettings()
    {
        var store = new JsonSettingsStore(SettingsFile);
        await store.SaveAsync(new AppSettings { CustomTranslationModel = "本地模型-v1" });

        Assert.Equal("本地模型-v1", (await store.LoadAsync()).CustomTranslationModel);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task ConcurrentSaves_UseCompleteIndependentReplacements()
    {
        var store = new JsonSettingsStore(SettingsFile);
        var saves = Enumerable.Range(0, 32).Select(index => Task.Run(async () =>
        {
            await store.SaveAsync(new AppSettings { CustomTranslationModel = "model-" + index });
        }));
        await Task.WhenAll(saves).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.StartsWith("model-", (await store.LoadAsync()).CustomTranslationModel);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsFile));
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task IndependentStores_CanReadWhileReplacingSettings()
    {
        await new JsonSettingsStore(SettingsFile).SaveAsync(new AppSettings { CustomTranslationModel = "model-initial" });
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workers = Enumerable.Range(0, 12).Select(index => Task.Run(async () =>
        {
            var store = new JsonSettingsStore(SettingsFile);
            await start.Task;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                await store.SaveAsync(new AppSettings { CustomTranslationModel = $"model-{index}-{attempt}" });
                Assert.StartsWith("model-", (await store.LoadAsync()).CustomTranslationModel);
            }
        })).ToArray();
        start.SetResult(true);
        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task PreCanceledSave_PreservesExistingSettings()
    {
        var store = new JsonSettingsStore(SettingsFile);
        await store.SaveAsync(new AppSettings { CustomTranslationModel = "original" });
        var original = await File.ReadAllTextAsync(SettingsFile);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SaveAsync(new AppSettings { CustomTranslationModel = "replacement" }, cancellation.Token));

        Assert.Equal(original, await File.ReadAllTextAsync(SettingsFile));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task CanceledQueuedSave_DoesNotPublishOrBlockLaterSaves()
    {
        var store = new JsonSettingsStore(SettingsFile);
        await store.SaveAsync(new AppSettings { CustomTranslationModel = "original" });
        var gate = Assert.IsType<SemaphoreSlim>(typeof(JsonSettingsStore)
            .GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(store));
        await gate.WaitAsync();
        using var cancellation = new CancellationTokenSource();
        try
        {
            var pending = store.SaveAsync(new AppSettings { CustomTranslationModel = "canceled" }, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        finally
        {
            gate.Release();
        }

        Assert.Equal("original", (await store.LoadAsync()).CustomTranslationModel);
        await store.SaveAsync(new AppSettings { CustomTranslationModel = "next" });
        Assert.Equal("next", (await store.LoadAsync()).CustomTranslationModel);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task PreCanceledLoad_DoesNotSilentlyReturnDefaults()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new JsonSettingsStore(SettingsFile).LoadAsync(cancellation.Token));
    }

    [Fact]
    public async Task FailedReplacement_CleansTemporaryFile()
    {
        Directory.CreateDirectory(SettingsFile);
        var exception = await Record.ExceptionAsync(() =>
            new JsonSettingsStore(SettingsFile).SaveAsync(new AppSettings()));

        Assert.True(exception is IOException or UnauthorizedAccessException);
        Assert.True(Directory.Exists(SettingsFile));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task MalformedSettings_ReturnDefaultsAndCanBeRepaired()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsFile, "{invalid json");
        var store = new JsonSettingsStore(SettingsFile);

        Assert.Equal(new AppSettings().CustomTranslationModel, (await store.LoadAsync()).CustomTranslationModel);
        await store.SaveAsync(new AppSettings { CustomTranslationModel = "repaired" });
        Assert.Equal("repaired", (await store.LoadAsync()).CustomTranslationModel);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
