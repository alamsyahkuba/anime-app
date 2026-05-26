using System.Diagnostics;
using System.Runtime.InteropServices;
using AnimeApp.Models;

namespace AnimeApp.Services;

public sealed class PlaybackService
{
    public string DefaultPlayer { get; } = DetectDefaultPlayer();

    public Task PlayAsync(EpisodeStream stream, string title, string episode, string? playerCommand)
    {
        var command = string.IsNullOrWhiteSpace(playerCommand)
            ? SplitDefaultCommand(DefaultPlayer)
            : SplitCommand(playerCommand);
        if (command.Count == 0)
        {
            throw new InvalidOperationException("Player command is empty.");
        }

        var process = CreateProcess(command);
        var commandText = string.Join(' ', command);
        var mediaTitle = $"{title} Episode {episode}";

        if (commandText.Contains("vlc", StringComparison.OrdinalIgnoreCase))
        {
            process.ArgumentList.Add($"--http-referrer={AllAnimeClient.AllAnimeReferrer}");
            process.ArgumentList.Add("--play-and-exit");
            process.ArgumentList.Add($"--meta-title={mediaTitle}");
            process.ArgumentList.Add(stream.Url);
        }
        else if (commandText.Contains("iina", StringComparison.OrdinalIgnoreCase))
        {
            process.ArgumentList.Add("--no-stdin");
            process.ArgumentList.Add("--keep-running");
            process.ArgumentList.Add($"--mpv-force-media-title={mediaTitle}");
            AddMpvArguments(process, stream, iinaPrefix: true);
            process.ArgumentList.Add(stream.Url);
        }
        else
        {
            process.ArgumentList.Add($"--force-media-title={mediaTitle}");
            process.ArgumentList.Add(stream.Url);
            AddMpvArguments(process, stream, iinaPrefix: false);
        }

        if (Process.Start(process) is null)
        {
            throw new InvalidOperationException($"Failed to start player: {command[0]}");
        }

        return Task.CompletedTask;
    }

    public async Task DownloadAsync(EpisodeStream stream, string title, string episode, string? downloadDirectory, CancellationToken cancellationToken = default)
    {
        downloadDirectory = string.IsNullOrWhiteSpace(downloadDirectory)
            ? Environment.GetEnvironmentVariable("ANI_CLI_DOWNLOAD_DIR") ?? Environment.CurrentDirectory
            : downloadDirectory;

        Directory.CreateDirectory(downloadDirectory);
        var fileName = $"{MakeSafeFileName(title)} Episode {MakeSafeFileName(episode)}";
        var outputPath = Path.Combine(downloadDirectory, $"{fileName}.mp4");

        if (!string.IsNullOrWhiteSpace(stream.SubtitleUrl))
        {
            await DownloadSubtitleAsync(stream.SubtitleUrl, Path.Combine(downloadDirectory, $"{fileName}.vtt"), cancellationToken);
        }

        if (stream.IsHls || stream.Url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            var ytDlp = ResolveTool("yt-dlp");
            if (ytDlp is not null)
            {
                await RunProcessAsync(
                    ytDlp,
                    [
                        "--referer", stream.Referrer ?? AllAnimeClient.AllAnimeReferrer,
                        stream.Url,
                        "--no-skip-unavailable-fragments",
                        "--fragment-retries", "infinite",
                        "-N", "16",
                        "-o", outputPath
                    ],
                    cancellationToken);
            }
            else
            {
                var ffmpeg = ResolveTool("ffmpeg") ?? "ffmpeg";
                await RunProcessAsync(
                    ffmpeg,
                    [
                        "-extension_picky", "0",
                        "-referer", stream.Referrer ?? AllAnimeClient.AllAnimeReferrer,
                        "-loglevel", "error",
                        "-stats",
                        "-i", stream.Url,
                        "-c", "copy",
                        outputPath
                    ],
                    cancellationToken);
            }

            return;
        }

        var aria2c = ResolveTool("aria2c");
        if (aria2c is not null)
        {
            await RunProcessAsync(
                aria2c,
                [
                    $"--referer={AllAnimeClient.AllAnimeReferrer}",
                    "--enable-rpc=false",
                    "--check-certificate=false",
                    "--continue",
                    "--summary-interval=0",
                    "-x", "16",
                    "-s", "16",
                    stream.Url,
                    $"--dir={downloadDirectory}",
                    "-o", $"{fileName}.mp4",
                    "--download-result=hide"
                ],
                cancellationToken);
        }
        else
        {
            var curl = ResolveTool("curl") ?? "curl";
            await RunProcessAsync(
                curl,
                [
                    "-L",
                    "-e", AllAnimeClient.AllAnimeReferrer,
                    "-o", outputPath,
                    stream.Url
                ],
                cancellationToken);
        }
    }

    private static ProcessStartInfo CreateProcess(IReadOnlyList<string> command)
    {
        var process = new ProcessStartInfo
        {
            FileName = ResolveTool(command[0]) ?? command[0],
            UseShellExecute = false
        };

        foreach (var argument in command.Skip(1))
        {
            process.ArgumentList.Add(argument);
        }

        return process;
    }

    private static void AddMpvArguments(ProcessStartInfo process, EpisodeStream stream, bool iinaPrefix)
    {
        var referrer = stream.Referrer;
        if (!string.IsNullOrWhiteSpace(referrer))
        {
            process.ArgumentList.Add(iinaPrefix ? $"--mpv-referrer={referrer}" : $"--referrer={referrer}");
        }

        if (!string.IsNullOrWhiteSpace(stream.SubtitleUrl))
        {
            process.ArgumentList.Add(iinaPrefix ? $"--mpv-sub-file={stream.SubtitleUrl}" : $"--sub-file={stream.SubtitleUrl}");
        }
    }

    private static async Task DownloadSubtitleAsync(string url, string outputPath, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        var content = await client.GetByteArrayAsync(url, cancellationToken);
        await File.WriteAllBytesAsync(outputPath, content, cancellationToken);
    }

    private static async Task RunProcessAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var process = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            process.ArgumentList.Add(argument);
        }

        using var child = Process.Start(process) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
        var outputTask = child.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = child.StandardError.ReadToEndAsync(cancellationToken);
        await child.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;
        if (child.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(error) ? output : error;
            throw new InvalidOperationException($"{fileName} exited with code {child.ExitCode}: {detail.Trim()}");
        }
    }

    private static string DetectDefaultPlayer()
    {
        if (OperatingSystem.IsMacOS())
        {
            var bundledMpv = ResolveTool("mpv");
            if (bundledMpv is not null)
            {
                return bundledMpv;
            }

            return File.Exists("/Applications/IINA.app/Contents/MacOS/iina-cli")
                ? "/Applications/IINA.app/Contents/MacOS/iina-cli"
                : "iina";
        }

        if (OperatingSystem.IsWindows())
        {
            return ResolveTool("mpv") ?? "mpv.exe";
        }

        var linuxMpv = ResolveTool("mpv");
        if (linuxMpv is not null)
        {
            return linuxMpv;
        }

        if (CommandExists("flatpak") && RunExitCode("flatpak", ["info", "io.mpv.Mpv"]) == 0)
        {
            return "flatpak run io.mpv.Mpv";
        }

        return "mpv";
    }

    private static int RunExitCode(string fileName, IReadOnlyList<string> arguments)
    {
        try
        {
            using var process = Process.Start(CreateProcess([fileName, .. arguments]));
            if (process is null)
            {
                return -1;
            }

            process.WaitForExit(3000);
            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }

    private static bool CommandExists(string command)
    {
        return ResolveTool(command) is not null;
    }

    private static string? ResolveTool(string command)
    {
        if (Path.IsPathRooted(command))
        {
            return File.Exists(command) ? command : null;
        }

        var candidates = new List<string>();
        var toolName = ToolExecutableName(command);
        var baseDirectory = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDirectory, "tools", CurrentToolRuntime(), toolName));
        candidates.Add(Path.Combine(baseDirectory, "tools", toolName));

        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        candidates.AddRange(paths.Select(path => Path.Combine(path, toolName)));
        candidates.AddRange(paths.Select(path => Path.Combine(path, command)));

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string ToolExecutableName(string command)
    {
        var fileName = Path.GetFileName(command);
        if (OperatingSystem.IsWindows() && !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return $"{fileName}.exe";
        }

        return fileName;
    }

    private static string CurrentToolRuntime()
    {
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        };

        return $"{os}-{architecture}";
    }

    private static IReadOnlyList<string> SplitDefaultCommand(string command)
    {
        return File.Exists(command) ? [command] : SplitCommand(command);
    }

    private static IReadOnlyList<string> SplitCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return [];
        }

        var parts = new List<string>();
        var current = new List<char>();
        var inQuote = false;

        foreach (var character in command)
        {
            if (character == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuote)
            {
                if (current.Count > 0)
                {
                    parts.Add(new string([.. current]));
                    current.Clear();
                }

                continue;
            }

            current.Add(character);
        }

        if (current.Count > 0)
        {
            parts.Add(new string([.. current]));
        }

        return parts;
    }

    private static string MakeSafeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Trim();
    }
}
