using System.Net;
using System.Text.RegularExpressions;
using AnimeApp.Models;

namespace AnimeApp.Services;

public sealed class AnimeScheduleClient
{
    private const string BaseUrl = "https://animeschedule.net";
    private readonly HttpClient _httpClient = new();

    public async Task<IReadOnlyList<NextEpisodeInfo>> SearchNextEpisodesAsync(string query, CancellationToken cancellationToken = default)
    {
        var encodedQuery = Uri.EscapeDataString(query).Replace("%20", "+", StringComparison.Ordinal);
        var url = $"{BaseUrl}/api/v3/anime?q={encodedQuery}";
        var index = await _httpClient.GetStringAsync(url, cancellationToken);
        var routes = Regex.Matches(index, @"""route"":""(?<route>[^""]+)"",""premier", RegexOptions.IgnoreCase)
            .Select(match => match.Groups["route"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        var results = new List<NextEpisodeInfo>();
        foreach (var route in routes)
        {
            var html = await _httpClient.GetStringAsync($"{BaseUrl}/anime/{route}", cancellationToken);
            results.Add(new NextEpisodeInfo(
                route,
                Extract(html, @"english-title"">(?<value>[^<]*)<"),
                Extract(html, @"main-title"".*>(?<value>[^<]*)<"),
                Extract(html, @"countdown-time-raw"" datetime=""(?<value>[^""]*)"">"),
                Extract(html, @"countdown-time"" datetime=""(?<value>[^""]*)"">")));
        }

        return results;
    }

    private static string? Extract(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value).Trim() : null;
    }
}
