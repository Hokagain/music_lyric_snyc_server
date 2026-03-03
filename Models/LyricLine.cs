using System;

namespace music_lyric_snyc_server.Models;

public sealed class LyricLine
{
    public TimeSpan Time { get; init; }
    public string Text { get; init; } = string.Empty;
}
