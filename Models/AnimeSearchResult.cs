namespace AnimeApp.Models;

public sealed record AnimeSearchResult(
    string Id,
    string Name,
    string Mode,
    string EpisodeCount,
    string? ResumeEpisode = null)
{
    public string Detail => string.IsNullOrWhiteSpace(ResumeEpisode)
        ? $"{EpisodeCount} episodes"
        : $"continue episode {ResumeEpisode}";

    public override string ToString() => $"{Name} ({Detail})";
}
