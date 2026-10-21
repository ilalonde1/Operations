#nullable enable
using System;
using System.Linq;
using System.Windows;

namespace Kor.Operations.App.BusinessDevelopment;

/// <summary>
/// Gives every BD window the BD look (Director/BdLook.xaml) as it loads (Ian, 21 Oct 2026: "I want nothing old looking"):
/// BD Director, the BD Workspace, and every window and dialog from the BD, pursuits, opportunities, brochure and fee-proposal
/// code, present and future, without editing each one. Engineering and IT windows are left as they are. Implicit styles
/// only, so a screen that sets its own style keeps it.
/// </summary>
public static class BdLookApplier
{
    private static readonly string[] BdNamespaces =
    {
        "Kor.Operations.App.BusinessDevelopment", "Kor.Operations.App.Crm", "Kor.Operations.App.Opportunities",
        "Kor.Operations.Brochures", "Kor.Operations.App.Brochures", "Kor.Operations.App.FeeProposal",
    };

    private static readonly Uri Source = new("pack://application:,,,/Kor.Operations.App;component/BusinessDevelopment/Director/BdLook.xaml", UriKind.Absolute);
    private static ResourceDictionary? _look;
    private static bool _registered;

    /// <summary>Once, at startup: every Window that initializes from now on is checked.</summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window w) Apply(w);
        }));
    }

    /// <summary>True for a window from the BD code.</summary>
    public static bool IsBd(Type type) =>
        type.Namespace is { } ns && BdNamespaces.Any(p => ns.Equals(p, StringComparison.Ordinal) || ns.StartsWith(p + ".", StringComparison.Ordinal));

    public static void Apply(Window window)
    {
        if (!IsBd(window.GetType())) return;
        try
        {
            _look ??= new ResourceDictionary { Source = Source };
            if (!window.Resources.MergedDictionaries.Contains(_look)) window.Resources.MergedDictionaries.Add(_look);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "BD look: could not apply to {Window}", window.GetType().Name);
        }
    }
}
