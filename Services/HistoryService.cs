using AnimeApp.Models;

namespace AnimeApp.Services;

public sealed class HistoryService
{
    private readonly string _historyFile;

    public HistoryService()
    {
        var historyDirectory = Environment.GetEnvironmentVariable("ANI_CLI_HIST_DIR");
        if (string.IsNullOrWhiteSpace(historyDirectory))
        {
            var stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            historyDirectory = string.IsNullOrWhiteSpace(stateHome)
                ? Path.Combine(home, ".local", "state", "ani-cli")
                : Path.Combine(stateHome, "ani-cli");
        }

        Directory.CreateDirectory(historyDirectory);
        _historyFile = Path.Combine(historyDirectory, "ani-hsts");
        if (!File.Exists(_historyFile))
        {
            File.WriteAllText(_historyFile, string.Empty);
        }
    }

    public async Task<IReadOnlyList<HistoryEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        var lines = await File.ReadAllLinesAsync(_historyFile, cancellationToken);
        return lines
            .Select(ParseLine)
            .Where(entry => entry is not null)
            .Cast<HistoryEntry>()
            .ToList();
    }

    public async Task UpdateAsync(string id, string title, string episode, CancellationToken cancellationToken = default)
    {
        var entries = (await GetEntriesAsync(cancellationToken)).ToList();
        var existingIndex = entries.FindIndex(entry => entry.Id == id);
        var updated = new HistoryEntry(episode, id, title);

        if (existingIndex >= 0)
        {
            entries[existingIndex] = updated;
        }
        else
        {
            entries.Add(updated);
        }

        await WriteEntriesAsync(entries, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        return File.WriteAllTextAsync(_historyFile, string.Empty, cancellationToken);
    }

    private async Task WriteEntriesAsync(IEnumerable<HistoryEntry> entries, CancellationToken cancellationToken)
    {
        var temporaryFile = $"{_historyFile}.new";
        var lines = entries.Select(entry => $"{entry.Episode}\t{entry.Id}\t{entry.Title}");
        await File.WriteAllLinesAsync(temporaryFile, lines, cancellationToken);
        File.Move(temporaryFile, _historyFile, overwrite: true);
    }

    private static HistoryEntry? ParseLine(string line)
    {
        var parts = line.Split('\t', 3);
        return parts.Length == 3 ? new HistoryEntry(parts[0], parts[1], parts[2]) : null;
    }
}
