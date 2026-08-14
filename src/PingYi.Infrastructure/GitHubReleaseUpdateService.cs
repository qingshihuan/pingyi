using System.Reflection;
using System.Text.Json;

namespace PingYi.Infrastructure;

public sealed record ReleaseUpdateInfo(
    Version CurrentVersion,
    Version LatestVersion,
    Uri ReleasePage,
    bool IsUpdateAvailable);

/// <summary>
/// Performs an explicit, metadata-only check against the official PingYi release feed.
/// It never downloads an installer and is only called after the user opts in.
/// </summary>
public sealed class GitHubReleaseUpdateService(HttpClient httpClient)
{
    private static readonly Uri LatestReleaseEndpoint =
        new("https://api.github.com/repos/qingshihuan/pingyi/releases/latest");

    public async Task<ReleaseUpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseEndpoint);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString();
        var pageValue = root.GetProperty("html_url").GetString();
        if (!TryParseVersion(tag, out var latest) ||
            !Uri.TryCreate(pageValue, UriKind.Absolute, out var releasePage) ||
            !string.Equals(releasePage.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The GitHub release response is incomplete.");
        }

        var current = GetCurrentVersion();
        return new ReleaseUpdateInfo(current, latest, releasePage, latest > current);
    }

    public static Version GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(GitHubReleaseUpdateService).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (TryParseVersion(informational, out var parsed))
        {
            return parsed;
        }

        return assembly.GetName().Version ?? new Version(0, 0, 0);
    }

    public static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().TrimStart('v', 'V');
        var metadata = normalized.IndexOfAny(['-', '+']);
        if (metadata >= 0)
        {
            normalized = normalized[..metadata];
        }

        return Version.TryParse(normalized, out version!);
    }
}
