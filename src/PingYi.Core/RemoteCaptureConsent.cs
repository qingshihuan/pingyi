using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PingYi.Core;

public enum RemoteCaptureConsentChoice { Deny, AllowOnce, AlwaysAllow }

/// <summary>Desktop capture approval binds a verified destination, never screenshot content or credentials.</summary>
public static class RemoteCaptureConsent
{
    public static string NormalizeApprovals(string? value) => string.Join('\n', Parse(value));

    private static IEnumerable<string> Parse(string? value) => (value ?? "")
        .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Where(v => v.Length == 64 && v.All(char.IsAsciiHexDigit)).Select(v => v.ToLowerInvariant())
        .Distinct(StringComparer.Ordinal).Take(256);

    private static string Key(AppSettings settings)
    {
        var route = ConfigurationRouting.Resolve(settings, ModelTasks.Ocr);
        var parts = new[] { route.CredentialRef, route.Settings.CustomTranslationEndpoint,
            route.Settings.CustomTranslationApiProtocol, route.Settings.CustomTranslationModel };
        var identity = string.Concat(parts.Select(value => value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("desktop-capture-v1\n" + identity))).ToLowerInvariant();
    }

    public static bool IsRemembered(AppSettings settings) => Parse(settings.RemoteCaptureApprovals).Contains(Key(settings), StringComparer.Ordinal);
    public static AppSettings Remember(AppSettings settings) => settings with
    {
        RemoteCaptureApprovals = string.Join('\n', new[] { Key(settings) }.Concat(Parse(settings.RemoteCaptureApprovals))
            .Distinct(StringComparer.Ordinal).Take(256))
    };

    public static async Task<AppSettings?> AuthorizeAsync(AppSettings expected, Func<AppSettings> current,
        Func<CancellationToken, Task<RemoteCaptureConsentChoice>> prompt,
        Func<AppSettings, CancellationToken, Task<AppSettings>> remember, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var choice = IsRemembered(expected) ? RemoteCaptureConsentChoice.AllowOnce : await prompt(token);
        token.ThrowIfCancellationRequested();
        if (choice == RemoteCaptureConsentChoice.Deny) return null;
        if (current() != expected) throw new ProviderException("capture_consent_changed",
            "配置已变化，本次未发送，请重试。 / Configuration changed; nothing was sent. Retry to confirm the destination.");
        return choice switch
        {
            RemoteCaptureConsentChoice.AllowOnce => expected,
            RemoteCaptureConsentChoice.AlwaysAllow => await remember(expected, token),
            _ => null
        };
    }
}
