using Avalonia.Media;
using SkiaSharp;

namespace PingYi.App;

/// <summary>Resolve one installed UI face once, rather than treating a CSS list as an asset URI.</summary>
public static class DesktopTypography
{
    private static readonly Lazy<FontFamily> Resolved = new(Resolve);
    public static FontFamily Interface => Resolved.Value;

    private static FontFamily Resolve()
    {
        // Use already installed fonts; never load files or download fonts here.
        var installed = SKFontManager.Default.FontFamilies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] preferred = ["Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC",
            "Noto Sans SC", "PingFang SC", "WenQuanYi Micro Hei", "Segoe UI", "DejaVu Sans"];
        var family = preferred.FirstOrDefault(installed.Contains);
        return family is null
            ? new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter")
            : new FontFamily(family);
    }
}
