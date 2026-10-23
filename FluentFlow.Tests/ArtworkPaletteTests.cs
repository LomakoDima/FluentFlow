using System.Windows.Media;
using FluentFlow.Controls;

namespace FluentFlow.Tests;

// The palette promises that whatever the cover, the accent stays readable on the player surface and the glows stay
// dark enough to sit under white text.
public class ArtworkPaletteTests
{
    public static TheoryData<string> Covers => new()
    {
        "#FFFFFF", "#000000", "#808080", "#FF0000", "#00FF00", "#0000FF", "#0A1A6B", "#FFFF00",
        "#F6F4EE", "#3B1E8C", "#E04FB0", "#12301A"
    };

    private static byte[] Solid(string hex, int pixels = 24 * 24)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var data = new byte[pixels * 4];
        for (var i = 0; i < data.Length; i += 4)
        {
            data[i] = color.B; data[i + 1] = color.G; data[i + 2] = color.R; data[i + 3] = 255;
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Covers))]
    public void Accent_is_readable_on_the_surface(string cover)
    {
        var palette = ArtworkPalette.FromPixels(Solid(cover));
        Assert.True(ArtworkPalette.Contrast(palette.Accent, ArtworkPalette.Surface) >= ArtworkPalette.MinimumAccentContrast,
            $"{cover} gave {palette.Accent}");
    }

    [Theory]
    [MemberData(nameof(Covers))]
    public void Glows_stay_dark(string cover)
    {
        var palette = ArtworkPalette.FromPixels(Solid(cover));
        Assert.True(ArtworkPalette.Luminance(palette.Glow) <= ArtworkPalette.MaximumGlowLuminance, $"{cover} gave glow {palette.Glow}");
        Assert.True(ArtworkPalette.Luminance(palette.GlowSecondary) <= ArtworkPalette.MaximumSecondaryGlowLuminance, $"{cover} gave glow {palette.GlowSecondary}");
    }

    [Fact]
    public void A_grey_cover_gets_a_nearly_neutral_accent()
    {
        var accent = ArtworkPalette.FromPixels(Solid("#808080")).Accent;
        Assert.True(Math.Max(accent.R, Math.Max(accent.G, accent.B)) - Math.Min(accent.R, Math.Min(accent.G, accent.B)) < 40);
    }

    [Fact]
    public void The_accent_keeps_the_hue_of_the_cover()
    {
        var accent = ArtworkPalette.FromPixels(Solid("#0A1A6B")).Accent;
        Assert.True(accent.B > accent.R && accent.B > accent.G, $"blue cover gave {accent}");
    }

    [Fact]
    public void A_small_colourful_detail_beats_a_large_grey_area()
    {
        // Mostly grey with a red quarter: red is the only colour there is, so it leads.
        var pixels = Solid("#777777");
        var red = Solid("#D02020", 24 * 6);
        red.CopyTo(pixels, 0);
        var accent = ArtworkPalette.FromPixels(pixels).Accent;
        Assert.True(accent.R > accent.G + 40 && accent.R > accent.B + 40, $"gave {accent}");
    }

    [Fact]
    public void Transparent_or_empty_input_falls_back_to_the_default()
    {
        Assert.Equal(ArtworkPalette.Default, ArtworkPalette.FromPixels([]));
        Assert.Equal(ArtworkPalette.Default, ArtworkPalette.FromPixels(new byte[4 * 16]));
        Assert.Equal(ArtworkPalette.Default, ArtworkPalette.From(null));
    }

    [Fact]
    public void The_default_accent_is_readable_too()
        => Assert.True(ArtworkPalette.Contrast(ArtworkPalette.Default.Accent, ArtworkPalette.Surface) >= ArtworkPalette.MinimumAccentContrast);
}
