using System.IO;
using FluentFlow.Services;

namespace FluentFlow.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FluentFlowTests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "nested", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Defaults_match_the_original_widget_look()
    {
        var settings = new AppSettings();
        Assert.Equal(WidgetAlignment.Auto, settings.Alignment);
        Assert.Equal(96, settings.VisualizerWidth);
        Assert.Equal(24, settings.VisualizerHeight);
        Assert.True(settings.Mirrored);
        Assert.True(settings.ShowTitle);
        Assert.False(settings.ShowArtist);
        Assert.Equal(VisualizerColorMode.Accent, settings.ColorMode);
    }

    [Theory]
    [InlineData(-5, AppSettings.MinVisualizerWidth)]
    [InlineData(10, AppSettings.MinVisualizerWidth)]
    [InlineData(150, 150)]
    [InlineData(9999, AppSettings.MaxVisualizerWidth)]
    public void Numbers_are_clamped_to_their_range(double given, double expected)
    {
        var settings = new AppSettings { VisualizerWidth = given };
        Assert.Equal(expected, settings.VisualizerWidth);
    }

    [Fact]
    public void Nan_and_infinity_are_ignored()
    {
        var settings = new AppSettings { VisualizerWidth = 150 };
        settings.VisualizerWidth = double.NaN;
        settings.VisualizerWidth = double.PositiveInfinity;
        Assert.Equal(150, settings.VisualizerWidth);
    }

    [Fact]
    public void An_unknown_enum_value_falls_back_to_the_default()
    {
        var settings = new AppSettings { Alignment = (WidgetAlignment)99, ColorMode = (VisualizerColorMode)42 };
        Assert.Equal(WidgetAlignment.Auto, settings.Alignment);
        Assert.Equal(VisualizerColorMode.Accent, settings.ColorMode);
    }

    [Theory]
    [InlineData("#a1b2c3", "#A1B2C3")]
    [InlineData("  #ff375f ", "#FF375F")]
    [InlineData("#80FF375F", "#80FF375F")]
    public void Valid_hex_colours_are_accepted_and_normalised(string given, string expected)
    {
        var settings = new AppSettings { CustomColor = given };
        Assert.Equal(expected, settings.CustomColor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#abc")]
    [InlineData("#GGGGGG")]
    [InlineData("A1B2C3")]
    [InlineData(null)]
    public void Invalid_colours_are_rejected_and_the_old_one_is_kept(string? given)
    {
        var settings = new AppSettings { CustomColor = "#112233" };
        settings.CustomColor = given!;
        Assert.Equal("#112233", settings.CustomColor);
    }

    [Fact]
    public void Property_changed_fires_for_real_changes_only()
    {
        var settings = new AppSettings();
        var names = new List<string?>();
        settings.PropertyChanged += (_, e) => names.Add(e.PropertyName);

        settings.Opacity = settings.Opacity;      // same value
        settings.Opacity = 0.5;
        settings.ShowTitle = false;
        settings.VisualizerWidth = 5;             // clamps to 48: a change
        settings.VisualizerWidth = 7;             // clamps to 48 again: no change

        Assert.Equal(["Opacity", "ShowTitle", "VisualizerWidth"], names);
    }

    [Fact]
    public void Settings_survive_a_save_and_load()
    {
        var original = new AppSettings
        {
            Alignment = WidgetAlignment.Center, EdgeOffset = 40, VisualizerWidth = 180, VisualizerHeight = 20,
            WidgetPadding = 8, BarThickness = 0.8, BarRadius = 3, Mirrored = false, Opacity = 0.7,
            ColorMode = VisualizerColorMode.Custom, CustomColor = "#0A84FF", ShowTitle = false, ShowArtist = true,
            TitleMaxWidth = 220, TitleFontSize = 14
        };
        Assert.True(original.Save(FilePath));

        var loaded = AppSettings.Load(FilePath);
        Assert.Equal(WidgetAlignment.Center, loaded.Alignment);
        Assert.Equal(40, loaded.EdgeOffset);
        Assert.Equal(180, loaded.VisualizerWidth);
        Assert.Equal(0.8, loaded.BarThickness);
        Assert.False(loaded.Mirrored);
        Assert.Equal(VisualizerColorMode.Custom, loaded.ColorMode);
        Assert.Equal("#0A84FF", loaded.CustomColor);
        Assert.False(loaded.ShowTitle);
        Assert.True(loaded.ShowArtist);
        Assert.Equal(220, loaded.TitleMaxWidth);
        Assert.Equal(FilePath, loaded.FilePath);
    }

    [Fact]
    public void The_file_is_readable_text_with_enum_names()
    {
        new AppSettings { Alignment = WidgetAlignment.Left }.Save(FilePath);
        var text = File.ReadAllText(FilePath);
        Assert.Contains("\"Alignment\": \"Left\"", text);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void A_missing_file_gives_defaults()
    {
        var loaded = AppSettings.Load(FilePath);
        Assert.Equal(96, loaded.VisualizerWidth);
        Assert.Equal(FilePath, loaded.FilePath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{ \"VisualizerWidth\": ")]
    [InlineData("[1,2,3]")]
    [InlineData("{ \"Alignment\": \"Sideways\" }")]
    public void A_corrupt_file_gives_defaults_instead_of_crashing(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);
        var loaded = AppSettings.Load(FilePath);
        Assert.Equal(96, loaded.VisualizerWidth);
        Assert.Equal(WidgetAlignment.Auto, loaded.Alignment);
    }

    [Fact]
    public void Out_of_range_values_in_a_hand_edited_file_are_clamped()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ \"VisualizerWidth\": 99999, \"Opacity\": -3, \"CustomColor\": \"nope\", \"Unknown\": 1 }");
        var loaded = AppSettings.Load(FilePath);
        Assert.Equal(AppSettings.MaxVisualizerWidth, loaded.VisualizerWidth);
        Assert.Equal(AppSettings.MinOpacity, loaded.Opacity);
        Assert.Equal(AppSettings.DefaultCustomColor, loaded.CustomColor);
    }

    [Fact]
    public void Reset_restores_every_default_and_reports_the_changes()
    {
        var settings = new AppSettings { VisualizerWidth = 200, ShowTitle = false, Alignment = WidgetAlignment.Right };
        var changes = 0;
        settings.PropertyChanged += (_, _) => changes++;
        settings.ResetToDefaults();
        Assert.Equal(96, settings.VisualizerWidth);
        Assert.True(settings.ShowTitle);
        Assert.Equal(WidgetAlignment.Auto, settings.Alignment);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void Profiles_get_their_own_file_and_names_are_sanitised()
    {
        Assert.Equal(AppSettings.DefaultPath, AppSettings.PathForProfile(null));
        Assert.Equal(AppSettings.DefaultPath, AppSettings.PathForProfile("  "));
        Assert.EndsWith(Path.Combine("profiles", "dev", "settings.json"), AppSettings.PathForProfile("dev"));
        Assert.EndsWith(Path.Combine("profiles", "etcpasswd", "settings.json"), AppSettings.PathForProfile("..\\..\\etc/passwd"));
    }
}
