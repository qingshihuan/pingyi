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
        var kana = 0;
        var hangul = 0;
        var cyrillic = 0;
        var arabic = 0;
        var hebrew = 0;
        var devanagari = 0;
        var thai = 0;
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
            else if (rune.Value is >= 0x3040 and <= 0x30FF)
            {
                kana++;
            }
            else if (rune.Value is >= 0xAC00 and <= 0xD7AF)
            {
                hangul++;
            }
            else if (rune.Value is >= 0x0400 and <= 0x052F)
            {
                cyrillic++;
            }
            else if (rune.Value is >= 0x0600 and <= 0x06FF)
            {
                arabic++;
            }
            else if (rune.Value is >= 0x0590 and <= 0x05FF)
            {
                hebrew++;
            }
            else if (rune.Value is >= 0x0900 and <= 0x097F)
            {
                devanagari++;
            }
            else if (rune.Value is >= 0x0E00 and <= 0x0E7F)
            {
                thai++;
            }
        }

        if (kana > 0) return "ja";
        if (hangul > 0) return "ko";
        if (cyrillic > 0) return "ru";
        if (arabic > 0) return "ar";
        if (hebrew > 0) return "he";
        if (devanagari > 0) return "hi";
        if (thai > 0) return "th";
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
