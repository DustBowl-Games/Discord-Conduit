# Discord Conduit — Development Guide

## Project structure

- `src/DustBowlGames.DiscordConduit.Core/` — Class library with all Discord logic. Zero UI dependencies. Designed as a future NuGet package.
- `src/DustBowlGames.DiscordConduit.App/` — Avalonia UI desktop application (FluentAvalonia theme, CommunityToolkit.Mvvm).
- `src/DustBowlGames.DiscordConduit.Cli/` — CLI companion using System.CommandLine.
- `src/DustBowlGames.DiscordConduit.Core.Tests/` — xUnit tests for Core.

## Build and test

```bash
dotnet build
dotnet test
```

## Conventions

- Root namespace: `DustBowlGames.DiscordConduit`
- Target framework: `net10.0`
- All Discord API calls go through `DiscordRestClient` which routes through `RateLimiter`. Never call `HttpClient` directly for Discord API.
- Public types in Core require XML doc comments (enforced by `TreatWarningsAsErrors` + `GenerateDocumentationFile`).
- API DTOs live in `Core/Api/Models/` and use `System.Text.Json` with `[JsonPropertyName]` attributes.
- No Discord.Net or DSharpPlus — raw REST only.
- Bot tokens stored in OS credential store, never plaintext.
- Branding: "Discord Conduit" in user-facing copy. "DustBowl Games" only in LICENSE and README footer.

## Bot commands

- **Move This** (context menu) — moves the single right-clicked message.
- **Move This & Below** (context menu) — moves from the right-clicked message to the end of the channel.
- **/move-range** — slash command with start/end message IDs.
- **/move-thread** — slash command to move an entire thread or forum post.
- Multi-step flow: action select -> destination -> confirmation -> move -> delete/keep.

## Architecture decisions

- Rate limiting is centralized in `RateLimiter`. All delays come from Discord response headers, no hard-coded sleeps.
- Migration state is persisted to `{AppData}/DiscordConduit/migrations/` as JSON after each message for resumability.
- Messages are posted via webhook with original author's username/avatar. No header text is added to message content.
- Reactions are migrated in a second pass after all messages are posted.
- The Core library exposes `MigrationEngine` as the main entry point with `IProgress<T>` for progress and `CancellationToken` for cancellation.
- Gateway client connects via WebSocket for slash command support. Supports IDENTIFY, RESUME, heartbeat, and reconnection with exponential backoff.
- Stickers cannot be migrated via webhook. Preview warns about sticker-containing messages.
