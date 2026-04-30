#nullable enable
using System.Windows.Controls;

namespace Kor.Operations;

internal static class ContextMenuExtensions
{
    public static void Hide(this ContextMenu? menu)
    {
        if (menu == null) return;
        try { menu.IsOpen = false; } catch (Exception) { /* WPF can throw if menu is in a transitional state */ }
    }
}
