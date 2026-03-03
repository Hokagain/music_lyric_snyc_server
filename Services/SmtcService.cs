using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using music_lyric_snyc_server.Models;
using Windows.Media.Control;
using Windows.Media;
using Windows.Storage.Streams;

namespace music_lyric_snyc_server.Services;

public sealed class SmtcService : IAsyncDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _qqSession;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public event EventHandler<PlaybackSnapshot>? PlaybackChanged;

    public async Task InitializeAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.SessionsChanged += OnSessionsChanged;
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;

        await BindQqSessionAsync();
        await PublishSnapshotAsync();
    }

    public async Task<PlaybackSnapshot?> GetCurrentSnapshotAsync()
    {
        if (_qqSession is null)
        {
            return null;
        }

        return await ReadSnapshotAsync(_qqSession);
    }

    private async void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        await BindQqSessionAsync();
        await PublishSnapshotAsync();
    }

    private async void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        await BindQqSessionAsync();
        await PublishSnapshotAsync();
    }

    private async Task BindQqSessionAsync()
    {
        if (_manager is null)
        {
            return;
        }

        var newSession = _manager
            .GetSessions()
            .FirstOrDefault(x => x.SourceAppUserModelId.Contains("qqmusic", StringComparison.OrdinalIgnoreCase));

        if (ReferenceEquals(_qqSession, newSession))
        {
            return;
        }

        if (_qqSession is not null)
        {
            _qqSession.MediaPropertiesChanged -= OnSessionMediaPropertiesChanged;
            _qqSession.PlaybackInfoChanged -= OnSessionPlaybackInfoChanged;
            _qqSession.TimelinePropertiesChanged -= OnSessionTimelineChanged;
        }

        _qqSession = newSession;

        if (_qqSession is not null)
        {
            _qqSession.MediaPropertiesChanged += OnSessionMediaPropertiesChanged;
            _qqSession.PlaybackInfoChanged += OnSessionPlaybackInfoChanged;
            _qqSession.TimelinePropertiesChanged += OnSessionTimelineChanged;
        }
    }

    private async void OnSessionMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        await PublishSnapshotAsync();
    }

    private async void OnSessionPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        await PublishSnapshotAsync();
    }

    private async void OnSessionTimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        await PublishSnapshotAsync();
    }

    private async Task PublishSnapshotAsync()
    {
        if (_qqSession is null)
        {
            PlaybackChanged?.Invoke(this, new PlaybackSnapshot());
            return;
        }

        await _refreshGate.WaitAsync();
        try
        {
            var snapshot = await ReadSnapshotAsync(_qqSession);
            if (snapshot is not null)
            {
                PlaybackChanged?.Invoke(this, snapshot);
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private static async Task<PlaybackSnapshot?> ReadSnapshotAsync(GlobalSystemMediaTransportControlsSession session)
    {
        var media = await session.TryGetMediaPropertiesAsync();
        var playbackInfo = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();

        var thumbBytes = await ReadThumbnailBytesAsync(media.Thumbnail);
        var duration = timeline.EndTime > TimeSpan.Zero ? timeline.EndTime : timeline.MaxSeekTime;

        return new PlaybackSnapshot
        {
            SourceAppUserModelId = session.SourceAppUserModelId,
            Title = media.Title ?? string.Empty,
            Artist = media.Artist ?? string.Empty,
            ThumbnailBytes = thumbBytes,
            IsPlaying = playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            Position = timeline.Position,
            Duration = duration
        };
    }

    private static async Task<byte[]?> ReadThumbnailBytesAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null)
        {
            return null;
        }

        using var stream = await thumbnail.OpenReadAsync();
        using var managedStream = stream.AsStreamForRead();
        using var memory = new MemoryStream();
        await managedStream.CopyToAsync(memory);
        return memory.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        if (_manager is not null)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }

        if (_qqSession is not null)
        {
            _qqSession.MediaPropertiesChanged -= OnSessionMediaPropertiesChanged;
            _qqSession.PlaybackInfoChanged -= OnSessionPlaybackInfoChanged;
            _qqSession.TimelinePropertiesChanged -= OnSessionTimelineChanged;
        }

        _refreshGate.Dispose();
        await Task.CompletedTask;
    }
}
