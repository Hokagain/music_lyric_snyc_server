using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace music_lyric_snyc_server.Services;

public sealed class QqMusicApiClient
{
    private static readonly HttpClient HttpClient = new();

    public async Task<long?> SearchFirstSongIdAsync(string songName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(songName))
        {
            return null;
        }

        var escaped = Uri.EscapeDataString(songName.Trim());
        var url = $"https://api.vkeys.cn/v2/music/tencent/search/song?word={escaped}";
        using var response = await HttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!doc.RootElement.TryGetProperty("code", out var codeElement) || codeElement.GetInt32() != 200)
        {
            return null;
        }

        if (!doc.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var firstSong = dataElement.EnumerateArray().FirstOrDefault();
        if (firstSong.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        if (!firstSong.TryGetProperty("id", out var idElement))
        {
            return null;
        }

        return idElement.ValueKind switch
        {
            JsonValueKind.Number => idElement.GetInt64(),
            JsonValueKind.String when long.TryParse(idElement.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    public async Task<string?> GetLyricBySongIdAsync(long songId, CancellationToken cancellationToken)
    {
        var url = $"https://api.vkeys.cn/v2/music/tencent/lyric?id={songId}";
        using var response = await HttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!doc.RootElement.TryGetProperty("code", out var codeElement) || codeElement.GetInt32() != 200)
        {
            return null;
        }

        if (!doc.RootElement.TryGetProperty("data", out var dataElement))
        {
            return null;
        }

        if (dataElement.ValueKind == JsonValueKind.String)
        {
            return dataElement.GetString();
        }

        if (dataElement.ValueKind == JsonValueKind.Object && dataElement.TryGetProperty("lrc", out var lrcElement))
        {
            return lrcElement.GetString();
        }

        return null;
    }
}
