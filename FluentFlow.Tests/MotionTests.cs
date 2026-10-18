using FluentFlow.Controls;

namespace FluentFlow.Tests;

// The easing curves are cubic Beziers with the CSS definition; these checks keep them honest: they start at 0, end
// at 1, never go backwards, and have the character their names promise.
public class MotionTests
{
    public static TheoryData<string> Curves => new() { "Decelerate", "Accelerate", "Smooth" };

    private static System.Windows.Media.Animation.IEasingFunction Curve(string name) => name switch
    {
        "Decelerate" => Motion.Decelerate,
        "Accelerate" => Motion.Accelerate,
        _ => Motion.Smooth
    };

    [Theory]
    [MemberData(nameof(Curves))]
    public void Curves_start_at_zero_and_end_at_one(string name)
    {
        Assert.Equal(0, Curve(name).Ease(0));
        Assert.Equal(1, Curve(name).Ease(1));
    }

    [Theory]
    [MemberData(nameof(Curves))]
    public void Curves_never_go_backwards_or_leave_the_range(string name)
    {
        var curve = Curve(name);
        var previous = 0.0;
        for (var i = 1; i <= 100; i++)
        {
            var value = curve.Ease(i / 100.0);
            Assert.InRange(value, previous - 1e-9, 1 + 1e-9);
            previous = value;
        }
    }

    [Fact]
    public void Decelerate_arrives_early_and_accelerate_leaves_late()
    {
        Assert.True(Motion.Decelerate.Ease(0.25) > 0.25);
        Assert.True(Motion.Accelerate.Ease(0.5) < 0.5);
    }

    [Fact]
    public void Decelerate_matches_the_documented_curve_at_the_midpoint()
    {
        // cubic-bezier(0.1, 0.9, 0.2, 1): solve x(t) = 0.5 by hand and read y(t) at the same t.
        double t = 0.5, low = 0, high = 1;
        for (var i = 0; i < 60; i++)
        {
            t = (low + high) / 2;
            var x = 3 * (1 - t) * (1 - t) * t * 0.1 + 3 * (1 - t) * t * t * 0.2 + t * t * t;
            if (x < 0.5) low = t; else high = t;
        }
        var y = 3 * (1 - t) * (1 - t) * t * 0.9 + 3 * (1 - t) * t * t * 1 + t * t * t;
        Assert.Equal(y, Motion.Decelerate.Ease(0.5), 4);
    }
}
