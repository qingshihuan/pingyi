using Avalonia.Controls;

namespace PingYi.App;

/// <summary>Tracks the app's modal windows without reflection into Avalonia's private dialog state.</summary>
internal static class DialogPresentation
{
    private static readonly HashSet<Window> Dialogs = [];
    internal static bool IsDialog(Window window) => Dialogs.Contains(window);
    internal static async Task<T> ShowAsync<T>(Window window, Window owner)
    {
        Dialogs.Add(window);
        try { return await window.ShowDialog<T>(owner); }
        finally { Dialogs.Remove(window); }
    }
    internal static Task ShowAsync(Window window, Window owner) => ShowAsync<object?>(window, owner);
}
