using System.Net.Http.Headers;
using PingYi.Core;

namespace PingYi.Infrastructure;

/// <summary>Read-only capability: a model client can read only the key for its frozen route.</summary>
public sealed class ProfileSecretStore(ISecretStore inner, string credentialRef, string? draftKey = null) : ISecretStore
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (key != SecretKeys.CustomTranslationApiKey || string.IsNullOrEmpty(credentialRef)) return null;
        var value = draftKey ?? await inner.GetAsync(credentialRef, cancellationToken);
        return ValidateKey(value);
    }
    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) => throw new NotSupportedException("Read-only route credentials.");
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException("Read-only route credentials.");

    public static string? ValidateKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length > 8192 || value.Any(char.IsControl)) throw InvalidKey();
        value = value.Trim();
        try { _ = new AuthenticationHeaderValue("Bearer", value); }
        catch (FormatException) { throw InvalidKey(); }
        return value;
    }
    private static ProviderException InvalidKey() => new("profile_key_invalid",
        "API Key 格式无效，请重新输入，勿包含换行或控制字符。 / Invalid API key format; remove line breaks and control characters.");
}

public static class ConfigurationSecrets
{
    public static async Task<AppSettings> ImportLegacyAsync(AppSettings settings, ISecretStore store, CancellationToken token = default)
    {
        var configuration = settings.ModelConfiguration;
        if (configuration is not { LegacyCredentialPending: true }) return settings;
        var imported = configuration.Services.SingleOrDefault(s => s.Id == ModelConfiguration.Imported);
        // Never move an old key to a destination edited after migration.
        if (imported is not null && ModelServiceProfile.CanonicalEndpoint(imported.Endpoint) ==
            ModelServiceProfile.CanonicalEndpoint(settings.CustomTranslationEndpoint))
        {
            var existing = await store.GetAsync(imported.CredentialRef, token);
            if (string.IsNullOrEmpty(existing))
            {
                var legacy = ProfileSecretStore.ValidateKey(await store.GetAsync(SecretKeys.CustomTranslationApiKey, token));
                if (!string.IsNullOrEmpty(legacy)) await store.SetAsync(imported.CredentialRef, legacy, token);
            }
        }
        return settings with { ModelConfiguration = configuration with { LegacyCredentialPending = false } };
    }

    /// <summary>Caller serializes settings transitions. Failed settings writes restore affected credentials.</summary>
    public static async Task CommitAsync(ISettingsStore settingsStore, ISecretStore secretStore,
        AppSettings before, AppSettings after, IReadOnlyDictionary<string, string> editedKeys,
        CancellationToken token = default)
    {
        after.ModelConfiguration?.Validate();
        var liveRefs = (after.ModelConfiguration?.Services ?? []).Select(s => s.CredentialRef).ToHashSet(StringComparer.Ordinal);
        if (editedKeys.Keys.Any(key => !liveRefs.Contains(key)))
            throw new ProviderException("profile_secret_scope", "密钥写入不属于当前服务配置。 / Credential write is outside the configured services.");
        var writes = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var pair in editedKeys) writes[pair.Key] = ProfileSecretStore.ValidateKey(pair.Value);
        foreach (var profile in before.ModelConfiguration?.Services ?? [])
            if (!liveRefs.Contains(profile.CredentialRef)) writes[profile.CredentialRef] = null;
        var restore = new Dictionary<string, string?>(StringComparer.Ordinal);
        try
        {
            foreach (var pair in writes)
            {
                restore[pair.Key] = await secretStore.GetAsync(pair.Key, token);
                if (pair.Value is null) await secretStore.DeleteAsync(pair.Key, token);
                else await secretStore.SetAsync(pair.Key, pair.Value, token);
            }
            await settingsStore.SaveAsync(after, token);
        }
        catch
        {
            var rollbackFailed = false;
            using var rollback = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            foreach (var pair in restore)
                try
                {
                    if (pair.Value is null) await secretStore.DeleteAsync(pair.Key, rollback.Token);
                    else await secretStore.SetAsync(pair.Key, pair.Value, rollback.Token);
                }
                catch { rollbackFailed = true; }
            if (rollbackFailed) throw new ProviderException("profile_secret_rollback", "配置未保存，部分密钥恢复失败，请重新填写。 / Configuration was not saved; re-enter credentials after a rollback failure.");
            throw;
        }
    }
}
