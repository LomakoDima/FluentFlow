using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.UI.ViewManagement;

namespace FluentFlow.Services;

// Publishes the Windows app theme and accent colour as the Flyout* brushes in the application resources.
// Flyout XAML uses DynamicResource, so replacing a brush restyles an open flyout immediately.
public sealed class SystemThemeService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly UISettings _settings = new();
    private readonly bool? _forceLight;
    private bool _disposed;

    // forceLight is for testing only (--theme light|dark); null follows Windows.
    public SystemThemeService(Dispatcher dispatcher, bool? forceLight = null)
    {
        _dispatcher = dispatcher;
        _forceLight = forceLight;
        _settings.ColorValuesChanged += Settings_ColorValuesChanged;
    }

    public void Apply()
    {
        if (_disposed) return;
        var light = _forceLight ?? IsLightTheme();
        var accent = ToColor(_settings.GetColorValue(light ? UIColorType.AccentDark1 : UIColorType.AccentLight2));
        var resources = Application.Current.Resources;

        void Set(string key, string color) => resources[key] = Brush(ToColor(color));

        if (light)
        {
            Set("FlyoutSurfaceBrush", "#F7F3F3F3");
            Set("FlyoutBorderBrush", "#33000000");
            Set("FlyoutDividerBrush", "#14000000");
            Set("FlyoutTextBrush", "#E4000000");
            Set("FlyoutSecondaryTextBrush", "#9E000000");
            Set("FlyoutHoverBrush", "#0D000000");
            Set("FlyoutPressedBrush", "#08000000");
            Set("FlyoutTrackBrush", "#72000000");
            Set("FlyoutThumbFaceBrush", "#FFFFFFFF");
            Set("FlyoutThumbStrokeBrush", "#33000000");
        }
        else
        {
            Set("FlyoutSurfaceBrush", "#F72C2C2C");
            Set("FlyoutBorderBrush", "#66757575");
            Set("FlyoutDividerBrush", "#15FFFFFF");
            Set("FlyoutTextBrush", "#FFFFFFFF");
            Set("FlyoutSecondaryTextBrush", "#C5FFFFFF");
            Set("FlyoutHoverBrush", "#0FFFFFFF");
            Set("FlyoutPressedBrush", "#0AFFFFFF");
            Set("FlyoutTrackBrush", "#8BFFFFFF");
            Set("FlyoutThumbFaceBrush", "#FF454545");
            Set("FlyoutThumbStrokeBrush", "#33FFFFFF");
        }
        resources["FlyoutAccentBrush"] = Brush(accent);
    }

    // UIColorType.Background is white in the light app mode and black in the dark one.
    private bool IsLightTheme()
    {
        var background = _settings.GetColorValue(UIColorType.Background);
        return background.R + background.G + background.B > 3 * 127;
    }

    // Raised on a background thread whenever the theme or accent changes.
    private void Settings_ColorValuesChanged(UISettings sender, object args)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.InvokeAsync(Apply);
    }

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color ToColor(Windows.UI.Color color) => Color.FromArgb(color.A, color.R, color.G, color.B);
    private static Color ToColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.ColorValuesChanged -= Settings_ColorValuesChanged;
    }
}
