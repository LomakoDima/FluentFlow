using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FluentFlow.Services;

// Where the widget goes on the taskbar. Auto follows the Windows taskbar icon alignment: with centred icons the free
// space is at the edges (widget goes to the far side), with left-aligned icons it is next to the notification area.
public enum WidgetAlignment { Auto, Left, Center, Right }

public enum VisualizerColorMode
{
    Accent,  // FluentFlow's own peach accent
    Windows, // the Windows accent colour
    Custom   // CustomColor
}

// Everything the user can customise. Setters clamp or reject bad values, so a hand-edited or corrupt file can never put
// the widget in an impossible state. Plain .NET types only: the file is JSON and the class is easy to test.
public sealed class AppSettings : INotifyPropertyChanged
{
    public const double MinVisualizerWidth = 48, MaxVisualizerWidth = 320;
    public const double MinVisualizerHeight = 12, MaxVisualizerHeight = 32;
    public const double MinPadding = 0, MaxPadding = 32;
    public const double MinEdgeOffset = 0, MaxEdgeOffset = 200;
    public const double MinBarThickness = 0.3, MaxBarThickness = 1;
    public const double MinBarRadius = 0, MaxBarRadius = 4;
    public const double MinOpacity = 0.2, MaxOpacity = 1;
    public const double MinTitleWidth = 60, MaxTitleWidth = 320;
    public const double MinTitleFontSize = 10, MaxTitleFontSize = 16;
    public const string DefaultCustomColor = "#D7AC99";

    private static readonly Regex HexColor = new("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private WidgetAlignment _alignment = WidgetAlignment.Auto;
    private double _edgeOffset;
    private double _visualizerWidth = 96;
    private double _visualizerHeight = 24;
    private double _widgetPadding = 14;
    private double _barThickness = 0.6;
    private double _barRadius = 1.5;
    private bool _mirrored = true;
    private VisualizerColorMode _colorMode = VisualizerColorMode.Accent;
    private string _customColor = DefaultCustomColor;
    private double _opacity = 0.9;
    private bool _showTitle = true;
    private bool _showArtist;
    private double _titleMaxWidth = 160;
    private double _titleFontSize = 12;

    public event PropertyChangedEventHandler? PropertyChanged;

    // Position
    public WidgetAlignment Alignment
    {
        get => _alignment;
        set => Set(ref _alignment, Enum.IsDefined(value) ? value : WidgetAlignment.Auto);
    }

    // Extra distance from the edge or neighbour the widget is aligned to, in DIPs.
    public double EdgeOffset { get => _edgeOffset; set => SetClamped(ref _edgeOffset, value, MinEdgeOffset, MaxEdgeOffset); }

    // Visualizer
    public double VisualizerWidth { get => _visualizerWidth; set => SetClamped(ref _visualizerWidth, value, MinVisualizerWidth, MaxVisualizerWidth); }
    public double VisualizerHeight { get => _visualizerHeight; set => SetClamped(ref _visualizerHeight, value, MinVisualizerHeight, MaxVisualizerHeight); }
    public double WidgetPadding { get => _widgetPadding; set => SetClamped(ref _widgetPadding, value, MinPadding, MaxPadding); }
    // Bar width as a fraction of its slot.
    public double BarThickness { get => _barThickness; set => SetClamped(ref _barThickness, value, MinBarThickness, MaxBarThickness); }
    public double BarRadius { get => _barRadius; set => SetClamped(ref _barRadius, value, MinBarRadius, MaxBarRadius); }
    public bool Mirrored { get => _mirrored; set => Set(ref _mirrored, value); }
    public double Opacity { get => _opacity; set => SetClamped(ref _opacity, value, MinOpacity, MaxOpacity); }

    public VisualizerColorMode ColorMode
    {
        get => _colorMode;
        set => Set(ref _colorMode, Enum.IsDefined(value) ? value : VisualizerColorMode.Accent);
    }

    // "#RRGGBB" or "#AARRGGBB"; anything else is ignored.
    public string CustomColor
    {
        get => _customColor;
        set
        {
            if (value is not null && HexColor.IsMatch(value.Trim())) Set(ref _customColor, value.Trim().ToUpperInvariant());
        }
    }

    // Track title
    public bool ShowTitle { get => _showTitle; set => Set(ref _showTitle, value); }
    public bool ShowArtist { get => _showArtist; set => Set(ref _showArtist, value); }
    public double TitleMaxWidth { get => _titleMaxWidth; set => SetClamped(ref _titleMaxWidth, value, MinTitleWidth, MaxTitleWidth); }
    public double TitleFontSize { get => _titleFontSize; set => SetClamped(ref _titleFontSize, value, MinTitleFontSize, MaxTitleFontSize); }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluentFlow", "settings.json");

    // A named profile keeps its own settings, so several copies (or a test run) never touch the main file.
    public static string PathForProfile(string? profile) => string.IsNullOrWhiteSpace(profile)
        ? DefaultPath
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluentFlow", "profiles",
            string.Concat(profile.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')), "settings.json");

    // Where Save() writes by default: the file this instance was loaded from.
    [JsonIgnore]
    public string? FilePath { get; private set; }

    // A missing, unreadable or corrupt file simply means defaults; settings must never stop the app from starting.
    public static AppSettings Load(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
                loaded.FilePath = path;
                return loaded;
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read settings: {exception.Message}");
        }
        return new AppSettings { FilePath = path ?? DefaultPath };
    }

    // Writes to a temporary file first so a crash mid-write can't leave a half-written settings file.
    public bool Save(string? path = null)
    {
        try
        {
            path ??= FilePath ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not save settings: {exception.Message}");
            return false;
        }
    }

    public void ResetToDefaults() => CopyFrom(new AppSettings());

    public void CopyFrom(AppSettings other)
    {
        Alignment = other.Alignment;
        EdgeOffset = other.EdgeOffset;
        VisualizerWidth = other.VisualizerWidth;
        VisualizerHeight = other.VisualizerHeight;
        WidgetPadding = other.WidgetPadding;
        BarThickness = other.BarThickness;
        BarRadius = other.BarRadius;
        Mirrored = other.Mirrored;
        Opacity = other.Opacity;
        ColorMode = other.ColorMode;
        CustomColor = other.CustomColor;
        ShowTitle = other.ShowTitle;
        ShowArtist = other.ShowArtist;
        TitleMaxWidth = other.TitleMaxWidth;
        TitleFontSize = other.TitleFontSize;
    }

    private void SetClamped(ref double field, double value, double minimum, double maximum, [CallerMemberName] string? name = null)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;
        Set(ref field, Math.Clamp(value, minimum, maximum), name);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
