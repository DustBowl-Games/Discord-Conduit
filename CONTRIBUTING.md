# Contributing to Discord Conduit

Thanks for your interest in improving Discord Conduit! This document covers how to build, test, and submit changes.

## Code of Conduct

This project follows the [Contributor Covenant](CODE_OF_CONDUCT.md). By participating, you agree to uphold it. Report unacceptable behavior to security@dustbowl.games.

## Prerequisites

- **.NET 10 SDK** — install from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0).
- A GitHub account and a fork of the repository.

The solution targets `net10.0` across all projects.

## Building and testing

```bash
git clone https://github.com/DustBowl-Games/Discord-Conduit.git
cd Discord-Conduit
dotnet restore
dotnet build
dotnet test
```

Restore uses lock files (`packages.lock.json`). CI runs `dotnet restore --locked-mode`, so if you add, remove, or update a package, regenerate the lock files (a normal `dotnet restore` updates them) and commit the changes.

### Running the apps

Desktop app:

```bash
dotnet run --project src/DustBowlGames.DiscordConduit.App
```

CLI:

```bash
dotnet run --project src/DustBowlGames.DiscordConduit.Cli -- <command>
# e.g.
dotnet run --project src/DustBowlGames.DiscordConduit.Cli -- profile list
```

See [SETUP.md](SETUP.md) for the full CLI reference and how to create a bot.

## Project layout

| Project | Purpose |
|---------|---------|
| `DustBowlGames.DiscordConduit.Core` | All Discord logic. Zero UI dependencies. Published as a NuGet package. |
| `DustBowlGames.DiscordConduit.App` | Avalonia desktop UI (FluentAvalonia, CommunityToolkit.Mvvm). |
| `DustBowlGames.DiscordConduit.Cli` | CLI companion (System.CommandLine). |
| `DustBowlGames.DiscordConduit.Core.Tests` | xUnit tests for Core. |

## Coding conventions

- **Root namespace:** `DustBowlGames.DiscordConduit`.
- **Target framework:** `net10.0`.
- **Raw REST only.** No Discord.Net or DSharpPlus. All Discord API access uses the raw REST client.
- **Route all Discord calls through `DiscordRestClient`**, which routes through `RateLimiter`. Never call `HttpClient` directly for the Discord API. Rate-limit delays come from Discord response headers — no hard-coded sleeps.
- **API DTOs** live in `Core/Api/Models/` and use `System.Text.Json` with `[JsonPropertyName]` attributes.
- **XML doc comments are required on public Core types.** Core builds with `TreatWarningsAsErrors` + `GenerateDocumentationFile`, so a missing doc comment fails the build.
- **Secrets are stored in the OS credential store**, never in plaintext files. Never log tokens.
- **Branding:** use "Discord Conduit" in user-facing copy. "DustBowl Games" appears only in the LICENSE, README footer, and About dialog.

Shared MSBuild properties (version, authors, repository URL, deterministic build settings) live in the root `Directory.Build.props`. Per-project settings (`TreatWarningsAsErrors`, `Nullable`, `OutputType`) stay in each `.csproj`.

## Branching and pull requests

1. Create a topic branch off `main` (e.g. `fix/reaction-pagination`, `feat/move-thread`). Don't commit directly to `main`.
2. Keep changes focused; one logical change per PR.
3. Add or update tests for behavior changes. `dotnet test` must pass.
4. Update documentation (README, SETUP, CHANGELOG) when behavior or usage changes.
5. Write clear commit messages. Conventional-style prefixes (`feat:`, `fix:`, `docs:`, `chore:`) are appreciated.
6. Open a PR against `main` and fill out the pull request template. CI must be green before merge.

## Reporting bugs and requesting features

Use the [issue templates](https://github.com/DustBowl-Games/Discord-Conduit/issues/new/choose). For **security vulnerabilities, do not open a public issue** — follow [SECURITY.md](SECURITY.md) instead.
