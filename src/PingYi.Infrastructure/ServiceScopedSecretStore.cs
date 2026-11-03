using PingYi.Core;

namespace PingYi.Infrastructure;

/// <summary>
/// Adapts the legacy custom-key contract used by existing model adapters to a
/// service-specific credential reference. Secrets remain outside settings.json.
/// </summary>
internal sealed class ServiceScopedSecretStore(ISecretStore inner, string credentialRef) : ISecretStore
{
    private string Resolve(string key) =>
        string.Equals(key, SecretKeys.CustomTranslationApiKey, StringComparison.Ordinal)
            ? credentialRef
            : key;

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        inner.GetAsync(Resolve(key), cancellationToken);

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) =>
        inner.SetAsync(Resolve(key), value, cancellationToken);

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(Resolve(key), cancellationToken);
}
