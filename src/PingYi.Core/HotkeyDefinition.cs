namespace PingYi.Core;

/// <summary>The portable shortcut subset implemented by both native backends.</summary>
public readonly record struct HotkeyDefinition(bool Control, bool Alt, bool Shift, char Key)
{
    public static bool TryParse(string? text, out HotkeyDefinition gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        bool control = false, alt = false, shift = false;
        char? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                if (control) return false;
                control = true;
            }
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                if (alt) return false;
                alt = true;
            }
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                if (shift) return false;
                shift = true;
            }
            else if (part.Length == 1 && char.IsAsciiLetterOrDigit(part[0]) && key is null)
                key = char.ToUpperInvariant(part[0]);
            else return false;
        }
        if (key is null || !(control || alt || shift)) return false;
        gesture = new(control, alt, shift, key.Value);
        return true;
    }

    public static string Normalize(string text) => TryParse(text, out var gesture)
        ? gesture.ToString()
        : throw new NotSupportedException("快捷键必须包含 Ctrl、Alt 或 Shift，以及一个字母或数字；不能重复或包含空项。");

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Control) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(char.ToUpperInvariant(Key).ToString());
        return string.Join('+', parts);
    }
}
