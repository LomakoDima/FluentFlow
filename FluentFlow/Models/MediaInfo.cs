using System.Windows.Media;

namespace FluentFlow.Models;

// One snapshot of the current session. "with" creates an updated copy for WPF bindings.
public sealed record MediaInfo
{
    public string Title { get; init; } = "No media playing";
    public string Artist { get; init; } = "Open a media app to get started";
    public ImageSource? Artwork { get; init; }
    public bool HasSession { get; init; }
    public bool IsPlaying { get; init; }
    public bool CanPrevious { get; init; }
    public bool CanPlayPause { get; init; }
    public bool CanNext { get; init; }
    public double PositionSeconds { get; init; }
    public double DurationSeconds { get; init; }
    public string PlaybackLabel { get; init; } = "NO MEDIA";
    public string StatusMessage { get; init; } = "Waiting for a Windows media session";

    public string PlayPauseDescription => IsPlaying ? "Pause" : "Play";
    public string CurrentTime => HasSession ? FormatTime(PositionSeconds) : "0:00";
    public string TotalTime => DurationSeconds > 0 ? FormatTime(DurationSeconds) : "--:--";

    // A zero-length or live timeline must not make the progress bar divide by zero.
    public double ProgressMaximum => DurationSeconds > 0 ? DurationSeconds : 1;
    public double ProgressValue => DurationSeconds > 0 ? PositionSeconds : 0;

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss")
            : time.ToString(@"m\:ss");
    }
}
