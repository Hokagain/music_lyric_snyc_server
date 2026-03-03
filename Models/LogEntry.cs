using System;

namespace music_lyric_snyc_server.Models;

public sealed class LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Level { get; init; } = "INFO";
    public string Message { get; init; } = string.Empty;

    public string Display => $"[{Timestamp:HH:mm:ss.fff}] [{Level}] {Message}";
}
