using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PingYi.App.Localization;

public sealed partial class ChineseStrings : ResourceDictionary
{
    public ChineseStrings()
    {
        AvaloniaXamlLoader.Load(this);
        foreach (var entry in new VisionChineseStrings()) Add(entry.Key, entry.Value);
    }
}
public sealed partial class EnglishStrings : ResourceDictionary
{
    public EnglishStrings()
    {
        AvaloniaXamlLoader.Load(this);
        foreach (var entry in new VisionEnglishStrings()) Add(entry.Key, entry.Value);
    }
}
public sealed partial class VisionChineseStrings : ResourceDictionary
{
    public VisionChineseStrings() => AvaloniaXamlLoader.Load(this);
}
public sealed partial class VisionEnglishStrings : ResourceDictionary
{
    public VisionEnglishStrings() => AvaloniaXamlLoader.Load(this);
}
