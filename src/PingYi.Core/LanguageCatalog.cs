namespace PingYi.Core;

public sealed record LanguageDefinition(
    string Code,
    string DisplayName,
    string PromptName,
    string EnglishDisplayName);

public static class LanguageCatalog
{
    public const string Auto = "auto";
    public const string AutoOpposite = "auto-opposite";

    public static IReadOnlyList<LanguageDefinition> All { get; } =
    [
        new("zh", "简体中文", "简体中文", "Chinese (Simplified)"),
        new("zh-Hant", "繁體中文", "繁體中文", "Chinese (Traditional)"),
        new("en", "英语", "英语", "English"),
        new("ja", "日语", "日语", "Japanese"),
        new("ko", "韩语", "韩语", "Korean"),
        new("fr", "法语", "法语", "French"),
        new("de", "德语", "德语", "German"),
        new("es", "西班牙语", "西班牙语", "Spanish"),
        new("pt", "葡萄牙语", "葡萄牙语", "Portuguese"),
        new("it", "意大利语", "意大利语", "Italian"),
        new("ru", "俄语", "俄语", "Russian"),
        new("uk", "乌克兰语", "乌克兰语", "Ukrainian"),
        new("pl", "波兰语", "波兰语", "Polish"),
        new("nl", "荷兰语", "荷兰语", "Dutch"),
        new("sv", "瑞典语", "瑞典语", "Swedish"),
        new("no", "挪威语", "挪威语", "Norwegian"),
        new("da", "丹麦语", "丹麦语", "Danish"),
        new("fi", "芬兰语", "芬兰语", "Finnish"),
        new("cs", "捷克语", "捷克语", "Czech"),
        new("ro", "罗马尼亚语", "罗马尼亚语", "Romanian"),
        new("hu", "匈牙利语", "匈牙利语", "Hungarian"),
        new("tr", "土耳其语", "土耳其语", "Turkish"),
        new("el", "希腊语", "希腊语", "Greek"),
        new("ar", "阿拉伯语", "阿拉伯语", "Arabic"),
        new("fa", "波斯语", "波斯语", "Persian"),
        new("he", "希伯来语", "希伯来语", "Hebrew"),
        new("hi", "印地语", "印地语", "Hindi"),
        new("bn", "孟加拉语", "孟加拉语", "Bengali"),
        new("th", "泰语", "泰语", "Thai"),
        new("vi", "越南语", "越南语", "Vietnamese"),
        new("id", "印度尼西亚语", "印度尼西亚语", "Indonesian"),
        new("ms", "马来语", "马来语", "Malay"),
        new("tl", "菲律宾语", "菲律宾语", "Filipino"),
        new("sw", "斯瓦希里语", "斯瓦希里语", "Swahili")
    ];

    public static IReadOnlyList<string> Codes { get; } = All
        .Select(language => language.Code)
        .ToArray();

    public static bool IsKnown(string? code) =>
        All.Any(language => string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase));

    public static string NormalizeSource(string? code) =>
        IsKnown(code) ? Canonicalize(code!) : Auto;

    public static string NormalizeTarget(string? code) =>
        IsKnown(code) ? Canonicalize(code!) : AutoOpposite;

    public static string GetDisplayName(string code) =>
        Find(code)?.DisplayName ?? code;

    public static string GetPromptName(string code) =>
        Find(code)?.PromptName ?? code;

    private static string Canonicalize(string code) => Find(code)?.Code ?? code;

    private static LanguageDefinition? Find(string code) => All.FirstOrDefault(
        language => string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase));
}
