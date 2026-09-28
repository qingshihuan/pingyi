using System.Diagnostics;
using PingYi.Core;

namespace PingYi.App;

internal static class DesktopWebLinks
{
    internal static Task<bool> OpenAsync(Uri uri)
    {
        if (QrCodeLinks.GetWebUri(uri.OriginalString) is null) return Task.FromResult(false);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return Task.FromResult(true);
        }
        catch { return Task.FromResult(false); }
    }
}
