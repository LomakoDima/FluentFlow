using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FluentFlow.Controls;

// A row of mutually exclusive choices (a ListBox underneath, so selection, binding and keyboard work as usual). The
// chosen one sits on a pill that glides over from the previous choice instead of jumping; the look is in
// Resources/SettingsStyles.xaml, which names the pill PART_Pill.
public sealed class Segmented : ListBox
{
    private FrameworkElement? _pill;
    private readonly TranslateTransform _pillOffset = new();

    static Segmented()
        => DefaultStyleKeyProperty.OverrideMetadata(typeof(Segmented), new FrameworkPropertyMetadata(typeof(Segmented)));

    public Segmented()
    {
        SizeChanged += (_, _) => MovePill(animate: false);
        // Containers are created after the first layout; place the pill once they exist.
        ItemContainerGenerator.StatusChanged += (_, _) => Dispatcher.BeginInvoke(() => MovePill(animate: false));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _pill = GetTemplateChild("PART_Pill") as FrameworkElement;
        if (_pill is not null) _pill.RenderTransform = _pillOffset;
        MovePill(animate: false);
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        MovePill(animate: IsLoaded);
    }

    private void MovePill(bool animate)
    {
        if (_pill is null) return;
        if (SelectedIndex < 0 || ItemContainerGenerator.ContainerFromIndex(SelectedIndex) is not FrameworkElement chosen
            || chosen.ActualWidth <= 0 || _pill.Parent is not UIElement host)
        {
            _pill.Visibility = Visibility.Hidden;
            return;
        }

        var wasHidden = _pill.Visibility != Visibility.Visible;
        _pill.Visibility = Visibility.Visible;
        var at = chosen.TranslatePoint(new Point(0, 0), host);
        Motion.Glide(_pillOffset, TranslateTransform.XProperty, at.X, animate && !wasHidden, Motion.Normal);
        Motion.Glide(_pill, WidthProperty, chosen.ActualWidth, animate && !wasHidden, Motion.Normal);
        _pill.Height = chosen.ActualHeight;
        _pill.VerticalAlignment = VerticalAlignment.Top;
        _pill.Margin = new Thickness(0, at.Y, 0, 0);
    }
}
