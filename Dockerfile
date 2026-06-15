# syntax=docker/dockerfile:1
#
# Discord Conduit — CLI bot container image.
#
# This image runs ONLY the CLI bot mode (`discordconduit bot start`) as a
# long-running, headless workload. The Avalonia desktop GUI is intentionally
# NOT containerized (it needs a display server and an OS credential store).
#
# The bot reads its token from the DISCORD_CONDUIT_TOKEN environment variable
# (or a file via `--token-file`); there is no OS credential store inside a
# container.

# ---------------------------------------------------------------------------
# Build stage — full SDK, restore + publish the CLI project only.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution + project files first and restore, so the (slow) restore layer
# is cached and only invalidated when a project/solution file changes.
COPY Discord-Conduit.slnx ./
COPY Directory.Build.props ./
COPY src/DustBowlGames.DiscordConduit.Core/DustBowlGames.DiscordConduit.Core.csproj src/DustBowlGames.DiscordConduit.Core/
COPY src/DustBowlGames.DiscordConduit.Cli/DustBowlGames.DiscordConduit.Cli.csproj src/DustBowlGames.DiscordConduit.Cli/

# Restore just the CLI project (it transitively pulls in Core). We deliberately
# do NOT restore/build the App (GUI) or Tests projects.
RUN dotnet restore src/DustBowlGames.DiscordConduit.Cli/DustBowlGames.DiscordConduit.Cli.csproj

# Copy the remaining source and publish. Framework-dependent publish is fine
# because the runtime stage already carries the .NET runtime.
COPY src/DustBowlGames.DiscordConduit.Core/ src/DustBowlGames.DiscordConduit.Core/
COPY src/DustBowlGames.DiscordConduit.Cli/ src/DustBowlGames.DiscordConduit.Cli/
RUN dotnet publish src/DustBowlGames.DiscordConduit.Cli/DustBowlGames.DiscordConduit.Cli.csproj \
        -c Release \
        --no-restore \
        -o /app/publish

# ---------------------------------------------------------------------------
# Runtime stage — console runtime only (NOT aspnet), non-root user.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

# OCI image metadata.
LABEL org.opencontainers.image.source="https://github.com/DustBowl-Games/Discord-Conduit" \
      org.opencontainers.image.title="Discord Conduit" \
      org.opencontainers.image.description="Headless Discord Conduit bot — moves Discord messages between channels and threads via slash commands." \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.vendor="DustBowl Games"

# Create a dedicated non-root user with a real, writable HOME. The app persists
# logs and migration state under Environment.SpecialFolder.ApplicationData,
# which on Linux resolves to $XDG_CONFIG_HOME or $HOME/.config. Without a
# writable HOME, Serilog file logging and migration-state writes would fail.
ENV HOME=/home/conduit
RUN useradd --create-home --home-dir ${HOME} --shell /usr/sbin/nologin --uid 10001 conduit \
    && mkdir -p ${HOME}/.config \
    && chown -R conduit:conduit ${HOME}

WORKDIR /app
COPY --from=build --chown=conduit:conduit /app/publish ./

# Documentation only: the token is supplied at run time via this env var or a
# mounted file (`--token-file`). Empty by default so an unconfigured container
# fails fast with a clear error instead of using a stale value.
ENV DISCORD_CONDUIT_TOKEN=""

USER conduit

# `bot start` (CMD) with no --profile reads the token from DISCORD_CONDUIT_TOKEN.
ENTRYPOINT ["dotnet", "discordconduit.dll"]
CMD ["bot", "start"]
