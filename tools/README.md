# Bundled Tools

Place optional Windows external executables here before running `./build.sh windows`.

Linux builds intentionally do not bundle these tools. Install them with apt instead:

```sh
sudo apt update
sudo apt install mpv ffmpeg aria2 yt-dlp
```

Expected layout:

```text
tools/
  win-x64/
    mpv.exe
    ffmpeg.exe
    aria2c.exe
    yt-dlp.exe
```

At runtime, AnimeApp searches this folder first, then falls back to commands installed in `PATH`.

Downloaded archives and source packages should be kept outside this folder, for example in
`tools-archives/`, so they are not copied into the published app.
