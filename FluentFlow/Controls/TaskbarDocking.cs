using System.Windows;
using FluentFlow.Services;

namespace FluentFlow.Controls;

// Pure geometry for the taskbar widget, in physical pixels: where the widget goes on the taskbar, given the taskbar,
// the notification area, the taskbar's buttons and the user's alignment choice. No windows involved, so every taskbar
// layout (any edge, any DPI, any monitor, either text direction, crowded) can be checked without owning that machine.
//
// The taskbar is treated as a strip of free space between its buttons and the tray, along its long axis: left to
// right on a top or bottom taskbar, top to bottom on a taskbar docked at the left or right edge. The widget takes the
// free gap that matches the chosen alignment, so it never covers an icon whatever the Windows icon alignment is.
// Left means "at the start of the strip" (top on a side taskbar) and Right "at the end" (bottom).
internal static class TaskbarDocking
{
    // Below this thickness (height of a horizontal taskbar, width of a vertical one) the visualizer would be clipped,
    // so such a taskbar is treated as unsupported.
    private const double MinimumThickness = 24; // DIPs

    // A taskbar docked at the left or right edge is taller than it is wide.
    public static bool IsVertical(Rect taskbar) => !taskbar.IsEmpty && taskbar.Height > taskbar.Width;

    // Auto: with centred icons the free space is at the edges, so go to the edge away from the tray; with
    // left-aligned icons the free space is next to the tray. Everything else is taken literally.
    // trayAtEnd: the tray is at the right end of a horizontal taskbar, or the bottom end of a vertical one.
    public static WidgetAlignment Resolve(WidgetAlignment alignment, bool iconsCentered, bool trayAtEnd)
    {
        if (alignment != WidgetAlignment.Auto) return alignment;
        var farSide = trayAtEnd ? WidgetAlignment.Left : WidgetAlignment.Right;
        var traySide = trayAtEnd ? WidgetAlignment.Right : WidgetAlignment.Left;
        return iconsCentered ? farSide : traySide;
    }

    // Returns the widget's rectangle in screen pixels, or null when there is no usable spot (unknown tray, taskbar too
    // thin, no gap wide enough). nominalSize is in the taskbar's own terms: Width is the wished length along the
    // taskbar and Height the thickness across it, whichever way the taskbar runs (so a vertical widget is Size(len, 36),
    // not Size(36, len)). The length is reduced to fit the gap, down to minimumLength.
    public static Rect? Calculate(Rect taskbar, Rect notificationArea, IEnumerable<Rect> taskbarButtons,
        Size nominalSize, double minimumLength, double scale, double gap, double crossPadding,
        WidgetAlignment alignment = WidgetAlignment.Auto, bool iconsCentered = false, double edgeOffset = 0)
    {
        if (taskbar.IsEmpty || taskbar.Width == taskbar.Height) return null; // a square "taskbar" has no direction
        var axis = new Axis(IsVertical(taskbar));
        if (notificationArea.IsEmpty || axis.Length(notificationArea) <= 0
            || axis.CrossEnd(notificationArea) <= axis.CrossStart(taskbar)
            || axis.CrossStart(notificationArea) >= axis.CrossEnd(taskbar)) return null;

        var gapPx = gap * scale;
        var thickness = Math.Min(nominalSize.Height * scale, axis.CrossLength(taskbar) - 2 * crossPadding * scale);
        if (thickness < MinimumThickness * scale) return null;

        var wishedLength = nominalSize.Width * scale;
        var minimumPx = minimumLength * scale;
        var offsetPx = Math.Max(0, edgeOffset) * scale;

        // Everything that is not free: the tray and every button, each with a margin around it.
        var blocked = new List<(double Start, double End)> { (axis.Start(notificationArea) - gapPx, axis.End(notificationArea) + gapPx) };
        foreach (var button in taskbarButtons)
        {
            if (button.IsEmpty || axis.Length(button) <= 0 || button.IntersectsWith(notificationArea)) continue;
            blocked.Add((axis.Start(button) - gapPx, axis.End(button) + gapPx));
        }

        var free = FreeIntervals(axis.Start(taskbar) + gapPx, axis.End(taskbar) - gapPx, blocked);
        var taskbarCenter = axis.Start(taskbar) + axis.Length(taskbar) / 2;
        var trayAtEnd = axis.Start(notificationArea) + axis.Length(notificationArea) / 2 >= taskbarCenter;
        var chosen = Resolve(alignment, iconsCentered, trayAtEnd);

        (double Start, double Length)? spot = chosen switch
        {
            WidgetAlignment.Left => PickLeft(free, wishedLength, minimumPx, offsetPx),
            WidgetAlignment.Center => PickCenter(free, taskbarCenter, wishedLength, minimumPx),
            _ => PickRight(free, wishedLength, minimumPx, offsetPx)
        };
        if (spot is not { } found) return null;

        var crossStart = axis.CrossStart(taskbar) + (axis.CrossLength(taskbar) - thickness) / 2;
        return axis.Make(Math.Round(found.Start), Math.Round(found.Length), Math.Round(crossStart), Math.Round(thickness));
    }

    // Projects rectangles onto the taskbar's long axis ("along") and the axis across it ("cross"), so the same
    // maths serves a horizontal and a vertical taskbar. Along runs left to right or top to bottom.
    private readonly record struct Axis(bool Vertical)
    {
        public double Start(Rect r) => Vertical ? r.Top : r.Left;
        public double End(Rect r) => Vertical ? r.Bottom : r.Right;
        public double Length(Rect r) => Vertical ? r.Height : r.Width;
        public double CrossStart(Rect r) => Vertical ? r.Left : r.Top;
        public double CrossEnd(Rect r) => Vertical ? r.Right : r.Bottom;
        public double CrossLength(Rect r) => Vertical ? r.Width : r.Height;
        public Rect Make(double start, double length, double crossStart, double crossLength)
            => Vertical ? new Rect(crossStart, start, crossLength, length) : new Rect(start, crossStart, length, crossLength);
    }

    // The gaps left in [start, end] once the blocked ranges are removed, in start-to-end order.
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
