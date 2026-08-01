#nullable enable
using System.Windows;

namespace Kor.Operations.Converters
{
    /// <summary>
    /// Round 52: Freezable that carries a binding source into places outside
    /// the visual tree — DataGrid columns can't use ElementName/RelativeSource,
    /// but they can bind through <c>Source={StaticResource proxy}</c>. Data is
    /// a DependencyProperty so assignments after parse (e.g. from a window
    /// ctor) propagate to existing bindings.
    /// </summary>
    public sealed class BindingProxy : Freezable
    {
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));

        public object? Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        protected override Freezable CreateInstanceCore() => new BindingProxy();
    }
}
