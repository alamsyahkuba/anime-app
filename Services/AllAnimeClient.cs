using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AnimeApp.Models;

namespace AnimeApp.Services;

public sealed class AllAnimeClient : IDisposable
{
    public const string AllAnimeReferrer = "https://youtu-chan.com";
    private const string AllAnimeBase = "allanime.day";
    private const string AllAnimeApi = "https://api.allanime.day";
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:150.0) Gecko/20100101 Firefox/150.0";
    private const string EpisodeEmbedPersistedHash = "d405d0edd690624b66baba3068e0edc3ac90f1597d898a1ec8db4e5c43c00fec";
    private static readonly byte[] AllAnimeCipherKey = SHA256.HashData(Encoding.UTF8.GetBytes("Xot36i3lK3:v1"));

    private static readonly (string Source, string SourceName)[] ProviderOrder =
    [
        ("wixmp", "Default"),
        ("youtube", "Yt-mp4"),
        ("sharepoint", "S-mp4"),
        ("hianime", "Luf-Mp4")
    ];

    private readonly HttpClient _httpClient = new();

    public AllAnimeClient()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _httpClient.DefaultRequestHeaders.Referrer = new Uri(AllAnimeReferrer);
    }

    public async Task<IReadOnlyList<AnimeSearchResult>> SearchAsync(string query, string mode, CancellationToken cancellationToken = default)
    {
        const string searchGraphQl = "query( $search: SearchInput $limit: Int $page: Int $translationType: VaildTranslationTypeEnumType $countryOrigin: VaildCountryOriginEnumType ) { shows( search: $search limit: $limit page: $page translationType: $translationType countryOrigin: $countryOrigin ) { edges { _id name availableEpisodes __typename } }}";

        var variables = new
        {
            search = new
            {
                allowAdult = false,
                allowUnknown = false,
                query
            },
            limit = 40,
            page = 1,
            translationType = mode,
            countryOrigin = "ALL"
        };

        using var document = await GraphQlAsync(searchGraphQl, variables, cancellationToken);
        var results = new List<AnimeSearchResult>();

        if (!TryGetElement(document.RootElement, "data", "shows", "edges", out var edges) || edges.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var edge in edges.EnumerateArray())
        {
            var id = ReadString(edge, "_id");
            var name = WebUtility.HtmlDecode(ReadString(edge, "name"));
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var episodeCount = "0";
            if (edge.TryGetProperty("availableEpisodes", out var availableEpisodes)
                && availableEpisodes.TryGetProperty(mode, out var modeEpisodes))
            {
                episodeCount = ReadJsonValue(modeEpisodes);
            }

            if (episodeCount != "0")
            {
                results.Add(new AnimeSearchResult(id, name, mode, episodeCount));
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<string>> GetEpisodesAsync(string showId, string mode, CancellationToken cancellationToken = default)
    {
        const string episodesGraphQl = "query ($showId: String!) { show( _id: $showId ) { _id availableEpisodesDetail }}";
        var variables = new { showId };

        using var document = await GraphQlAsync(episodesGraphQl, variables, cancellationToken);
        if (!TryGetElement(document.RootElement, "data", "show", "availableEpisodesDetail", out var details)
            || !details.TryGetProperty(mode, out var episodes)
            || episodes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return episodes
            .EnumerateArray()
            .Select(ReadJsonValue)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(ParseEpisodeNumber)
            .ThenBy(value => value, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<EpisodeStream>> GetStreamsAsync(
        string showId,
        string mode,
        string episode,
        CancellationToken cancellationToken = default)
    {
        var variables = new
        {
            showId,
            translationType = mode,
            episodeString = episode
        };

        using var document = await GraphQlPersistedAsync(EpisodeEmbedPersistedHash, variables, cancellationToken);
        if (!TryGetEpisodeSourceUrls(document.RootElement, out var sourceUrls))
        {
            return [];
        }

        var candidates = GetProviderCandidates(sourceUrls);
        var streams = new List<EpisodeStream>();
        foreach (var provider in candidates)
        {
            try
            {
                streams.AddRange(await GetLinksAsync(provider, cancellationToken));
            }
            catch
            {
                // ani-cli probes providers independently; one broken provider should not hide the rest.
            }
        }

        return streams
            .Where(stream => !string.IsNullOrWhiteSpace(stream.Url))
            .DistinctBy(stream => stream.Url)
            .OrderByDescending(stream => stream.QualityHeight)
            .ThenBy(stream => stream.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static EpisodeStream? SelectStream(IEnumerable<EpisodeStream> streams, string? quality)
    {
        var ordered = streams
            .OrderByDescending(stream => stream.QualityHeight)
            .ThenBy(stream => stream.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ordered.Count == 0)
        {
            return null;
        }

        quality = string.IsNullOrWhiteSpace(quality) ? "best" : quality.Trim();
        if (quality.Equals("best", StringComparison.OrdinalIgnoreCase))
        {
            return ordered.First();
        }

        if (quality.Equals("worst", StringComparison.OrdinalIgnoreCase))
        {
            return ordered.Last();
        }

        return ordered.FirstOrDefault(stream => stream.Quality.Contains(quality, StringComparison.OrdinalIgnoreCase))
            ?? ordered.First();
    }

    private async Task<JsonDocument> GraphQlAsync<TVariables>(string query, TVariables variables, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            variables,
            query
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{AllAnimeApi}/api")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        request.Headers.Referrer = new Uri(AllAnimeReferrer);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, content);

        return ParseGraphQlContent(content);
    }

    private async Task<JsonDocument> GraphQlPersistedAsync<TVariables>(
        string hash,
        TVariables variables,
        CancellationToken cancellationToken)
    {
        var variablesJson = JsonSerializer.Serialize(variables);
        var extensionsJson = JsonSerializer.Serialize(new
        {
            persistedQuery = new
            {
                version = 1,
                sha256Hash = hash
            }
        });

        var url = $"{AllAnimeApi}/api?variables={Uri.EscapeDataString(variablesJson)}&extensions={Uri.EscapeDataString(extensionsJson)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri(AllAnimeReferrer);
        request.Headers.TryAddWithoutValidation("Origin", AllAnimeReferrer);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, content);

        return ParseGraphQlContent(content);
    }

    private static JsonDocument ParseGraphQlContent(string content)
    {
        var document = JsonDocument.Parse(content);
        if (!TryGetElement(document.RootElement, "data", "tobeparsed", out var encryptedElement)
            || encryptedElement.ValueKind != JsonValueKind.String)
        {
            return document;
        }

        var encrypted = encryptedElement.GetString();
        document.Dispose();

        if (string.IsNullOrWhiteSpace(encrypted))
        {
            throw new InvalidOperationException("AllAnime returned an empty encrypted payload.");
        }

        try
        {
            return JsonDocument.Parse(DecryptAllAnimePayload(encrypted));
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or JsonException)
        {
            throw new InvalidOperationException("AllAnime returned an encrypted payload that could not be decoded.", exception);
        }
    }

    private List<ProviderCandidate> GetProviderCandidates(JsonElement sourceUrls)
    {
        var rawSources = sourceUrls
            .EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.Object)
            .Select(element => new
            {
                SourceName = ReadString(element, "sourceName"),
                SourceUrl = ReadString(element, "sourceUrl")
            })
            .Where(source => !string.IsNullOrWhiteSpace(source.SourceName) && !string.IsNullOrWhiteSpace(source.SourceUrl))
            .ToList();

        var candidates = new List<ProviderCandidate>();
        foreach (var provider in ProviderOrder)
        {
            var source = rawSources.FirstOrDefault(item =>
                item.SourceName.Contains(provider.SourceName, StringComparison.OrdinalIgnoreCase));
            if (source is null)
            {
                continue;
            }

            var decodedPath = DecodeProviderPath(source.SourceUrl);
            if (!string.IsNullOrWhiteSpace(decodedPath))
            {
                candidates.Add(new ProviderCandidate(provider.Source, decodedPath));
            }
        }

        return candidates;
    }

    private async Task<IReadOnlyList<EpisodeStream>> GetLinksAsync(ProviderCandidate provider, CancellationToken cancellationToken)
    {
        var streams = new List<EpisodeStream>();
        if (provider.Path.Contains("tools.fast4speed.rsvp", StringComparison.OrdinalIgnoreCase))
        {
            streams.Add(new EpisodeStream("youtube", "Yt", provider.Path, AllAnimeReferrer, null, IsHls: false));
        }

        string response;
        try
        {
            response = NormalizeEscapedJson(await FetchStringAsync(BuildProviderUri(provider.Path), AllAnimeReferrer, cancellationToken));
        }
        catch when (streams.Count > 0)
        {
            return streams;
        }

        var rawLinks = ExtractRawLinks(response);
        if (rawLinks.Any(link => link.Url.Contains("repackager.wixmp.com", StringComparison.OrdinalIgnoreCase)))
        {
            streams.AddRange(ExtractWixmpLinks(provider.Source, rawLinks));
            return streams;
        }

        if (rawLinks.Any(link => link.Url.Contains("master.m3u8", StringComparison.OrdinalIgnoreCase)))
        {
            streams.AddRange(await ExtractHlsLinksAsync(provider.Source, response, rawLinks, cancellationToken));
            return streams;
        }

        streams.AddRange(rawLinks.Select(link => new EpisodeStream(
            provider.Source,
            NormalizeQuality(link.Quality),
            link.Url,
            AllAnimeReferrer,
            null,
            IsHls: false)));

        return streams;
    }

    private async Task<string> FetchStringAsync(string url, string? referrer, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(referrer))
        {
            request.Headers.Referrer = new Uri(referrer);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static List<RawEpisodeLink> ExtractRawLinks(string response)
    {
        var links = new List<RawEpisodeLink>();

        foreach (Match match in Regex.Matches(
            response,
            "link\":\"(?<url>[^\"]*)\".*?\"resolutionStr\":\"(?<quality>[^\"]*)\"",
            RegexOptions.Singleline))
        {
            links.Add(new RawEpisodeLink(
                NormalizeQuality(match.Groups["quality"].Value),
                NormalizeEscapedJson(match.Groups["url"].Value)));
        }

        foreach (Match match in Regex.Matches(
            response,
            "hls\"\\s*,\\s*\"url\":\"(?<url>[^\"]*)\".*?\"hardsub_lang\":\"en-US\"",
            RegexOptions.Singleline))
        {
            links.Add(new RawEpisodeLink("hls", NormalizeEscapedJson(match.Groups["url"].Value)));
        }

        if (links.All(link => !link.Url.Contains("master.m3u8", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (Match match in Regex.Matches(response, "\"url\":\"(?<url>[^\"]*master\\.m3u8[^\"]*)\"", RegexOptions.Singleline))
            {
                links.Add(new RawEpisodeLink("hls", NormalizeEscapedJson(match.Groups["url"].Value)));
            }
        }

        return links;
    }

    private static IEnumerable<EpisodeStream> ExtractWixmpLinks(string source, IReadOnlyList<RawEpisodeLink> rawLinks)
    {
        foreach (var link in rawLinks.Where(link => link.Url.Contains("repackager.wixmp.com", StringComparison.OrdinalIgnoreCase)))
        {
            var extractLink = Regex.Replace(
                link.Url.Replace("repackager.wixmp.com/", string.Empty, StringComparison.OrdinalIgnoreCase),
                "\\.urlset.*",
                string.Empty,
                RegexOptions.IgnoreCase);

            var match = Regex.Match(link.Url, @"/(?<qualities>[^/]*),/mp4", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                yield return new EpisodeStream(source, NormalizeQuality(link.Quality), extractLink, AllAnimeReferrer, null, IsHls: false);
                continue;
            }

            foreach (var qualityToken in match.Groups["qualities"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var resolved = Regex.Replace(extractLink, ",[^/]*", qualityToken);
                yield return new EpisodeStream(source, NormalizeQuality(qualityToken), resolved, AllAnimeReferrer, null, IsHls: false);
            }
        }
    }

    private async Task<IEnumerable<EpisodeStream>> ExtractHlsLinksAsync(
        string source,
        string response,
        IReadOnlyList<RawEpisodeLink> rawLinks,
        CancellationToken cancellationToken)
    {
        var masterUrl = rawLinks.FirstOrDefault(link => link.Url.Contains("master.m3u8", StringComparison.OrdinalIgnoreCase))?.Url;
        if (string.IsNullOrWhiteSpace(masterUrl))
        {
            return [];
        }

        var referrer = ExtractRegexGroup(response, "Referer\":\"(?<value>[^\"]*)\"") ?? AllAnimeReferrer;
        var subtitleUrl = ExtractRegexGroup(
            response,
            "\"subtitles\":\\[\\{\"lang\":\"en\",\"label\":\"English\",\"default\":\"default\",\"src\":\"(?<value>[^\"]*)\"");

        var playlist = await FetchStringAsync(masterUrl, referrer, cancellationToken);
        if (!playlist.Contains("#EXTM3U", StringComparison.OrdinalIgnoreCase))
        {
            return [new EpisodeStream(source, "hls", masterUrl, referrer, subtitleUrl, IsHls: true)];
        }

        var streams = new List<EpisodeStream>();
        var lines = playlist
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!line.StartsWith("#EXT-X-STREAM", StringComparison.OrdinalIgnoreCase)
                || line.Contains("EXT-X-I-FRAME", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var height = ExtractRegexGroup(line, @"RESOLUTION=\d+x(?<value>\d+)") ?? "0";
            var streamPath = lines.Skip(index + 1).FirstOrDefault(value => !value.StartsWith('#'));
            if (string.IsNullOrWhiteSpace(streamPath))
            {
                continue;
            }

            var streamUrl = ResolveUrl(masterUrl, streamPath);
            streams.Add(new EpisodeStream(source, NormalizeQuality(height), streamUrl, referrer, subtitleUrl, IsHls: true));
        }

        return streams;
    }

    private static string DecodeProviderPath(string sourceUrl)
    {
        var encoded = sourceUrl.StartsWith("--", StringComparison.Ordinal) ? sourceUrl[2..] : sourceUrl;
        encoded = NormalizeEscapedJson(encoded);

        if (encoded.Length % 2 == 0 && Regex.IsMatch(encoded, @"\A[0-9a-fA-F]+\z"))
        {
            var chars = new char[encoded.Length / 2];
            for (var index = 0; index < encoded.Length; index += 2)
            {
                var value = Convert.ToByte(encoded.Substring(index, 2), 16);
                chars[index / 2] = (char)(value ^ 0x38);
            }

            encoded = new string(chars);
        }

        return Regex.Replace(encoded, "/clock(?!\\.json)", "/clock.json");
    }

    private static string BuildProviderUri(string providerPath)
    {
        if (Uri.TryCreate(providerPath, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        return $"https://{AllAnimeBase}{providerPath}";
    }

    private static string ResolveUrl(string baseUrl, string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        return new Uri(new Uri(baseUrl), path).ToString();
    }

    private static bool TryGetEpisodeSourceUrls(JsonElement root, out JsonElement value)
    {
        return TryGetElement(root, "data", "episode", "sourceUrls", out value)
            || TryGetElement(root, "episode", "sourceUrls", out value);
    }

    private static bool TryGetElement(JsonElement root, string first, string second, out JsonElement value)
    {
        value = default;
        return root.TryGetProperty(first, out var firstValue)
            && firstValue.TryGetProperty(second, out value);
    }

    private static bool TryGetElement(JsonElement root, string first, string second, string third, out JsonElement value)
    {
        value = default;
        return root.TryGetProperty(first, out var firstValue)
            && firstValue.TryGetProperty(second, out var secondValue)
            && secondValue.TryGetProperty(third, out value);
    }

    private static bool TryGetElement(JsonElement root, string first, string second, string third, string fourth, out JsonElement value)
    {
        value = default;
        return root.TryGetProperty(first, out var firstValue)
            && firstValue.TryGetProperty(second, out var secondValue)
            && secondValue.TryGetProperty(third, out var thirdValue)
            && thirdValue.TryGetProperty(fourth, out value);
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? ReadJsonValue(property)
            : string.Empty;
    }

    private static string ReadJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static decimal ParseEpisodeNumber(string value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number
            : decimal.MaxValue;
    }

    private static string NormalizeQuality(string quality)
    {
        quality = quality.Trim();
        if (Regex.IsMatch(quality, @"^\d{3,4}$"))
        {
            return $"{quality}p";
        }

        return string.IsNullOrWhiteSpace(quality) ? "unknown" : quality;
    }

    private static string NormalizeEscapedJson(string value)
    {
        return value
            .Replace("\\u002F", "/", StringComparison.OrdinalIgnoreCase)
            .Replace("\\/", "/", StringComparison.Ordinal)
            .Replace("\\", string.Empty, StringComparison.Ordinal);
    }

    private static string? ExtractRegexGroup(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return match.Success ? NormalizeEscapedJson(match.Groups["value"].Value) : null;
    }

    private static string DecryptAllAnimePayload(string encrypted)
    {
        var payload = Convert.FromBase64String(encrypted);
        if (payload.Length <= 29)
        {
            throw new CryptographicException("Encrypted payload is too short.");
        }

        var counter = new byte[16];
        Buffer.BlockCopy(payload, 1, counter, 0, 12);
        counter[15] = 2;

        var cipherLength = payload.Length - 13 - 16;
        var cipherText = new byte[cipherLength];
        Buffer.BlockCopy(payload, 13, cipherText, 0, cipherLength);

        return Encoding.UTF8.GetString(AesCtrTransform(cipherText, AllAnimeCipherKey, counter));
    }

    private static byte[] AesCtrTransform(byte[] input, byte[] key, byte[] counter)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key;

        using var encryptor = aes.CreateEncryptor();
        var output = new byte[input.Length];
        var keystream = new byte[16];

        for (var offset = 0; offset < input.Length; offset += 16)
        {
            encryptor.TransformBlock(counter, 0, counter.Length, keystream, 0);

            var blockLength = Math.Min(16, input.Length - offset);
            for (var index = 0; index < blockLength; index++)
            {
                output[offset + index] = (byte)(input[offset + index] ^ keystream[index]);
            }

            IncrementCounter(counter);
        }

        return output;
    }

    private static void IncrementCounter(byte[] counter)
    {
        for (var index = counter.Length - 1; index >= 0; index--)
        {
            counter[index]++;
            if (counter[index] != 0)
            {
                return;
            }
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string content)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = content.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase)
            || content.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
            ? "AllAnime menolak request dengan Cloudflare challenge."
            : Truncate(content, 300);

        throw new HttpRequestException(
            $"AllAnime request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {detail}",
            null,
            response.StatusCode);
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : $"{text[..maxLength]}...";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private sealed record ProviderCandidate(string Source, string Path);

    private sealed record RawEpisodeLink(string Quality, string Url);
}
