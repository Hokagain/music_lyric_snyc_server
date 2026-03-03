using System;

namespace music_lyric_snyc_server.Models;

public sealed class PlaybackSnapshot
{
    public string SourceAppUserModelId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public byte[]? ThumbnailBytes { get; init; }
    public bool IsPlaying { get; init; }
    public TimeSpan Position { get; init; }
    public TimeSpan Duration { get; init; }

    public string TrackKey => $"{Title}|{Artist}";
}
