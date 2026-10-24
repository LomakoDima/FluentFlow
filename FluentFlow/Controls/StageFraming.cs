namespace FluentFlow.Controls;

// How the settings stage frames the model taskbar along its length: pure geometry, so it is easy to test.
internal static class StageFraming
{
    // Share of the stage within which an end of the taskbar is pulled fully into view.
    public const double EndSnap = 0.12;

    // The offset that centres the subject (wanted), held so the model never leaves a gap at either end of the stage.
    // A model shorter than the stage is centred. When an end of the taskbar is nearly in view anyway it is brought
    // fully in, rather than leaving a sliver of the tray cut off.
    public static double Follow(double stage, double span, double wanted)
    {
        if (span <= stage) return (stage - span) / 2;
        var offset = Math.Clamp(wanted, stage - span, 0);
        var snap = stage * EndSnap;
        if (-offset < snap) return 0;
        if (offset - (stage - span) < snap) return stage - span;
        return offset;
    }
}
