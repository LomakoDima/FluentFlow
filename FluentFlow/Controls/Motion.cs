using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FluentFlow.Controls;

// The app's motion language, taken from Windows 11: things arrive fast and settle softly, they leave by speeding up,
// and every move is short. All of it follows the Windows "Animation effects" setting; with that off nothing moves.
//
// Animations here never hold their end value: the element is first set to its final state and the animation only
// covers the way there (FillBehavior.Stop), so later code and bindings are free to change the property again.
internal static class Motion
{
    public const int Quick = 120, Normal = 180, Slow = 240;

    public static readonly IEasingFunction Decelerate = new CubicBezier(0.1, 0.9, 0.2, 1);
    public static readonly IEasingFunction Accelerate = new CubicBezier(0.7, 0, 1, 0.5);
    // Gentler than Decelerate, for things that change size and push their neighbours around.
    public static readonly IEasingFunction Smooth = new CubicBezier(0.25, 0.1, 0.25, 1);

    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    // The state a block was last asked to be in, so asking again for the same state is a no-op and cannot cut a
    // running reveal short (settings changes arrive in bursts).
    private static readonly DependencyProperty RevealedStateProperty =
        DependencyProperty.RegisterAttached("RevealedState", typeof(bool?), typeof(Motion));

    // Fades an element in while it travels from an offset (DIPs) to its place and grows from a scale (1 = no growth),
    // pivoting on its centre. Starts from wherever the element currently is if it was already moving. A delay holds
    // the element at its starting point first, for groups that arrive one after another.
    public static void Enter(UIElement element, double dx = 0, double dy = 0, double scale = 1, int milliseconds = Normal,
        int delay = 0)
    {
        var (scaleTransform, move) = Transforms(element);
        Stop(element, scaleTransform, move);
        if (!Enabled) return;

        // The delay is part of each animation (a flat start of the curve) rather than a BeginTime, so the element
        // is held at its starting point instead of showing its final state until the animation begins.
        var total = milliseconds + delay;
        var duration = TimeSpan.FromMilliseconds(total);
        var easing = delay > 0 ? new Delayed(Decelerate, (double)delay / total) : Decelerate;
        // The fade is a little quicker than the move, so the element is solid before it settles.
        var fadeTotal = milliseconds * 0.75 + delay;
        var fadeEasing = delay > 0 ? new Delayed(Decelerate, delay / fadeTotal) : Decelerate;
        element.BeginAnimation(UIElement.OpacityProperty, From(0, 1, TimeSpan.FromMilliseconds(fadeTotal), fadeEasing));
        if (dx != 0) move.BeginAnimation(TranslateTransform.XProperty, From(dx, 0, duration, easing));
        if (dy != 0) move.BeginAnimation(TranslateTransform.YProperty, From(dy, 0, duration, easing));
        if (scale != 1)
        {
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, From(scale, 1, duration, easing));
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, From(scale, 1, duration, easing));
        }
    }

    // Moves a brush to a new colour from wherever it is now (or at once, with animate: false). The brush must not be
    // frozen.
    public static void ColorTo(SolidColorBrush brush, Color to, bool animate = true, int milliseconds = Slow)
    {
        var from = brush.Color;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        brush.Color = to;
        if (animate && Enabled && from != to)
            brush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = Smooth, FillBehavior = FillBehavior.Stop });
    }

    // The same for one stop of a gradient.
    public static void ColorTo(GradientStop stop, Color to, bool animate = true, int milliseconds = Slow)
    {
        var from = stop.Color;
        stop.BeginAnimation(GradientStop.ColorProperty, null);
        stop.Color = to;
        if (animate && Enabled && from != to)
            stop.BeginAnimation(GradientStop.ColorProperty,
                new ColorAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = Smooth, FillBehavior = FillBehavior.Stop });
    }

    // The way out: fades the element away while it drifts and shrinks, then calls done. The element ends up hidden
    // (opacity 0), so done is where the caller hides or closes whatever it belongs to. Without animations done runs
    // at once.
    public static void Leave(UIElement element, Action done, double dx = 0, double dy = 0, double scale = 1, int milliseconds = Quick)
    {
        if (!Enabled)
        {
            done();
            return;
        }

        var (scaleTransform, move) = Transforms(element);
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var fade = To(element.Opacity, 0, duration, Accelerate);
        fade.Completed += (_, _) => done();
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        if (dx != 0) move.BeginAnimation(TranslateTransform.XProperty, To(move.X, dx, duration, Accelerate));
        if (dy != 0) move.BeginAnimation(TranslateTransform.YProperty, To(move.Y, dy, duration, Accelerate));
        if (scale != 1)
        {
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, To(scaleTransform.ScaleX, scale, duration, Accelerate));
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, To(scaleTransform.ScaleY, scale, duration, Accelerate));
        }
    }

    // Fades an element's opacity to a value from wherever it is now (hover plates and the like).
    public static void FadeTo(UIElement element, double to, int milliseconds = Quick)
    {
        var from = element.Opacity;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = to;
        if (Enabled && from != to) element.BeginAnimation(UIElement.OpacityProperty, From(from, to, TimeSpan.FromMilliseconds(milliseconds), Decelerate));
    }

    // Puts an element back to rest: opaque, in place, full size.
    public static void Reset(UIElement element)
    {
        var (scaleTransform, move) = Transforms(element);
        Stop(element, scaleTransform, move);
    }

    // Shows or hides a block with a height reveal, so what is below it glides instead of jumping. The block's own
    // Visibility is the end state. animate: false just applies it.
    public static void SetRevealed(FrameworkElement block, bool revealed, bool animate, int milliseconds = Normal)
    {
        if (block.GetValue(RevealedStateProperty) is bool last && last == revealed) return;
        block.SetValue(RevealedStateProperty, revealed);

        block.BeginAnimation(FrameworkElement.HeightProperty, null);
        block.BeginAnimation(UIElement.OpacityProperty, null);
        block.Height = double.NaN;
        block.Opacity = 1;
        block.ClipToBounds = false;
        if (!animate || !Enabled || !block.IsLoaded)
        {
            block.Visibility = revealed ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(milliseconds);
        if (revealed)
        {
            if (block.Visibility == Visibility.Visible) return;
            block.Visibility = Visibility.Visible;
            block.UpdateLayout();
            var full = block.ActualHeight;
            block.ClipToBounds = true;
            var grow = From(0, full, duration, Smooth);
            grow.Completed += (_, _) => block.ClipToBounds = false;
            block.BeginAnimation(FrameworkElement.HeightProperty, grow);
            block.BeginAnimation(UIElement.OpacityProperty, From(0, 1, duration, Smooth));
        }
        else
        {
            if (block.Visibility != Visibility.Visible) return;
            var full = block.ActualHeight;
            block.ClipToBounds = true;
            var shrink = new DoubleAnimation(full, 0, duration) { EasingFunction = Smooth, FillBehavior = FillBehavior.HoldEnd };
            shrink.Completed += (_, _) =>
            {
                block.BeginAnimation(FrameworkElement.HeightProperty, null);
                block.BeginAnimation(UIElement.OpacityProperty, null);
                block.Height = double.NaN;
                block.Opacity = 1;
                block.ClipToBounds = false;
                block.Visibility = Visibility.Collapsed;
            };
            block.BeginAnimation(FrameworkElement.HeightProperty, shrink);
            block.BeginAnimation(UIElement.OpacityProperty, To(1, 0, duration, Smooth));
        }
    }

    private static void Stop(UIElement element, ScaleTransform scale, TranslateTransform move)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        move.BeginAnimation(TranslateTransform.XProperty, null);
        move.BeginAnimation(TranslateTransform.YProperty, null);
        element.Opacity = 1;
        scale.ScaleX = scale.ScaleY = 1;
        move.X = move.Y = 0;
    }

    // Every animated element carries one scale and one translation, created the first time it is needed. They pivot on
    // the element's centre unless the element already chose another pivot.
    private static (ScaleTransform Scale, TranslateTransform Move) Transforms(UIElement element)
    {
        if (element.RenderTransform is TransformGroup { Children: [ScaleTransform scale, TranslateTransform move] })
            return (scale, move);

        scale = new ScaleTransform(1, 1);
        move = new TranslateTransform();
        element.RenderTransform = new TransformGroup { Children = { scale, move } };
        if (element.RenderTransformOrigin == default) element.RenderTransformOrigin = new Point(0.5, 0.5);
        return (scale, move);
    }

    private static DoubleAnimation From(double from, double to, TimeSpan duration, IEasingFunction easing)
        => new(from, to, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };

    private static DoubleAnimation To(double from, double to, TimeSpan duration, IEasingFunction easing)
        => new(from, to, duration) { EasingFunction = easing, FillBehavior = FillBehavior.HoldEnd };

    // Stays at 0 for the first part of the time, then runs the inner curve over the rest.
    internal sealed class Delayed(IEasingFunction inner, double start) : IEasingFunction
    {
        public double Ease(double normalizedTime)
            => normalizedTime <= start ? 0 : inner.Ease((normalizedTime - start) / (1 - start));
    }

    // A cubic Bezier easing with the CSS definition (the curves Windows 11 documents): control points (x1, y1) and
    // (x2, y2) between (0, 0) and (1, 1).
    private sealed class CubicBezier(double x1, double y1, double x2, double y2) : IEasingFunction
    {
        public double Ease(double normalizedTime)
        {
            if (normalizedTime <= 0) return 0;
            if (normalizedTime >= 1) return 1;
            // Solve x(t) = time for t by bisection, then read y(t). Twenty steps are far finer than a pixel.
            double low = 0, high = 1, t = normalizedTime;
            for (var i = 0; i < 20; i++)
            {
                t = (low + high) / 2;
                if (Bezier(t, x1, x2) < normalizedTime) low = t;
                else high = t;
            }
            return Bezier(t, y1, y2);
        }

        private static double Bezier(double t, double p1, double p2)
        {
            var u = 1 - t;
            return 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t;
        }
    }
}
