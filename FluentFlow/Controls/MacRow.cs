using System.Windows;
using System.Windows.Controls;

namespace FluentFlow.Controls;

// One row of a settings card: a title (and optional description) on the left, the control on the right. The look is
// in Resources/MacStyles.xaml; the content is whatever control the row hosts.
public sealed class MacRow : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(MacRow), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(MacRow), new PropertyMetadata(string.Empty));

    static MacRow() => DefaultStyleKeyProperty.OverrideMetadata(typeof(MacRow), new FrameworkPropertyMetadata(typeof(MacRow)));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
