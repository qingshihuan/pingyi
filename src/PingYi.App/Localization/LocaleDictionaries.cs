using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PingYi.App.Localization;

public sealed partial class ChineseStrings : ResourceDictionary
{
    public ChineseStrings() => AvaloniaXamlLoader.Load(this);
}
public sealed partial class EnglishStrings : ResourceDictionary
{
    public EnglishStrings() => AvaloniaXamlLoader.Load(this);
}
