using System.Text.Json.Serialization;

namespace music_lyric_snyc_server.Models;

public sealed class LyricSocketPayload
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("position_ms")]
    public long PositionMs { get; init; }

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; init; }

    [JsonPropertyName("lines")]
    public string Lines { get; init; } = string.Empty;
}
