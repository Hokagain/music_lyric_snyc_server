using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using music_lyric_snyc_server.Models;

namespace music_lyric_snyc_server.Services;

public sealed class LyricParser
{
    private static readonly Regex TimeTagRegex = new(@"\[(\d{1,2}):(\d{1,2})(?:\.(\d{1,3}))?\]", RegexOptions.Compiled);

    public IReadOnlyList<LyricLine> Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc))
        {
            return Array.Empty<LyricLine>();
        }

        var lines = new List<LyricLine>();
        foreach (var rawLine in lrc.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var matches = TimeTagRegex.Matches(rawLine);
            if (matches.Count == 0)
            {
                continue;
            }

            var lyricText = TimeTagRegex.Replace(rawLine, string.Empty).Trim();
            foreach (Match match in matches)
            {
                var minute = int.Parse(match.Groups[1].Value);
                var second = int.Parse(match.Groups[2].Value);
                var millisecond = ParseMillisecond(match.Groups[3].Value);
                lines.Add(new LyricLine
                {
                    Time = new TimeSpan(0, 0, minute, second, millisecond),
                    Text = string.IsNullOrWhiteSpace(lyricText) ? "..." : lyricText
                });
            }
        }

        return lines.OrderBy(x => x.Time).ToList();
    }

    public int GetCurrentLineIndex(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        if (lines.Count == 0)
        {
            return -1;
        }

        var left = 0;
        var right = lines.Count - 1;
        var answer = -1;

        while (left <= right)
        {
            var mid = left + ((right - left) / 2);
            if (lines[mid].Time <= position)
            {
                answer = mid;
                left = mid + 1;
            }
            else
            {
                right = mid - 1;
            }
        }

        return answer;
    }

    private static int ParseMillisecond(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return 0;
        }

        return raw.Length switch
        {
            1 => int.Parse(raw) * 100,
            2 => int.Parse(raw) * 10,
            _ => int.Parse(raw[..3])
        };
    }
}
