using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace PingYi.App;

/// <summary>Keep status brushes resolved against the control's current theme and scope.</summary>
internal static class ThemeResources
{
    private sealed record Lease(string Key, IDisposable Binding);
    private static readonly ConditionalWeakTable<Control, Dictionary<AvaloniaProperty, Lease>> Bindings = new();

    public static void Use(Control target, AvaloniaProperty property, string resourceKey)
    {
        var values = Bindings.GetOrCreateValue(target);
        if (values.TryGetValue(property, out var current))
        {
            if (current.Key == resourceKey) return;
            current.Binding.Dispose();
        }
        // Dynamic resources honor Light/Dark dictionaries and later OS theme changes.
        // Replacing a status disposes its previous binding rather than accumulating it.
        values[property] = new Lease(resourceKey, target.Bind(property, new DynamicResourceExtension(resourceKey)));
    }
}
