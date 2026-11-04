using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace PingYi.App;

public sealed class WorkspaceStyles : Styles
{
    public WorkspaceStyles() => AvaloniaXamlLoader.Load(this);
}
