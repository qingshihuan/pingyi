using System.Text;

namespace PingYi.Core;

public static class TextProcessing
{
    public static string DetectLanguage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "unknown";
        }

        var cjk = 0;
        var latin = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is >= 0x3400 and <= 0x9FFF)
            {
                cjk++;
            }
            else if ((rune.Value is >= 'A' and <= 'Z') || (rune.Value is >= 'a' and <= 'z'))
            {
                latin++;
            }
        }

        return cjk > 0 && cjk * 5 >= latin ? "zh" : "en";
    }

    public static string ResolveTargetLanguage(string sourceLanguage, string configuredTarget)
    {
        if (LanguageCatalog.IsKnown(configuredTarget))
        {
            return LanguageCatalog.NormalizeTarget(configuredTarget);
        }

        return sourceLanguage == "zh" ? "en" : "zh";
    }

    public static string BuildPlainText(IEnumerable<OcrBlock> blocks)
    {
        var items = blocks
            .Where(block => !string.IsNullOrWhiteSpace(block.Text))
            .OrderBy(block => block.Bounds.Y)
            .ThenBy(block => block.Bounds.X)
            .ToArray();

        if (items.Length == 0)
        {
            return string.Empty;
        }

        var averageHeight = Math.Max(1, items.Average(block => Math.Max(1, block.Bounds.Height)));
        var lineTolerance = averageHeight * 0.55;
        var lines = new List<List<OcrBlock>>();

        foreach (var block in items)
        {
            var centerY = block.Bounds.Y + block.Bounds.Height / 2d;
            var line = lines.LastOrDefault();
            if (line is null || Math.Abs(centerY - line.Average(item => item.Bounds.Y + item.Bounds.Height / 2d)) > lineTolerance)
            {
                line = [];
                lines.Add(line);
            }

            line.Add(block);
        }

        return string.Join(
            Environment.NewLine,
            lines.Select(line => string.Join(" ", line.OrderBy(block => block.Bounds.X).Select(block => block.Text.Trim()))));
    }

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= 6
            ? new string('*', value.Length)
            : $"{value[..2]}{new string('*', value.Length - 4)}{value[^2..]}";
    }
}
