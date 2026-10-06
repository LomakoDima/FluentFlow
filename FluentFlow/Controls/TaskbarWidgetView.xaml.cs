using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluentFlow.Services;

namespace FluentFlow.Controls;

// The widget's content: track title and visualizer, laid out and styled from AppSettings. The same view sits on the
// taskbar and in the settings preview, so what the user tunes is exactly what the taskbar shows.
public partial class TaskbarWidgetView : UserControl
{
    private const string PreviewTitle = "Midnight Drive";
    private readonly AppSettings _settings;
    private readonly MediaSessionService? _media;
    private readonly bool _preview;

    // preview: show a sample title when nothing is playing, so the settings window always has something to show.
    public TaskbarWidgetView(AppSettings settings, MediaSessionService? media, AudioVisualizerService visualizer, bool preview = false)
    {
        InitializeComponent();
        _settings = settings;
        _media = media;
        _preview = preview;
        Visualizer.Service = visualizer;
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
        Apply();
    }

    // Raised after the settings or the track changed what the view needs to look like (its width may be different).
    public event EventHandler? AppearanceChanged;

    // The width this view wants, in DIPs, given the current settings and title.

    public double MeasureDesiredWidth()
    {
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return DesiredSize.Width;
    }

    private void Subscribe()
    {
        Unsubscribe();
        _settings.PropertyChanged += Settings_PropertyChanged;
        if (_media is not null) _media.PropertyChanged += Media_PropertyChanged;
        Apply();
    }

    private void Unsubscribe()
    {
        _settings.PropertyChanged -= Settings_PropertyChanged;
        if (_media is not null) _media.PropertyChanged -= Media_PropertyChanged;
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e) => Apply();

    // The media service reports a new position every second; only a different title changes what we show.
    private void Media_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if ((BuildTitle() ?? string.Empty) != TitleText.Text) Apply();
    }

    private void Apply()
    {
        var settings = _settings;
        Root.Margin = new Thickness(settings.WidgetPadding, 0, settings.WidgetPadding, 0);

        Visualizer.Width = settings.VisualizerWidth;
        Visualizer.Height = settings.VisualizerHeight;
        Visualizer.IsMirrored = settings.Mirrored;
        Visualizer.BarThickness = settings.BarThickness;
        Visualizer.BarRadius = settings.BarRadius;
        Visualizer.Opacity = settings.Opacity;
        ApplyColor(settings);

        var title = BuildTitle();
        TitleText.Text = title ?? string.Empty;
        TitleText.Visibility = string.IsNullOrEmpty(title) ? Visibility.Collapsed : Visibility.Visible;
        TitleText.FontSize = settings.TitleFontSize;
        TitleText.MaxWidth = settings.TitleMaxWidth;

        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    // DynamicResource keeps the two system-driven colours in step with Windows; a custom colour is a fixed brush.
    private void ApplyColor(AppSettings settings)
    {
        switch (settings.ColorMode)
        {
            case VisualizerColorMode.Windows:
                Visualizer.SetResourceReference(AudioVisualizer.BarBrushProperty, "WindowsAccentBrush");
                break;
            case VisualizerColorMode.Custom:
                Visualizer.BarBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.CustomColor));
                break;
            default:
                Visualizer.SetResourceReference(AudioVisualizer.BarBrushProperty, "AccentBrush");
                break;
        }
    }

    private string? BuildTitle()
    {
        if (!_settings.ShowTitle) return null;
        var media = _media?.CurrentMedia;
        if (media is { HasSession: true } && !string.IsNullOrWhiteSpace(media.Title))
        {
            return _settings.ShowArtist && !string.IsNullOrWhiteSpace(media.Artist)
                ? $"{media.Title} – {media.Artist}"
                : media.Title;
        }
        return _preview ? PreviewTitle : null;
    }
}
