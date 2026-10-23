using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FluentFlow.Controls;

// The player's progress line, drawn in the language of the taskbar visualizer: a row of slim bars, mirrored around the
// middle. Played bars take the accent; the bar being played fills up as the song moves and a soft swell travels with
// it, so the current position reads at a glance without a thumb that suggests dragging.
//
// It is a ProgressBar without a template (so it keeps ProgressBar's UI Automation: a read-only range) that draws
// itself in one pass. Value can be animated like any ProgressBar's.
public sealed class PlaybackTicks : ProgressBar
{
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(PlaybackTicks),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    // Without a known length (no media, a live stream) there is no position to point at, so the swell is left out.
    public static readonly DependencyProperty ShowsPositionProperty = DependencyProperty.Register(
        nameof(ShowsPosition), typeof(bool), typeof(PlaybackTicks),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double TickWidth = 3;
    private const double TickGap = 3;
    private const double RestHeight = 6;
    private const double SwellReach = 2.6; // bars on each side that the swell lifts

    static PlaybackTicks()
    {
        // Its own style key: no theme template, so OnRender is the whole look.
        DefaultStyleKeyProperty.OverrideMetadata(typeof(PlaybackTicks), new FrameworkPropertyMetadata(typeof(PlaybackTicks)));
        ForegroundProperty.OverrideMetadata(typeof(PlaybackTicks),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public bool ShowsPosition
    {
        get => (bool)GetValue(ShowsPositionProperty);
        set => SetValue(ShowsPositionProperty, value);
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        InvalidateVisual();
    }

    protected override void OnMaximumChanged(double oldMaximum, double newMaximum)
    {
        base.OnMaximumChanged(oldMaximum, newMaximum);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size constraint) => new(0, Math.Min(constraint.Height, 14));

    protected override Size ArrangeOverride(Size arrangeBounds) => arrangeBounds;

    protected override void OnRender(DrawingContext drawing)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < TickWidth || height <= 0) return;

        var count = TickCount(width);
        var step = count > 1 ? (width - TickWidth) / (count - 1) : 0;
        var range = Maximum - Minimum;
        var fraction = range > 0 ? Math.Clamp((Value - Minimum) / range, 0, 1) : 0;
        var position = fraction * count; // in bars: 2.5 means two bars played and the third half way
        var pixel = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var peak = height;

        for (var i = 0; i < count; i++)
        {
            var tall = ShowsPosition ? RestHeight + (peak - RestHeight) * Swell(i + 0.5 - position) : RestHeight;
            // Whole device pixels, so the bars stay crisp at every display scale.
            var x = Math.Round(i * step * pixel) / pixel;
            var y = Math.Round((height - tall) / 2 * pixel) / pixel;
            var bar = new Rect(x, y, TickWidth, Math.Round(tall * pixel) / pixel);
            var radius = TickWidth / 2;

            var played = Math.Clamp(position - i, 0, 1);
            if (played < 1) drawing.DrawRoundedRectangle(TrackBrush, null, bar, radius, radius);
            if (played <= 0) continue;
            drawing.PushOpacity(played);
            drawing.DrawRoundedRectangle(Foreground, null, bar, radius, radius);
            drawing.Pop();
        }
    }

    internal static int TickCount(double width) => Math.Max(1, (int)((width + TickGap) / (TickWidth + TickGap)));

    // 1 at the playing position, easing down to 0 a couple of bars away on either side.
    internal static double Swell(double distance)
    {
        var t = Math.Clamp(1 - Math.Abs(distance) / SwellReach, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
