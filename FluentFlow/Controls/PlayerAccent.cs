using System.Windows;
using System.Windows.Media;

namespace FluentFlow.Controls;

// The player's accent brush, inherited down the tree from the window, which recolours it with every cover. It is an
// inherited property rather than a resource on purpose: WPF freezes a brush that a style reaches through
// DynamicResource, and a frozen brush can't change colour.
public static class PlayerAccent
{
    public static readonly DependencyProperty BrushProperty = DependencyProperty.RegisterAttached(
        "Brush", typeof(Brush), typeof(PlayerAccent),
        new FrameworkPropertyMetadata(new SolidColorBrush(ArtworkPalette.Default.Accent).GetAsFrozen(),
            FrameworkPropertyMetadataOptions.Inherits));

    public static Brush GetBrush(DependencyObject element) => (Brush)element.GetValue(BrushProperty);

    public static void SetBrush(DependencyObject element, Brush value) => element.SetValue(BrushProperty, value);
}
