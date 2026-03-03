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

    public async Task<long?> SearchSongIdBySongAndSingerAsync(string songName, string singerName, CancellationToken cancellationToken)
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

        var candidates = dataElement
            .EnumerateArray()
            .Select(ParseCandidate)
            .Where(x => x.Id.HasValue && !string.IsNullOrWhiteSpace(x.Song))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var normalizedTitle = Normalize(songName);
        var normalizedSinger = Normalize(singerName);

        var titleMatched = candidates
            .Where(x => Normalize(x.Song) == normalizedTitle)
            .ToList();

        var singerMatched = titleMatched
            .FirstOrDefault(x => IsSingerMatch(normalizedSinger, Normalize(x.Singer)));

        if (singerMatched.Id.HasValue)
        {
            return singerMatched.Id.Value;
        }

        var firstTitleMatched = titleMatched.FirstOrDefault();
        if (firstTitleMatched.Id.HasValue)
        {
            return firstTitleMatched.Id.Value;
        }

        return candidates[0].Id;
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

    private static (long? Id, string Song, string Singer) ParseCandidate(JsonElement element)
    {
        long? id = null;
        if (element.TryGetProperty("id", out var idElement))
        {
            id = idElement.ValueKind switch
            {
                JsonValueKind.Number => idElement.GetInt64(),
                JsonValueKind.String when long.TryParse(idElement.GetString(), out var parsed) => parsed,
                _ => null
            };
        }

        var song = element.TryGetProperty("song", out var songElement) ? songElement.GetString() ?? string.Empty : string.Empty;
        var singer = element.TryGetProperty("singer", out var singerElement) ? singerElement.GetString() ?? string.Empty : string.Empty;
        return (id, song, singer);
    }

    private static bool IsSingerMatch(string expectedSinger, string candidateSinger)
    {
        if (string.IsNullOrWhiteSpace(expectedSinger))
        {
            return false;
        }

        if (candidateSinger == expectedSinger)
        {
            return true;
        }

        return candidateSinger.Contains(expectedSinger, StringComparison.Ordinal) ||
               expectedSinger.Contains(candidateSinger, StringComparison.Ordinal);
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value
            .Where(c => !char.IsWhiteSpace(c) && c != '/' && c != '、' && c != '&' && c != ',' && c != '，')
            .ToArray();
        return new string(chars).Trim().ToLowerInvariant();
    }
}
