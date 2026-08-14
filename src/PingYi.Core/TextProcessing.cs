using System.Text;

namespace PingYi.Core;

public static class TextProcessing
{
    private static readonly IReadOnlyDictionary<string, HashSet<string>> LatinLanguageWords =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["fr"] = new(StringComparer.OrdinalIgnoreCase)
                { "le", "la", "les", "des", "une", "et", "est", "pour", "dans", "avec", "bonjour", "merci", "monde", "cette" },
            ["es"] = new(StringComparer.OrdinalIgnoreCase)
                { "el", "la", "los", "las", "una", "y", "es", "para", "con", "hola", "gracias", "como", "esta", "estas", "este" },
            ["de"] = new(StringComparer.OrdinalIgnoreCase)
                { "der", "die", "das", "und", "ist", "ein", "eine", "mit", "für", "guten", "morgen", "wie", "geht", "ihnen", "danke" },
            ["pt"] = new(StringComparer.OrdinalIgnoreCase)
                { "os", "uma", "um", "e", "para", "com", "olá", "obrigado", "como", "você" },
            ["it"] = new(StringComparer.OrdinalIgnoreCase)
                { "il", "lo", "gli", "le", "uno", "una", "e", "per", "con", "ciao", "grazie", "come", "questo" },
            ["nl"] = new(StringComparer.OrdinalIgnoreCase)
                { "het", "een", "en", "is", "voor", "met", "hallo", "dank", "deze" }
        };

    private static readonly IReadOnlyDictionary<string, string> StrongLatinLanguageWords =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bonjour"] = "fr",
            ["merci"] = "fr",
            ["hola"] = "es",
            ["gracias"] = "es",
            ["guten"] = "de",
            ["danke"] = "de",
            ["olá"] = "pt",
            ["obrigado"] = "pt",
            ["ciao"] = "it",
            ["grazie"] = "it"
        };

    private static readonly HashSet<string> EnglishEvidenceWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "is", "are", "to", "of", "in", "for", "with", "this", "that", "from", "on", "not",
        "you", "your", "hello", "good", "morning", "screen", "screenshot", "translation", "file", "open", "save",
        "copy", "settings", "where", "what", "how", "login", "auth", "github", "application"
    };

    private static readonly HashSet<string> StrongEnglishEvidenceWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "hello", "screen", "screenshot", "translation", "settings", "copy", "login", "auth", "github", "application"
    };

    public readonly record struct TranslationLanguageRoute(
        string SourceLanguage,
        string TargetLanguage);

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
        if (cjk > 0 && cjk * 5 >= latin)
        {
            return "zh";
        }

        return DetectLikelyLatinLanguage(text) ?? "en";
    }

    public static string DetectLanguageForOfflineFallback(string text)
    {
        var detected = DetectLanguage(text);
        if (!string.Equals(detected, "en", StringComparison.OrdinalIgnoreCase))
        {
            return detected;
        }

        var words = TokenizeLetters(text).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var evidenceCount = words.Count(EnglishEvidenceWords.Contains);
        return words.Any(StrongEnglishEvidenceWords.Contains) || evidenceCount >= 2
            ? "en"
            : "unknown";
    }

    public static string ResolveTargetLanguage(string sourceLanguage, string configuredTarget)
    {
        if (LanguageCatalog.IsKnown(configuredTarget))
        {
            return LanguageCatalog.NormalizeTarget(configuredTarget);
        }

        return sourceLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "en" : "zh";
    }

    public static TranslationLanguageRoute ResolveTranslationLanguages(
        string configuredSource,
        string configuredTarget,
        string? detectedOcrLanguage,
        string text,
        bool providerCanDetectSourceLanguage)
    {
        var hasExplicitSource = configuredSource != LanguageCatalog.Auto;
        var detectedSource = hasExplicitSource
            ? LanguageCatalog.NormalizeSource(configuredSource)
            : LanguageCatalog.IsKnown(detectedOcrLanguage)
                ? LanguageCatalog.NormalizeSource(detectedOcrLanguage)
                : DetectLanguage(text);

        if (!LanguageCatalog.IsKnown(detectedSource))
        {
            detectedSource = DetectLanguage(text);
        }

        var targetLanguage = ResolveTargetLanguage(detectedSource, configuredTarget);
        var providerSource = !hasExplicitSource && providerCanDetectSourceLanguage
            ? LanguageCatalog.Auto
            : detectedSource;
        return new TranslationLanguageRoute(providerSource, targetLanguage);
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

    private static string? DetectLikelyLatinLanguage(string text)
    {
        var words = TokenizeLetters(text).ToArray();
        foreach (var word in words)
        {
            if (StrongLatinLanguageWords.TryGetValue(word, out var language))
            {
                return language;
            }
        }

        if (text.IndexOfAny(['¿', '¡', 'ñ', 'Ñ']) >= 0) return "es";
        if (text.IndexOfAny(['ß']) >= 0) return "de";
        if (text.IndexOfAny(['ã', 'Ã', 'õ', 'Õ']) >= 0) return "pt";

        var scores = LatinLanguageWords
            .Select(pair => new
            {
                Language = pair.Key,
                Score = words.Distinct(StringComparer.OrdinalIgnoreCase).Count(pair.Value.Contains)
            })
            .OrderByDescending(item => item.Score)
            .ToArray();
        return scores.Length > 0 &&
               scores[0].Score >= 2 &&
               (scores.Length == 1 || scores[0].Score > scores[1].Score)
            ? scores[0].Language
            : null;
    }

    private static IEnumerable<string> TokenizeLetters(string text)
    {
        var word = new StringBuilder();
        foreach (var character in text)
        {
            if (char.IsLetter(character))
            {
                word.Append(char.ToLowerInvariant(character));
            }
            else if (word.Length > 0)
            {
                yield return word.ToString();
                word.Clear();
            }
        }

        if (word.Length > 0)
        {
            yield return word.ToString();
        }
    }
}
