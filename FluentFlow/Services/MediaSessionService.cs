using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentFlow.Models;
using Windows.Media.Control;

namespace FluentFlow.Services;

// All Windows media calls live here. Published snapshots and session changes run on the UI thread.
public sealed class MediaSessionService : INotifyPropertyChanged, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _positionTimer;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private MediaInfo _currentMedia = new();
    private bool _initialized;
    private bool _disposed;
    private bool _commandPending;
    private int _metadataVersion;

    private bool _hasTimeline;
    private TimeSpan _rawPosition;
    private DateTimeOffset _rawUpdatedAt;
    private TimeSpan _timelineStart;
    private TimeSpan _timelineEnd;
    private double _anchorPositionSeconds;
    private long _anchorTimestamp;
    private double _playbackRate = 1;

    public MediaSessionService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _positionTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _positionTimer.Tick += PositionTimer_Tick;
    }

    public MediaInfo CurrentMedia
    {
        get => _currentMedia;
        private set
        {
            _currentMedia = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentMedia)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task InitializeAsync()
    {
        if (_initialized || _disposed) return;
        _initialized = true;

        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed) return;

            _manager = manager;
            _manager.CurrentSessionChanged += Manager_CurrentSessionChanged;
            _manager.SessionsChanged += Manager_SessionsChanged;
            await RefreshCurrentSessionAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Media initialization failed: {exception}");
            if (!_disposed)
            {
                CurrentMedia = new MediaInfo
                {
                    Artist = "Windows media access is unavailable",
                    PlaybackLabel = "UNAVAILABLE",
                    StatusMessage = "Could not connect to Windows media sessions. Restart FluentFlow to retry."
                };
            }
        }
    }

    public Task PreviousAsync() => ExecuteCommandAsync(
        async session => await session.TrySkipPreviousAsync(), CurrentMedia.CanPrevious);

    public Task NextAsync() => ExecuteCommandAsync(
        async session => await session.TrySkipNextAsync(), CurrentMedia.CanNext);

    public Task PlayPauseAsync() => ExecuteCommandAsync(async session =>
    {
        var playback = session.GetPlaybackInfo();
        if (playback.Controls.IsPlayPauseToggleEnabled)
            return await session.TryTogglePlayPauseAsync();

        return playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
            ? await session.TryPauseAsync()
            : await session.TryPlayAsync();
    }, CurrentMedia.CanPlayPause);

    private async Task ExecuteCommandAsync(
        Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command, bool supported)
    {
        var session = _session;
        if (_disposed || session is null || !supported || _commandPending) return;

        _commandPending = true;
        try
        {
            var accepted = await command(session);
            if (_disposed || !ReferenceEquals(session, _session)) return;

            RefreshPlaybackAndTimeline();
            CurrentMedia = CurrentMedia with
            {
                StatusMessage = accepted ? "Connected to Windows media" : "The media app did not accept this command"
            };
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Media command failed: {exception}");
            if (!_disposed && ReferenceEquals(session, _session))
                CurrentMedia = CurrentMedia with { StatusMessage = "The media app is no longer responding" };
        }
        finally
        {
            _commandPending = false;
        }
    }

    private async Task RefreshCurrentSessionAsync()
    {
        if (_disposed || _manager is null) return;

        try
        {
            var session = _manager.GetCurrentSession();
            if (!ReferenceEquals(session, _session))
            {
                DetachSession();
                _session = session;
                _hasTimeline = false;
                CurrentMedia = session is null ? new MediaInfo() : new MediaInfo
                {
                    HasSession = true,
                    Title = "Loading media…",
                    Artist = "",
                    StatusMessage = session.SourceAppUserModelId
                };

                if (session is not null)
                {
                    session.MediaPropertiesChanged += Session_MediaPropertiesChanged;
                    session.PlaybackInfoChanged += Session_PlaybackInfoChanged;
                    session.TimelinePropertiesChanged += Session_TimelinePropertiesChanged;
                }
            }

            if (_session is null) return;
            RefreshPlaybackAndTimeline();
            await RefreshMetadataAsync(_session);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Session refresh failed: {exception}");
            if (!_disposed)
            {
                DetachSession();
                CurrentMedia = new MediaInfo();
            }
        }
    }

    private async Task RefreshMetadataAsync(GlobalSystemMediaTransportControlsSession session)
    {
        var version = ++_metadataVersion;
        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            if (!IsCurrentMetadata(session, version)) return;

            // Clear the old cover immediately when a new track arrives.
            CurrentMedia = CurrentMedia with
            {
                Title = string.IsNullOrWhiteSpace(properties.Title) ? "Untitled media" : properties.Title,
                Artist = string.IsNullOrWhiteSpace(properties.Artist) ? properties.AlbumArtist ?? "" : properties.Artist,
                Artwork = null,
                StatusMessage = session.SourceAppUserModelId
            };

            if (properties.Thumbnail is null) return;
            try
            {
                using var thumbnail = await properties.Thumbnail.OpenReadAsync();
                using var stream = thumbnail.AsStreamForRead();
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 224;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();

                // An older artwork request must never overwrite a newer song or session.
                if (IsCurrentMetadata(session, version))
                    CurrentMedia = CurrentMedia with { Artwork = image };
            }
            catch (Exception exception)
            {
                // Some apps publish missing or invalid thumbnails; the XAML keeps its fallback.
                Debug.WriteLine($"Thumbnail unavailable: {exception.Message}");
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Metadata unavailable: {exception}");
            if (IsCurrentMetadata(session, version))
                CurrentMedia = CurrentMedia with
                {
                    Title = "Media information unavailable", Artist = "", Artwork = null
                };
        }
    }

    private bool IsCurrentMetadata(GlobalSystemMediaTransportControlsSession session, int version)
        => !_disposed && ReferenceEquals(session, _session) && version == _metadataVersion;

    private void RefreshPlaybackAndTimeline()
    {
        if (_disposed || _session is null) return;

        try
        {
            var playback = _session.GetPlaybackInfo();
            var timeline = _session.GetTimelineProperties();
            var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var previousPosition = EstimatePosition();
            var rate = playback.PlaybackRate ?? 1;
            if (!double.IsFinite(rate)) rate = 1;

            // On pause/resume, an unchanged timeline must not count time spent paused.
            var sameTimeline = _hasTimeline && _rawPosition == timeline.Position
                && _rawUpdatedAt == timeline.LastUpdatedTime
                && _timelineStart == timeline.StartTime && _timelineEnd == timeline.EndTime;
            _anchorPositionSeconds = sameTimeline
                ? previousPosition
                : Math.Max(0, (timeline.Position - timeline.StartTime).TotalSeconds);

            if (!sameTimeline && playing && timeline.LastUpdatedTime > DateTimeOffset.UnixEpoch)
                _anchorPositionSeconds += Math.Max(0, (DateTimeOffset.UtcNow - timeline.LastUpdatedTime).TotalSeconds) * rate;

            _rawPosition = timeline.Position;
            _rawUpdatedAt = timeline.LastUpdatedTime;
            _timelineStart = timeline.StartTime;
            _timelineEnd = timeline.EndTime;
            _anchorTimestamp = Stopwatch.GetTimestamp();
            _playbackRate = rate;
            _hasTimeline = true;

            var controls = playback.Controls;
            CurrentMedia = CurrentMedia with
            {
                IsPlaying = playing,
                CanPrevious = controls.IsPreviousEnabled,
                CanNext = controls.IsNextEnabled,
                CanPlayPause = controls.IsPlayPauseToggleEnabled || (playing ? controls.IsPauseEnabled : controls.IsPlayEnabled),
                DurationSeconds = Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds),
                PlaybackLabel = playback.PlaybackStatus switch
                {
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => "NOW PLAYING",
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => "PAUSED",
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => "STOPPED",
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => "LOADING",
                    _ => "MEDIA"
                }
            };
            CurrentMedia = CurrentMedia with { PositionSeconds = EstimatePosition() };
            if (playing) _positionTimer.Start();
            else _positionTimer.Stop();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Playback information unavailable: {exception}");
            _positionTimer.Stop();
            CurrentMedia = CurrentMedia with
            {
                IsPlaying = false, CanPrevious = false, CanPlayPause = false, CanNext = false,
                PlaybackLabel = "UNAVAILABLE", StatusMessage = "Playback information is unavailable"
            };
        }
    }

    private double EstimatePosition()
    {
        if (!_hasTimeline) return 0;
        var seconds = _anchorPositionSeconds;
        if (CurrentMedia.IsPlaying)
            seconds += Stopwatch.GetElapsedTime(_anchorTimestamp).TotalSeconds * _playbackRate;
        return CurrentMedia.DurationSeconds > 0
            ? Math.Clamp(seconds, 0, CurrentMedia.DurationSeconds)
            : Math.Max(0, seconds);
    }

    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        if (!_disposed && _session is not null)
            CurrentMedia = CurrentMedia with { PositionSeconds = EstimatePosition() };
    }

    // Windows can raise these events on a background thread. Never update WPF bindings there.
    private void QueueUpdate(Action update)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.InvokeAsync(() => { if (!_disposed) update(); });
    }

    private void Manager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        => QueueUpdate(() => { _ = RefreshCurrentSessionAsync(); });

    private void Manager_SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        => QueueUpdate(() => { _ = RefreshCurrentSessionAsync(); });

    private void Session_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        => QueueUpdate(() =>
        {
            if (!ReferenceEquals(sender, _session)) return;
            RefreshPlaybackAndTimeline();
            _ = RefreshMetadataAsync(sender);
        });

    private void Session_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        => QueueUpdate(() => { if (ReferenceEquals(sender, _session)) RefreshPlaybackAndTimeline(); });

    private void Session_TimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        => QueueUpdate(() => { if (ReferenceEquals(sender, _session)) RefreshPlaybackAndTimeline(); });

    private void DetachSession()
    {
        ++_metadataVersion;
        _positionTimer.Stop();
        if (_session is null) return;
        _session.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
        _session.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
        _session.TimelinePropertiesChanged -= Session_TimelinePropertiesChanged;
        _session = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DetachSession();
        _positionTimer.Tick -= PositionTimer_Tick;
        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= Manager_CurrentSessionChanged;
            _manager.SessionsChanged -= Manager_SessionsChanged;
            _manager = null;
        }
    }
}
