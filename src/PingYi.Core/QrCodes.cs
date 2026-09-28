namespace PingYi.Core;

public sealed record QrCodeResult(string Text)
{
    // QR payloads may also be Wi-Fi settings, contacts, or custom app commands.
    // Only explicit web URLs are eligible for the browser action.
    public Uri? WebUri => QrCodeLinks.GetWebUri(Text);
}

public interface IQrCodeDecoder
{
    Task<IReadOnlyList<QrCodeResult>> DecodeAsync(ImageFrame image,
        CancellationToken cancellationToken = default);
}

public static class QrCodeLinks
{
    public static Uri? GetWebUri(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim();
        if (value.Any(char.IsControl) || value.Contains('\\') ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsWellFormedOriginalString()) return null;
        return uri;
    }
}
