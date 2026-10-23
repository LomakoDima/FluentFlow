using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluentFlow.Controls;

// The player's colours, taken from the cover: an accent for the play button, the status line and the progress bars,
// and two deep glow colours for the light behind the cover. Every colour is pulled into a range that works on the
// player's near-black surface, so a white, black or neon cover can never make text or controls hard to read.
internal readonly record struct ArtworkPalette(Color Accent, Color Glow, Color GlowSecondary)
{
    // The player surface the colours are measured against.
    public static readonly Color Surface = Color.FromRgb(0x13, 0x13, 0x17);

    // The accent carries small text and the play glyph sits on it, so it stays at least this far from the surface
    // (WCAG contrast ratio; 4.5 is the bar for body text).
    public const double MinimumAccentContrast = 6;

    // The glows lie under white text, so they stay below these luminances, whatever their hue.
    public const double MaximumGlowLuminance = 0.09, MaximumSecondaryGlowLuminance = 0.035;

    // FluentFlow's own warm terracotta, matching the fallback cover.
    public static readonly ArtworkPalette Default = new(
        Color.FromRgb(0xF0, 0xA8, 0x8E), Color.FromRgb(0x7A, 0x34, 0x26), Color.FromRgb(0x3C, 0x1D, 0x26));

    private const int SampleSide = 24;
    private const int HueBuckets = 12; // 30 degrees each

    public static ArtworkPalette From(ImageSource? image)
    {
        if (image is not BitmapSource { PixelWidth: > 0, PixelHeight: > 0 } bitmap) return Default;
        try
        {
            // 24 x 24 is plenty to find the colours of a cover and costs well under a millisecond.
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var scaled = new TransformedBitmap(converted,
                new ScaleTransform((double)SampleSide / bitmap.PixelWidth, (double)SampleSide / bitmap.PixelHeight));
            var stride = scaled.PixelWidth * 4;
            var pixels = new byte[stride * scaled.PixelHeight];
            scaled.CopyPixels(pixels, stride, 0);
            return FromPixels(pixels);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Artwork palette unavailable: {exception.Message}");
            return Default;
        }
    }

    // pixels: BGRA, 4 bytes each. Colourful pixels vote for their hue, weighted towards saturated and bright ones; the
    // heaviest hue becomes the accent and the main glow, the heaviest one at least 60 degrees away the second glow.
    // A grey cover gets a quiet, nearly neutral palette instead of an invented colour.
    public static ArtworkPalette FromPixels(ReadOnlySpan<byte> pixels)
    {
        Span<double> weight = stackalloc double[HueBuckets];
        Span<double> red = stackalloc double[HueBuckets];
        Span<double> green = stackalloc double[HueBuckets];
        Span<double> blue = stackalloc double[HueBuckets];
        double sumR = 0, sumG = 0, sumB = 0, count = 0;

        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (pixels[i + 3] < 128) continue;
            double b = pixels[i] / 255.0, g = pixels[i + 1] / 255.0, r = pixels[i + 2] / 255.0;
            sumR += r; sumG += g; sumB += b; count++;

            var (hue, saturation, value) = ToHsv(r, g, b);
            if (saturation < 0.18 || value < 0.18) continue; // greys and near-blacks don't vote
            var vote = saturation * saturation * Math.Min(1, value * 1.3);
            var bucket = (int)(hue / (360.0 / HueBuckets)) % HueBuckets;
            weight[bucket] += vote;
            red[bucket] += r * vote; green[bucket] += g * vote; blue[bucket] += b * vote;
        }
        if (count == 0) return Default;

        var average = (sumR / count, sumG / count, sumB / count);
        var primary = Heaviest(weight, exclude: -1);
        var total = 0.0;
        foreach (var w in weight) total += w;

        // Less than a few percent of colour: treat the cover as monochrome.
        if (primary < 0 || total < 0.04 * count)
        {
            var (h, _, _) = ToHsl(average.Item1, average.Item2, average.Item3);
            return new ArtworkPalette(
                Lift(FromHsl(h, 0.10, 0.80)),
                Dim(FromHsl(h, 0.08, 0.24), MaximumGlowLuminance),
                Dim(FromHsl(h, 0.06, 0.14), MaximumSecondaryGlowLuminance));
        }

        var main = Mean(primary, weight, red, green, blue);
        var secondary = Heaviest(weight, exclude: primary);
        var second = secondary >= 0 && weight[secondary] >= 0.15 * weight[primary]
            ? Mean(secondary, weight, red, green, blue)
            : average;

        var (mainHue, mainSaturation, _) = ToHsl(main.Item1, main.Item2, main.Item3);
        var (secondHue, secondSaturation, _) = ToHsl(second.Item1, second.Item2, second.Item3);
        return new ArtworkPalette(
            Lift(FromHsl(mainHue, Math.Clamp(mainSaturation, 0.35, 0.85), 0.72)),
            Dim(FromHsl(mainHue, Math.Clamp(mainSaturation, 0.30, 0.70), 0.30), MaximumGlowLuminance),
            Dim(FromHsl(secondHue, Math.Clamp(secondSaturation, 0.15, 0.55), 0.18), MaximumSecondaryGlowLuminance));
    }

    // WCAG contrast ratio between two colours (1 to 21).
    public static double Contrast(Color a, Color b)
    {
        var (light, dark) = (Luminance(a), Luminance(b));
        if (light < dark) (light, dark) = (dark, light);
        return (light + 0.05) / (dark + 0.05);
    }

    public static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    // Brightens a colour, keeping its hue, until it stands far enough off the surface. Blues need the most help.
    private static Color Lift(Color color)
    {
        var (hue, saturation, lightness) = ToHsl(color.R / 255.0, color.G / 255.0, color.B / 255.0);
        while (Contrast(color, Surface) < MinimumAccentContrast && lightness < 0.97)
        {
            lightness += 0.02;
            color = FromHsl(hue, saturation, lightness);
        }
        return color;
    }

    // Darkens a colour, keeping its hue, until it is no brighter than the limit. Greens and yellows need the most.
    private static Color Dim(Color color, double maximumLuminance)
    {
        var (hue, saturation, lightness) = ToHsl(color.R / 255.0, color.G / 255.0, color.B / 255.0);
        while (Luminance(color) > maximumLuminance && lightness > 0.02)
        {
            lightness -= 0.01;
            color = FromHsl(hue, saturation, lightness);
        }
        return color;
    }

    private static int Heaviest(ReadOnlySpan<double> weight, int exclude)
    {
        var best = -1;
        for (var i = 0; i < weight.Length; i++)
        {
            if (weight[i] <= 0) continue;
            // At least two buckets (60 degrees) around the circle from the excluded hue.
            if (exclude >= 0)
            {
                var distance = Math.Abs(i - exclude);
                if (Math.Min(distance, HueBuckets - distance) < 2) continue;
            }
            if (best < 0 || weight[i] > weight[best]) best = i;
        }
        return best;
    }

    private static (double, double, double) Mean(int bucket, ReadOnlySpan<double> weight,
        ReadOnlySpan<double> red, ReadOnlySpan<double> green, ReadOnlySpan<double> blue)
        => (red[bucket] / weight[bucket], green[bucket] / weight[bucket], blue[bucket] / weight[bucket]);

    private static (double Hue, double Saturation, double Value) ToHsv(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        return (Hue(r, g, b, max, delta), max == 0 ? 0 : delta / max, max);
    }

    private static (double Hue, double Saturation, double Lightness) ToHsl(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var lightness = (max + min) / 2;
        var saturation = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * lightness - 1));
        return (Hue(r, g, b, max, delta), Math.Clamp(saturation, 0, 1), lightness);
    }

    private static double Hue(double r, double g, double b, double max, double delta)
    {
        if (delta == 0) return 0;
        double hue;
        if (max == r) hue = 60 * ((g - b) / delta % 6);
        else if (max == g) hue = 60 * ((b - r) / delta + 2);
        else hue = 60 * ((r - g) / delta + 4);
        return hue < 0 ? hue + 360 : hue;
    }

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        lightness = Math.Clamp(lightness, 0, 1);
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * Math.Clamp(saturation, 0, 1);
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = lightness - chroma / 2;
        var (r, g, b) = (int)(hue / 60) switch
        {
            0 => (chroma, x, 0.0),
            1 => (x, chroma, 0.0),
            2 => (0.0, chroma, x),
            3 => (0.0, x, chroma),
            4 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x)
        };
        static byte Byte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return Color.FromRgb(Byte(r + m), Byte(g + m), Byte(b + m));
    }
}
