using System.Globalization;
using System.Text.RegularExpressions;

namespace AnimeApp.Models;

public sealed record EpisodeStream(
    string Source,
    string Quality,
    string Url,
    string? Referrer,
    string? SubtitleUrl,
    bool IsHls)
{
    public int QualityHeight
    {
        get
        {
            var match = Regex.Match(Quality, @"\d{3,4}");
            return match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
                ? height
                : 0;
        }
    }

    public string Display
    {
        get
        {
            var flags = new List<string>();
            if (IsHls)
            {
                flags.Add("HLS");
            }

            if (!string.IsNullOrWhiteSpace(SubtitleUrl))
            {
                flags.Add("sub");
            }

            var suffix = flags.Count == 0 ? string.Empty : $" ({string.Join(", ", flags)})";
            return $"{Quality} - {Source}{suffix}";
        }
    }

    public override string ToString() => Display;
}
