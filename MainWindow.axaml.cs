using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AnimeApp.Models;
using AnimeApp.Services;

namespace AnimeApp;

public partial class MainWindow : Window
{
    private readonly AllAnimeClient _allAnimeClient = new();
    private readonly AnimeScheduleClient _scheduleClient = new();
    private readonly HistoryService _historyService = new();
    private readonly PlaybackService _playbackService = new();
    private readonly ObservableCollection<AnimeSearchResult> _animeResults = [];
    private readonly ObservableCollection<string> _episodes = [];
    private readonly ObservableCollection<EpisodeStream> _streams = [];

    private TextBox _queryBox = null!;
    private ComboBox _modeBox = null!;
    private Button _searchButton = null!;
    private Button _historyButton = null!;
    private Button _nextReleaseButton = null!;
    private Button _loadLinksButton = null!;
    private Button _playButton = null!;
    private Button _downloadButton = null!;
    private Button _previousButton = null!;
    private Button _nextButton = null!;
    private Button _clearHistoryButton = null!;
    private TextBox _qualityBox = null!;
    private TextBox _playerBox = null!;
    private TextBox _downloadDirBox = null!;
    private TextBox _logBox = null!;
    private TextBlock _statusText = null!;
    private ListBox _searchResultsList = null!;
    private ListBox _episodesList = null!;
    private ListBox _streamsList = null!;

    public MainWindow()
    {
        InitializeComponent();
        InitializeControls();
    }

    private void InitializeControls()
    {
        _queryBox = FindRequired<TextBox>("QueryBox");
        _modeBox = FindRequired<ComboBox>("ModeBox");
        _searchButton = FindRequired<Button>("SearchButton");
        _historyButton = FindRequired<Button>("HistoryButton");
        _nextReleaseButton = FindRequired<Button>("NextReleaseButton");
        _loadLinksButton = FindRequired<Button>("LoadLinksButton");
        _playButton = FindRequired<Button>("PlayButton");
        _downloadButton = FindRequired<Button>("DownloadButton");
        _previousButton = FindRequired<Button>("PreviousButton");
        _nextButton = FindRequired<Button>("NextButton");
        _clearHistoryButton = FindRequired<Button>("ClearHistoryButton");
        _qualityBox = FindRequired<TextBox>("QualityBox");
        _playerBox = FindRequired<TextBox>("PlayerBox");
        _downloadDirBox = FindRequired<TextBox>("DownloadDirBox");
        _logBox = FindRequired<TextBox>("LogBox");
        _statusText = FindRequired<TextBlock>("StatusText");
        _searchResultsList = FindRequired<ListBox>("SearchResultsList");
        _episodesList = FindRequired<ListBox>("EpisodesList");
        _streamsList = FindRequired<ListBox>("StreamsList");

        _searchResultsList.ItemsSource = _animeResults;
        _episodesList.ItemsSource = _episodes;
        _streamsList.ItemsSource = _streams;
        _playerBox.Text = _playbackService.DefaultPlayer;
        _downloadDirBox.Text = Environment.GetEnvironmentVariable("ANI_CLI_DOWNLOAD_DIR")
            ?? Path.Combine(Environment.CurrentDirectory, "downloads");
    }

    private async void QueryBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SearchAsync();
        }
    }

    private async void SearchButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await SearchAsync();
    }

    private async void HistoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await LoadHistoryAsync();
    }

    private async void NextReleaseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await LoadNextReleaseAsync();
    }

    private async void SearchResultsList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_searchResultsList.SelectedItem is AnimeSearchResult result)
        {
            await LoadEpisodesAsync(result);
        }
    }

    private void EpisodesList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _streams.Clear();
        SetStatus(_episodesList.SelectedItem is string episode
            ? $"Episode {episode} dipilih. Load link untuk mengambil kualitas."
            : "Pilih episode.");
    }

    private async void LoadLinksButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await LoadStreamsAsync();
    }

    private async void PlayButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await PlaySelectedAsync();
    }

    private async void DownloadButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await DownloadSelectedAsync();
    }

    private void PreviousButton_OnClick(object? sender, RoutedEventArgs e)
    {
        MoveEpisode(-1);
    }

    private void NextButton_OnClick(object? sender, RoutedEventArgs e)
    {
        MoveEpisode(1);
    }

    private async void ClearHistoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await _historyService.ClearAsync();
        AppendLog("History dihapus.");
        SetStatus("History kosong.");
    }

    private async Task SearchAsync()
    {
        var query = _queryBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            SetStatus("Masukkan judul anime dulu.");
            return;
        }

        await RunUiTaskAsync($"Mencari \"{query}\"...", async () =>
        {
            _animeResults.Clear();
            _episodes.Clear();
            _streams.Clear();

            var results = await _allAnimeClient.SearchAsync(query, SelectedMode);
            foreach (var result in results)
            {
                _animeResults.Add(result);
            }

            SetStatus(results.Count == 0
                ? "Tidak ada hasil."
                : $"{results.Count} hasil ditemukan.");
            AppendLog($"{results.Count} hasil untuk \"{query}\".");
        });
    }

    private async Task LoadHistoryAsync()
    {
        await RunUiTaskAsync("Membaca history...", async () =>
        {
            var entries = await _historyService.GetEntriesAsync();
            _animeResults.Clear();
            _episodes.Clear();
            _streams.Clear();

            foreach (var entry in entries)
            {
                _animeResults.Add(new AnimeSearchResult(entry.Id, entry.Title, SelectedMode, "history", entry.Episode));
            }

            SetStatus(entries.Count == 0 ? "History masih kosong." : $"{entries.Count} item history dimuat.");
            AppendLog($"{entries.Count} item history dimuat.");
        });
    }

    private async Task LoadNextReleaseAsync()
    {
        var query = SelectedAnime?.Name ?? _queryBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            SetStatus("Pilih anime atau isi query untuk cek jadwal.");
            return;
        }

        await RunUiTaskAsync($"Mengecek jadwal \"{query}\"...", async () =>
        {
            var results = await _scheduleClient.SearchNextEpisodesAsync(query);
            if (results.Count == 0)
            {
                AppendLog($"Tidak ada jadwal ditemukan untuk \"{query}\".");
                SetStatus("Jadwal tidak ditemukan.");
                return;
            }

            AppendLog(string.Join($"{Environment.NewLine}---{Environment.NewLine}", results.Select(result => result.Display)));
            SetStatus($"{results.Count} jadwal ditemukan.");
        });
    }

    private async Task LoadEpisodesAsync(AnimeSearchResult result)
    {
        await RunUiTaskAsync($"Memuat episode {result.Name}...", async () =>
        {
            _episodes.Clear();
            _streams.Clear();

            var episodes = await _allAnimeClient.GetEpisodesAsync(result.Id, SelectedMode);
            foreach (var episode in episodes)
            {
                _episodes.Add(episode);
            }

            if (_episodes.Count > 0)
            {
                _episodesList.SelectedItem = string.IsNullOrWhiteSpace(result.ResumeEpisode)
                    ? _episodes[0]
                    : _episodes.FirstOrDefault(episode => episode == result.ResumeEpisode) ?? _episodes[0];
            }

            SetStatus(_episodes.Count == 0
                ? $"Episode untuk {result.Name} tidak ditemukan."
                : $"{_episodes.Count} episode tersedia untuk {result.Name}.");
            AppendLog($"Episode {result.Name}: {_episodes.Count} item.");
        });
    }

    private async Task<EpisodeStream?> LoadStreamsAsync()
    {
        var anime = SelectedAnime;
        var episode = SelectedEpisode;
        if (anime is null || string.IsNullOrWhiteSpace(episode))
        {
            SetStatus("Pilih anime dan episode dulu.");
            return null;
        }

        EpisodeStream? selected = null;
        await RunUiTaskAsync($"Mengambil link episode {episode}...", async () =>
        {
            _streams.Clear();
            var streams = await _allAnimeClient.GetStreamsAsync(anime.Id, SelectedMode, episode);
            foreach (var stream in streams)
            {
                _streams.Add(stream);
            }

            selected = AllAnimeClient.SelectStream(streams, _qualityBox.Text);
            if (selected is not null)
            {
                _streamsList.SelectedItem = selected;
            }

            SetStatus(streams.Count == 0
                ? "Tidak ada link valid dari provider."
                : $"{streams.Count} link ditemukan.");
            AppendLog($"{streams.Count} link untuk {anime.Name} episode {episode}.");
        });

        return selected;
    }

    private async Task PlaySelectedAsync()
    {
        var anime = SelectedAnime;
        var episode = SelectedEpisode;
        if (anime is null || string.IsNullOrWhiteSpace(episode))
        {
            SetStatus("Pilih anime dan episode dulu.");
            return;
        }

        var stream = SelectedStream ?? await LoadStreamsAsync();
        if (stream is null)
        {
            SetStatus("Tidak ada link untuk diputar.");
            return;
        }

        await RunUiTaskAsync($"Membuka player untuk episode {episode}...", async () =>
        {
            await _playbackService.PlayAsync(stream, anime.Name, episode, _playerBox.Text);
            await _historyService.UpdateAsync(anime.Id, anime.Name, episode);
            SetStatus($"Player dibuka: {anime.Name} episode {episode}.");
            AppendLog($"Play {stream.Display}: {stream.Url}");
        });
    }

    private async Task DownloadSelectedAsync()
    {
        var anime = SelectedAnime;
        var episode = SelectedEpisode;
        if (anime is null || string.IsNullOrWhiteSpace(episode))
        {
            SetStatus("Pilih anime dan episode dulu.");
            return;
        }

        var stream = SelectedStream ?? await LoadStreamsAsync();
        if (stream is null)
        {
            SetStatus("Tidak ada link untuk di-download.");
            return;
        }

        await RunUiTaskAsync($"Download episode {episode}...", async () =>
        {
            await _playbackService.DownloadAsync(stream, anime.Name, episode, _downloadDirBox.Text);
            await _historyService.UpdateAsync(anime.Id, anime.Name, episode);
            SetStatus($"Download selesai: {anime.Name} episode {episode}.");
            AppendLog($"Download selesai: {anime.Name} episode {episode}.");
        });
    }

    private void MoveEpisode(int delta)
    {
        if (_episodes.Count == 0)
        {
            return;
        }

        var index = _episodesList.SelectedIndex;
        if (index < 0)
        {
            index = 0;
        }

        var nextIndex = Math.Clamp(index + delta, 0, _episodes.Count - 1);
        _episodesList.SelectedIndex = nextIndex;
        _streams.Clear();
    }

    private async Task RunUiTaskAsync(string status, Func<Task> action)
    {
        SetBusy(true);
        SetStatus(status);

        try
        {
            await action();
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            AppendLog($"Error: {exception.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _searchButton.IsEnabled = !busy;
        _historyButton.IsEnabled = !busy;
        _nextReleaseButton.IsEnabled = !busy;
        _loadLinksButton.IsEnabled = !busy;
        _playButton.IsEnabled = !busy;
        _downloadButton.IsEnabled = !busy;
        _previousButton.IsEnabled = !busy;
        _nextButton.IsEnabled = !busy;
        _clearHistoryButton.IsEnabled = !busy;
    }

    private void SetStatus(string message)
    {
        _statusText.Text = message;
    }

    private void AppendLog(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        _logBox.Text = string.IsNullOrWhiteSpace(_logBox.Text)
            ? line
            : $"{_logBox.Text}{Environment.NewLine}{line}";
        _logBox.CaretIndex = _logBox.Text.Length;
    }

    private string SelectedMode
    {
        get
        {
            if (_modeBox.SelectedItem is ComboBoxItem item && item.Content is not null)
            {
                return item.Content.ToString()?.ToLowerInvariant() ?? "sub";
            }

            return "sub";
        }
    }

    private AnimeSearchResult? SelectedAnime => _searchResultsList.SelectedItem as AnimeSearchResult;

    private string? SelectedEpisode => _episodesList.SelectedItem as string;

    private EpisodeStream? SelectedStream => _streamsList.SelectedItem as EpisodeStream;

    private T FindRequired<T>(string name) where T : Control
    {
        return this.FindControl<T>(name)
            ?? throw new InvalidOperationException($"Control '{name}' was not found.");
    }

    protected override void OnClosed(EventArgs e)
    {
        _allAnimeClient.Dispose();
        base.OnClosed(e);
    }
}
