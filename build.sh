#!/usr/bin/env bash
set -euo pipefail

PROJECT_FILE="AnimeApp.csproj"
SDK_IMAGE="mcr.microsoft.com/dotnet/sdk:8.0"
APP_NAME="AnimeApp"
PACKAGE_NAME="anime-app"
VERSION="${VERSION:-1.0.0}"

usage() {
  printf '%s\n' "Usage: ./build.sh [all|linux|windows|deb|clean]"
  printf '%s\n' ""
  printf '%s\n' "Outputs:"
  printf '%s\n' "  linux   publish/linux-x64/AnimeApp"
  printf '%s\n' "  windows publish/win-x64/AnimeApp.exe"
  printf '%s\n' "  deb     dist/anime-app_${VERSION}_amd64.deb"
  printf '%s\n' ""
  printf '%s\n' "Set VERSION=x.y.z to change package version."
}

publish_target() {
  local runtime="$1"
  local output="$2"
  local include_tools="${3:-false}"

  rm -rf "$output"

  docker run --rm --user "$(id -u):$(id -g)" \
    -e HOME=/tmp/dotnet \
    -e DOTNET_CLI_HOME=/tmp/dotnet \
    -e NUGET_PACKAGES=/tmp/nuget \
    -v "$PWD":/src -w /src \
    "$SDK_IMAGE" \
    dotnet publish "$PROJECT_FILE" \
      -c Release \
      -r "$runtime" \
      --self-contained true \
      -o "$output" \
      /p:PublishSingleFile=true \
      /p:IncludeNativeLibrariesForSelfExtract=true \
      "/p:IncludeBundledTools=$include_tools"
}

package_deb() {
  local output="dist/${PACKAGE_NAME}_${VERSION}_amd64.deb"
  local package_root="dist/deb/${PACKAGE_NAME}_${VERSION}_amd64"

  publish_target linux-x64 publish/linux-x64 false

  rm -rf "$package_root" "$output"
  mkdir -p \
    "$package_root/DEBIAN" \
    "$package_root/opt/$PACKAGE_NAME" \
    "$package_root/usr/bin" \
    "$package_root/usr/share/applications"

  cp -a publish/linux-x64/. "$package_root/opt/$PACKAGE_NAME/"
  rm -rf "$package_root/opt/$PACKAGE_NAME/tools"
  ln -s "/opt/$PACKAGE_NAME/$APP_NAME" "$package_root/usr/bin/$PACKAGE_NAME"

  cat >"$package_root/DEBIAN/control" <<EOF_CONTROL
Package: $PACKAGE_NAME
Version: $VERSION
Section: video
Priority: optional
Architecture: amd64
Maintainer: Anime App
Depends: mpv, ffmpeg, aria2, yt-dlp, libdbus-1-3, libfontconfig1, libfreetype6, libglib2.0-0, libharfbuzz0b, libice6, libsm6, libx11-6, libxcb1, libxcursor1, libxext6, libxi6, libxinerama1, libxkbcommon0, libxrandr2, libxrender1
Description: Desktop anime search and playback app
 Avalonia UI app for searching anime, selecting episodes, and playing or downloading streams.
EOF_CONTROL

  cat >"$package_root/usr/share/applications/$PACKAGE_NAME.desktop" <<EOF_DESKTOP
[Desktop Entry]
Type=Application
Name=Anime App
Exec=/opt/$PACKAGE_NAME/$APP_NAME
Terminal=false
Categories=AudioVideo;Network;
EOF_DESKTOP

  chmod 755 "$package_root/opt/$PACKAGE_NAME/$APP_NAME"
  chmod 755 "$package_root/usr/bin"
  dpkg-deb --root-owner-group --build "$package_root" "$output"
}

clean_artifacts() {
  rm -rf bin obj .avalonia-build-tasks
}

target="${1:-all}"

case "$target" in
  all)
    publish_target linux-x64 publish/linux-x64 false
    publish_target win-x64 publish/win-x64 true
    clean_artifacts
    ;;
  linux)
    publish_target linux-x64 publish/linux-x64 false
    clean_artifacts
    ;;
  windows | win)
    publish_target win-x64 publish/win-x64 true
    clean_artifacts
    ;;
  deb | linux-installer)
    package_deb
    clean_artifacts
    ;;
  clean)
    rm -rf publish dist
    clean_artifacts
    ;;
  -h | --help | help)
    usage
    ;;
  *)
    usage
    exit 1
    ;;
esac
