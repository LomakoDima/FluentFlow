using System.Windows;
using FluentFlow.Services;

namespace FluentFlow.Controls;

// Pure geometry for the taskbar widget, in physical pixels: where the widget goes on the taskbar, given the taskbar,
// the notification area, the taskbar's buttons and the user's alignment choice. No windows involved, so every taskbar
// layout (top/bottom, any DPI, any monitor, either text direction, crowded) can be checked without owning that machine.
//
// The taskbar is treated as a strip of free space between its buttons and the tray. The widget takes the free gap
// that matches the chosen alignment, so it never covers an icon whatever the Windows icon alignment is.
internal static class TaskbarDocking
{
    // Below this height the visualizer would be clipped, so such a taskbar is treated as unsupported.
    private const double MinimumHeight = 24; // DIPs

    // Auto: with centred icons the free space is at the edges, so go to the edge away from the tray; with
    // left-aligned icons the free space is next to the tray. Everything else is taken literally.
    public static WidgetAlignment Resolve(WidgetAlignment alignment, bool iconsCentered, bool trayOnRight)
    {
        if (alignment != WidgetAlignment.Auto) return alignment;
        var farSide = trayOnRight ? WidgetAlignment.Left : WidgetAlignment.Right;
        var traySide = trayOnRight ? WidgetAlignment.Right : WidgetAlignment.Left;
        return iconsCentered ? farSide : traySide;
    }

    // Returns the widget's rectangle in screen pixels, or null when there is no usable spot (vertical taskbar,
    // unknown tray, no gap wide enough). Width is the wished width; it is reduced to fit the gap, down to minimumWidth.
    public static Rect? Calculate(Rect taskbar, Rect notificationArea, IEnumerable<Rect> taskbarButtons,
        Size nominalSize, double minimumWidth, double scale, double gap, double verticalPadding,
        WidgetAlignment alignment = WidgetAlignment.Auto, bool iconsCentered = false, double edgeOffset = 0)
    {
        if (taskbar.IsEmpty || taskbar.Width <= taskbar.Height) return null; // vertical taskbars are not supported
        if (notificationArea.IsEmpty || notificationArea.Width <= 0
            || notificationArea.Bottom <= taskbar.Top || notificationArea.Top >= taskbar.Bottom) return null;

        var gapPx = gap * scale;
        var height = Math.Min(nominalSize.Height * scale, taskbar.Height - 2 * verticalPadding * scale);
        if (height < MinimumHeight * scale) return null;

        var wishedWidth = nominalSize.Width * scale;
        var minimumPx = minimumWidth * scale;
        var offsetPx = Math.Max(0, edgeOffset) * scale;

        // Everything that is not free: the tray and every button, each with a margin around it.
        var blocked = new List<(double Start, double End)> { (notificationArea.Left - gapPx, notificationArea.Right + gapPx) };
        foreach (var button in taskbarButtons)
        {
            if (button.IsEmpty || button.Width <= 0 || button.IntersectsWith(notificationArea)) continue;
            blocked.Add((button.Left - gapPx, button.Right + gapPx));
        }

        var free = FreeIntervals(taskbar.Left + gapPx, taskbar.Right - gapPx, blocked);
        var trayOnRight = notificationArea.Left + notificationArea.Width / 2 >= taskbar.Left + taskbar.Width / 2;
        var chosen = Resolve(alignment, iconsCentered, trayOnRight);

        (double Left, double Width)? spot = chosen switch
        {
            WidgetAlignment.Left => PickLeft(free, wishedWidth, minimumPx, offsetPx),
            WidgetAlignment.Center => PickCenter(free, taskbar.Left + taskbar.Width / 2, wishedWidth, minimumPx),
            _ => PickRight(free, wishedWidth, minimumPx, offsetPx)
        };
        if (spot is not { } found) return null;

        var top = taskbar.Top + (taskbar.Height - height) / 2;
        return new Rect(Math.Round(found.Left), Math.Round(top), Math.Round(found.Width), Math.Round(height));
    }

    // The gaps left in [start, end] once the blocked ranges are removed, in left-to-right order.
    private static List<(double Start, double End)> FreeIntervals(double start, double end, List<(double Start, double End)> blocked)
    {
        var free = new List<(double, double)>();
        var cursor = start;
        foreach (var (blockStart, blockEnd) in blocked.OrderBy(b => b.Start))
        {
            if (blockStart > cursor) free.Add((cursor, Math.Min(blockStart, end)));
            cursor = Math.Max(cursor, blockEnd);
            if (cursor >= end) break;
        }
        if (cursor < end) free.Add((cursor, end));
        return free.Where(f => f.Item2 > f.Item1).ToList();
    }

    private static (double, double)? PickLeft(List<(double Start, double End)> free, double wished, double minimum, double offset)
    {
        foreach (var (start, end) in free)
        {
            var width = Math.Min(wished, end - start - offset);
            if (width >= minimum) return (start + offset, width);
        }
        return null;
    }

    private static (double, double)? PickRight(List<(double Start, double End)> free, double wished, double minimum, double offset)
    {
        for (var i = free.Count - 1; i >= 0; i--)
        {
            var (start, end) = free[i];
            var width = Math.Min(wished, end - start - offset);
            if (width >= minimum) return (end - offset - width, width);
        }
        return null;
    }

    // Centred on the taskbar when that spot is free, otherwise in the nearest gap that is wide enough.
    private static (double, double)? PickCenter(List<(double Start, double End)> free, double center, double wished, double minimum)
    {
        (double Start, double End)? best = null;
        var bestDistance = double.MaxValue;
        foreach (var interval in free)
        {
            if (interval.End - interval.Start < minimum) continue;
            var distance = center < interval.Start ? interval.Start - center : center > interval.End ? center - interval.End : 0;
            if (distance < bestDistance) { best = interval; bestDistance = distance; }
        }
        if (best is not { } gapFound) return null;
        var width = Math.Min(wished, gapFound.End - gapFound.Start);
        var left = Math.Clamp(center - width / 2, gapFound.Start, gapFound.End - width);
        return (left, width);
    }
}
