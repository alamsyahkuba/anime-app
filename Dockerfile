FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY AnimeApp.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish AnimeApp.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        aria2 \
        ca-certificates \
        ffmpeg \
        libdbus-1-3 \
        libfontconfig1 \
        libfreetype6 \
        libglib2.0-0 \
        libharfbuzz0b \
        libice6 \
        libsm6 \
        libx11-6 \
        libxcb1 \
        libxcursor1 \
        libxext6 \
        libxi6 \
        libxinerama1 \
        libxkbcommon0 \
        libxrandr2 \
        libxrender1 \
        mpv \
        yt-dlp \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "AnimeApp.dll"]
