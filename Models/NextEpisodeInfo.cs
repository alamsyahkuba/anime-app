namespace AnimeApp.Models;

public sealed record NextEpisodeInfo(
    string Route,
    string? EnglishTitle,
    string? JapaneseTitle,
    string? NextRawRelease,
    string? NextSubRelease)
{
    public string Status => string.IsNullOrWhiteSpace(NextRawRelease) ? "Finished" : "Ongoing";

    public string Display
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(EnglishTitle))
            {
                parts.Add($"English: {EnglishTitle}");
            }

            if (!string.IsNullOrWhiteSpace(JapaneseTitle))
            {
                parts.Add($"Japanese: {JapaneseTitle}");
            }

            if (!string.IsNullOrWhiteSpace(NextRawRelease))
            {
                parts.Add($"Next raw: {NextRawRelease}");
            }

            if (!string.IsNullOrWhiteSpace(NextSubRelease))
            {
                parts.Add($"Next sub: {NextSubRelease}");
            }

            parts.Add($"Status: {Status}");
            return string.Join(Environment.NewLine, parts);
        }
    }
}
