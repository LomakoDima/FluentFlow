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

    // The app theme last applied; windows that tint their own frame read it.
    public bool IsLight { get; private set; }

    // Raised after the brushes were replaced (at startup and on every Windows theme or accent change).
    public event EventHandler? Applied;

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
        IsLight = light;
        var accent = ToColor(_settings.GetColorValue(light ? UIColorType.AccentDark1 : UIColorType.AccentLight2));
        var resources = Application.Current.Resources;

        void Set(string key, string color) => resources[key] = Brush(ToColor(color));

        // Settings window (Resources/SettingsStyles.xaml). It sits on Mica, so most surfaces are thin layers of white
        // or black over it rather than solid colours.
        if (light)
        {
            Set("SettingsWindowBrush", "#FFF3F3F3");
            Set("SettingsLayerBrush", "#80FFFFFF");
            Set("SettingsLayerEdgeBrush", "#0F000000");
            Set("SettingsCardBrush", "#B3FFFFFF");
            Set("SettingsCardStrokeBrush", "#0F000000");
            Set("SettingsDividerBrush", "#0F000000");
            Set("SettingsTextBrush", "#E4000000");
            Set("SettingsSecondaryTextBrush", "#9E000000");
            Set("SettingsTertiaryTextBrush", "#72000000");
            Set("SettingsControlBrush", "#B3FFFFFF");
            Set("SettingsControlHoverBrush", "#80F9F9F9");
            Set("SettingsControlStrokeBrush", "#14000000");
            Set("SettingsControlStrongBrush", "#72000000");
            Set("SettingsTrackBrush", "#29000000");
            Set("SettingsThumbBrush", "#FFFFFFFF");
            Set("SettingsPillBrush", "#FFFFFFFF");
            Set("SettingsSubtleHoverBrush", "#09000000");
            Set("SettingsSubtlePressedBrush", "#06000000");
            Set("SettingsSelectedBrush", "#0C000000");
            Set("SettingsOnAccentBrush", "#FFFFFFFF");
            Set("SettingsDangerBrush", "#FFC42B1C");
            Set("SettingsOnDangerBrush", "#FFFFFFFF");
        }
        else
        {
            Set("SettingsWindowBrush", "#FF202020");
            Set("SettingsLayerBrush", "#4C3A3A3A");
            Set("SettingsLayerEdgeBrush", "#19FFFFFF");
            Set("SettingsCardBrush", "#0DFFFFFF");
            Set("SettingsCardStrokeBrush", "#12FFFFFF");
            Set("SettingsDividerBrush", "#15FFFFFF");
            Set("SettingsTextBrush", "#FFFFFFFF");
            Set("SettingsSecondaryTextBrush", "#C5FFFFFF");
            Set("SettingsTertiaryTextBrush", "#87FFFFFF");
            Set("SettingsControlBrush", "#0FFFFFFF");
            Set("SettingsControlHoverBrush", "#15FFFFFF");
            Set("SettingsControlStrokeBrush", "#14FFFFFF");
            Set("SettingsControlStrongBrush", "#8BFFFFFF");
            Set("SettingsTrackBrush", "#33FFFFFF");
            Set("SettingsThumbBrush", "#FF454545");
            Set("SettingsPillBrush", "#24FFFFFF");
            Set("SettingsSubtleHoverBrush", "#0FFFFFFF");
            Set("SettingsSubtlePressedBrush", "#0AFFFFFF");
            Set("SettingsSelectedBrush", "#14FFFFFF");
            Set("SettingsOnAccentBrush", "#FF000000");
            Set("SettingsDangerBrush", "#FFFF99A4");
            Set("SettingsOnDangerBrush", "#FF000000");
        }
        // The accent variant Windows itself uses on this background, so it always reads well.
        resources["SettingsAccentBrush"] = Brush(accent);
        resources["WindowsAccentBrush"] = Brush(ToColor(_settings.GetColorValue(UIColorType.Accent)));

        // The taskbar follows the system theme, not the app theme, so its text colour is chosen separately.
        var taskbarLight = _forceLight ?? WindowsTaskbarInfo.SystemUsesLightTheme;
        Set("TaskbarTextBrush", taskbarLight ? "#E4000000" : "#FFFFFFFF");
        Set("TaskbarSecondaryTextBrush", taskbarLight ? "#9E000000" : "#C5FFFFFF");
        Set("TaskbarBackgroundBrush", taskbarLight ? "#FFF3F3F3" : "#FF1C1C1C");
        Set("TaskbarEdgeBrush", taskbarLight ? "#1F000000" : "#14FFFFFF");

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
        Applied?.Invoke(this, EventArgs.Empty);
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
