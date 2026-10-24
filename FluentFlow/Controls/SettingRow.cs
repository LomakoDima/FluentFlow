using System.Windows;
using System.Windows.Controls;

namespace FluentFlow.Controls;

// One row of a settings card: a title (and optional description) on the left, the control on the right. The look is
// in Resources/SettingsStyles.xaml; the content is whatever control the row hosts.
public sealed class SettingRow : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(SettingRow), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingRow), new PropertyMetadata(string.Empty));

    static SettingRow() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingRow), new FrameworkPropertyMetadata(typeof(SettingRow)));

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
