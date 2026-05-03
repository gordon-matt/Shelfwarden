# Shelfwarden web container — SDK and runtime use the same 10.0-noble (glibc) image family.

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS base
WORKDIR /app
EXPOSE 8080
# Library, cover and database storage all live under /app/data so the host can mount one volume.
RUN mkdir -p /app/data/library /app/data/covers /app/data/db /app/data/logs /app/data/tts-cache \
    && apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Shelfwarden/Shelfwarden.csproj", "Shelfwarden/"]
COPY ["Shelfwarden.Core/Shelfwarden.Core.csproj", "Shelfwarden.Core/"]
COPY ["Shelfwarden.Data/Shelfwarden.Data.csproj", "Shelfwarden.Data/"]
COPY ["Shelfwarden.Data.Sqlite/Shelfwarden.Data.Sqlite.csproj", "Shelfwarden.Data.Sqlite/"]
COPY ["Shelfwarden.Data.Sql/Shelfwarden.Data.Sql.csproj", "Shelfwarden.Data.Sql/"]
COPY ["Shelfwarden.Data.Npgsql/Shelfwarden.Data.Npgsql.csproj", "Shelfwarden.Data.Npgsql/"]
COPY ["Shelfwarden.Data.MySql/Shelfwarden.Data.MySql.csproj", "Shelfwarden.Data.MySql/"]
COPY ["Shelfwarden.Models/Shelfwarden.Models.csproj", "Shelfwarden.Models/"]
COPY ["Shelfwarden.Services/Shelfwarden.Services.csproj", "Shelfwarden.Services/"]
RUN dotnet restore "./Shelfwarden/Shelfwarden.csproj"
COPY . .
WORKDIR "/src/Shelfwarden"
# Restore client-side libraries (wwwroot/lib) since they are not in source control.
RUN dotnet tool install -g Microsoft.Web.LibraryManager.Cli --version 2.1.113 && \
    export PATH="${PATH}:/root/.dotnet/tools" && \
    libman restore --verbose
RUN dotnet build "./Shelfwarden.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Shelfwarden.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
# Publish can omit content added after restore — make sure LibMan output ships.
RUN cp -r /src/Shelfwarden/wwwroot/lib /app/publish/wwwroot/ || true
# Scoped CSS (Razor isolated styles) may land at publish root; mirror into wwwroot.
RUN if [ -f /app/publish/Shelfwarden.styles.css ]; then cp /app/publish/Shelfwarden.styles.css /app/publish/wwwroot/; fi

FROM base AS final
COPY --from=publish /app/publish .

# Same URL as <see cref="KokoroEngineProvider"/> — bake once so TTS does not download ~320 MB at runtime.
ARG KOKORO_MODEL_URL=https://github.com/taylorchu/kokoro-onnx/releases/download/v0.2.0/kokoro.onnx
RUN curl -fsSL "$KOKORO_MODEL_URL" -o /app/data/tts-cache/kokoro.onnx

ENTRYPOINT ["dotnet", "Shelfwarden.dll"]
