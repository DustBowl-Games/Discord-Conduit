# Discord Conduit — Implementation Plan

## Context

Discord Conduit is a new cross-platform desktop application that migrates Discord messages between channels and threads while preserving the original poster's identity. Messages are reposted via webhook with the original author's username and avatar — no header or timestamp injection. This matches the clean Pippin-style migration format the user prefers.

The repo is empty. This plan covers solution structure, architecture, Core API design, state management, rate limiting, testing, CI/CD, packaging, and a vertical-slice build order.

> **Note:** The 8 MB attachment limit referenced throughout this plan was later raised to a fixed 25 MB cap (see CHANGELOG).

---

## 1. Solution & Project Structure

```
Discord-Conduit/
├── Discord-Conduit.sln
├── src/
│   ├── DustBowlGames.DiscordConduit.Core/          # Class library, zero UI deps
│   │   ├── Api/                                      # Discord REST client, rate limiter
│   │   │   ├── DiscordRestClient.cs                  # Thin HttpClient wrapper
│   │   │   ├── RateLimiter.cs                        # Centralized rate limit handler
│   │   │   ├── Endpoints/                            # Per-resource endpoint classes
│   │   │   │   ├── ChannelEndpoints.cs
│   │   │   │   ├── GuildEndpoints.cs
│   │   │   │   ├── MessageEndpoints.cs
│   │   │   │   ├── WebhookEndpoints.cs
│   │   │   │   └── ReactionEndpoints.cs
│   │   │   └── Models/                               # Discord API DTOs (JSON-serializable)
│   │   │       ├── Guild.cs
│   │   │       ├── Channel.cs
│   │   │       ├── Message.cs
│   │   │       ├── User.cs
│   │   │       ├── Webhook.cs
│   │   │       ├── Attachment.cs
│   │   │       ├── Embed.cs
│   │   │       └── Reaction.cs
│   │   ├── Migration/                                # Migration engine
│   │   │   ├── MigrationEngine.cs                    # Orchestrator — the main entry point
│   │   │   ├── MigrationOptions.cs                   # Config record for a migration run
│   │   │   ├── MigrationState.cs                     # Serializable state for resume
│   │   │   ├── MigrationProgress.cs                  # Progress reporting model
│   │   │   ├── MigrationResult.cs                    # Final result summary
│   │   │   ├── MessageMigrator.cs                    # Single-message migration logic
│   │   │   └── AttachmentHandler.cs                  # Download + re-upload logic
│   │   ├── Credentials/                              # OS credential store abstraction
│   │   │   ├── ICredentialStore.cs
│   │   │   ├── WindowsCredentialStore.cs             # DPAPI / Windows Credential Manager
│   │   │   ├── MacOsCredentialStore.cs               # macOS Keychain via security CLI
│   │   │   └── LinuxCredentialStore.cs               # libsecret via secret-tool CLI
│   │   ├── Profiles/                                 # Bot profile management
│   │   │   ├── BotProfile.cs
│   │   │   └── ProfileManager.cs
│   │   └── Validation/                               # Pre-flight checks
│   │       └── PermissionValidator.cs
│   ├── DustBowlGames.DiscordConduit.App/             # Avalonia UI (GUI)
│   │   ├── App.axaml / App.axaml.cs
│   │   ├── Program.cs
│   │   ├── ViewModels/
│   │   │   ├── MainWindowViewModel.cs
│   │   │   ├── ProfileManagerViewModel.cs
│   │   │   ├── ChannelBrowserViewModel.cs
│   │   │   ├── MigrationPreviewViewModel.cs
│   │   │   └── MigrationProgressViewModel.cs
│   │   ├── Views/
│   │   │   ├── MainWindow.axaml
│   │   │   ├── ProfileManagerView.axaml
│   │   │   ├── ChannelBrowserView.axaml
│   │   │   ├── MigrationPreviewView.axaml
│   │   │   └── MigrationProgressView.axaml
│   │   ├── Services/                                 # UI-side service wiring
│   │   │   └── ServiceCollectionExtensions.cs
│   │   └── Assets/
│   │       └── icon.ico
│   ├── DustBowlGames.DiscordConduit.Cli/             # CLI companion
│   │   ├── Program.cs
│   │   └── Commands/
│   │       ├── MigrateCommand.cs
│   │       ├── ProfileCommand.cs
│   │       └── ValidateCommand.cs
│   └── DustBowlGames.DiscordConduit.Core.Tests/      # xUnit tests for Core
│       ├── Api/
│       │   ├── RateLimiterTests.cs
│       │   └── DiscordRestClientTests.cs
│       ├── Migration/
│       │   ├── MigrationEngineTests.cs
│       │   ├── MessageMigratorTests.cs
│       │   └── AttachmentHandlerTests.cs
│       └── Fixtures/
│           └── FakeHttpHandler.cs                    # Custom HttpMessageHandler for mocking
├── docs/
│   └── PLAN.md                                       # This plan (also committed to repo)
├── .github/
│   └── workflows/
│       ├── ci.yml                                    # Build + test on PR
│       └── release.yml                               # Build + package + Velopack publish on tag
├── .gitignore
├── LICENSE                                           # MIT
├── README.md
└── CLAUDE.md
```

**Key namespaces:**
- `DustBowlGames.DiscordConduit.Core.Api` — REST client, rate limiter, DTOs
- `DustBowlGames.DiscordConduit.Core.Migration` — engine, state, progress
- `DustBowlGames.DiscordConduit.Core.Credentials` — OS keychain abstraction
- `DustBowlGames.DiscordConduit.Core.Profiles` — bot profile CRUD
- `DustBowlGames.DiscordConduit.App.ViewModels` / `.Views` — MVVM UI

---

## 2. Core Library API Surface

The Core library exposes migration as an async, cancellable, progress-reporting operation:

```csharp
// Main entry point
public class MigrationEngine
{
    public MigrationEngine(DiscordRestClient client, ICredentialStore credentials, ILogger logger);

    /// Pre-flight: fetch message count, attachment sizes, permission checks
    public Task<MigrationPreview> PreviewAsync(MigrationOptions options, CancellationToken ct);

    /// Run migration with progress reporting
    public Task<MigrationResult> RunAsync(
        MigrationOptions options,
        IProgress<MigrationProgress> progress,
        CancellationToken ct);

    /// Resume an interrupted migration from state file
    public Task<MigrationResult> ResumeAsync(
        MigrationState state,
        IProgress<MigrationProgress> progress,
        CancellationToken ct);
}

public record MigrationOptions(
    ulong SourceChannelId,
    ulong DestinationChannelId,
    ulong GuildId,
    bool DryRun = false,
    bool IncludeReactions = true);

public record MigrationPreview(
    int MessageCount,
    int AttachmentCount,
    long TotalAttachmentBytes,
    List<OversizedAttachment> OversizedAttachments,  // > 8MB, for user review
    TimeSpan EstimatedDuration,
    List<string> Warnings,                            // Missing perms, etc.
    PermissionCheckResult Permissions);

public record MigrationProgress(
    int Completed,
    int Total,
    int Failed,
    int Skipped,
    string? CurrentMessagePreview,
    TimeSpan Elapsed,
    TimeSpan? EstimatedRemaining,
    MigrationPhase Phase);                            // Messages, Reactions, Done

public enum MigrationPhase { FetchingPreview, MigratingMessages, MigratingReactions, Complete }
```

### Message posting format

Messages are posted via webhook with **no header text added**. The webhook `username` is set to the original author's display name, and `avatar_url` to their current avatar CDN URL. The message `content` is the original content as-is.

For **reply references**, a line is prepended:
```
↩ replying to @OriginalUser: "First 100 chars of the replied-to message..."
```
This is the only text we add to the original content.

---

## 3. Migration State File Schema

Stored at `{AppData}/DiscordConduit/migrations/{migrationId}.json`

Migration ID format: `{sourceId}-{destId}-{unix_timestamp}`

```json
{
  "version": 1,
  "migrationId": "123456789-987654321-1712700000",
  "sourceChannelId": "123456789",
  "destinationChannelId": "987654321",
  "guildId": "111222333",
  "webhookId": "444555666",
  "webhookToken": "webhook-token-here",
  "startedAt": "2026-04-10T12:00:00Z",
  "lastUpdatedAt": "2026-04-10T12:05:30Z",
  "totalMessageCount": 1500,
  "lastSuccessfulSourceMessageId": "777888999",
  "migratedCount": 342,
  "phase": "MigratingMessages",
  "messageIdMap": {
    "original_id_1": "reposted_id_1",
    "original_id_2": "reposted_id_2"
  },
  "failedMessages": [
    {
      "sourceMessageId": "111000111",
      "reason": "Attachment exceeded 8MB limit (12.3 MB)",
      "timestamp": "2026-04-10T12:03:12Z"
    }
  ],
  "oversizedAttachments": [
    {
      "sourceMessageId": "111000111",
      "filename": "large_video.mp4",
      "sizeBytes": 12902400,
      "userAction": "pending"
    }
  ],
  "options": {
    "dryRun": false,
    "includeReactions": true
  }
}
```

The `messageIdMap` serves two purposes:
1. Resume: skip already-migrated messages
2. Reply references: look up the reposted message ID for reply-to chains (future use if we want to link to the reposted message)

---

## 4. Rate Limit Architecture

All HTTP calls go through `DiscordRestClient`, which wraps a single `HttpClient` and delegates to `RateLimiter`.

```
DiscordRestClient
  └── SendAsync(HttpRequestMessage, bucketKey)
        └── RateLimiter.ExecuteAsync(bucketKey, request)
              ├── Check per-bucket remaining count
              ├── If remaining == 0, delay until reset time
              ├── Execute request
              ├── Read X-RateLimit-Remaining, X-RateLimit-Reset-After from response
              ├── Update bucket state
              └── On 429: read retry_after from body, delay exactly that, retry once
```

**Bucket keys:** Discord rate limits are per-route. Keys are derived from the HTTP method + path template (e.g., `POST:/channels/{id}/messages`). The `X-RateLimit-Bucket` header from responses is used to map routes to shared buckets.

**Global rate limit:** Discord also has a global 50 req/s limit. The rate limiter tracks a global semaphore alongside per-bucket state.

**No hard-coded sleeps.** All delays are derived from response headers. The only configurable value is an optional inter-message delay floor (default 0) for users who want to throttle below the API limit.

---

## 5. Testing Strategy

### Unit tests (xUnit, in Core.Tests)

**What gets unit tested:**
- `RateLimiter` — bucket tracking, delay calculation, 429 retry logic. Uses a fake `TimeProvider` to avoid real sleeps.
- `MessageMigrator` — message transformation logic (reply reference formatting, attachment handling decisions, embed passthrough).
- `AttachmentHandler` — size checking, download/re-upload flow. Mocked HttpClient via `FakeHttpHandler`.
- `MigrationEngine` — orchestration logic (pagination, state persistence, skip/continue on failure, resume from state). Full mock of `DiscordRestClient`.
- `MigrationState` — serialization/deserialization roundtrip, schema version handling.
- `ProfileManager` — CRUD operations with a mocked `ICredentialStore`.
- `PermissionValidator` — correct permission bit checks against mock guild/channel data.

**How Discord API is mocked:**
- Custom `FakeHttpHandler : DelegatingHandler` that intercepts requests by URL pattern and returns pre-built JSON responses.
- Fixture JSON files for common payloads (guild list, channel list, message page, etc.).
- The `DiscordRestClient` accepts an `HttpClient` in its constructor, making it trivially testable.

### Integration tests (out of v1, but designed for)
- Hit a real Discord test server with a dedicated bot.
- Not run in CI — manual or separate workflow with secrets.
- The Core API's constructor injection of `HttpClient` and `ICredentialStore` makes this straightforward.

### What does NOT get tested
- Avalonia UI views (not worth the complexity for v1)
- OS-specific credential stores (tested manually on each platform)

---

## 6. Packaging & Distribution

> **Planned / not implemented in v1.** The Velopack auto-update and installer approach described below was **deferred and never built** for v1. Releases are currently plain self-contained `dotnet publish` builds per RID, packaged as `.tar.gz` archives and attached to GitHub Releases — there is no auto-update, no installer, and no Velopack dependency. The content below is retained as historical design context.

### Velopack

**Why adopt it now:**
- Handles cross-platform auto-update out of the box (Windows, macOS, Linux)
- Produces self-contained single-file executables
- Update checking is ~20 lines of code in App startup
- GitHub Releases as the update source — no server needed
- Active, well-maintained, designed for .NET desktop apps

**Tradeoffs:**
- Adds a NuGet dependency (`Velopack`)
- macOS code signing/notarization still requires an Apple Developer account (Velopack doesn't remove this need, just integrates with it). For v1, unsigned macOS builds are fine for direct downloads.
- Linux auto-update via AppImage works but is less polished than Windows/macOS

**Installer formats:**
| Platform | Format | Auto-update |
|----------|--------|-------------|
| Windows  | Velopack Setup.exe (NSIS-based) | Yes |
| macOS    | .app in .zip (unsigned for v1) | Yes |
| Linux    | AppImage | Yes |

### CI/CD (GitHub Actions)

**`ci.yml`** — runs on every PR and push to `main`:
1. `dotnet restore`
2. `dotnet build`
3. `dotnet test`
4. (No packaging)

**`release.yml`** — runs on version tags (`v*`):
1. Build + test
2. `dotnet publish` for all 3 RIDs: `win-x64`, `osx-x64`, `linux-x64` (add `osx-arm64` for Apple Silicon)
3. `vpk pack` (Velopack CLI) to produce installers/update packages
4. Create GitHub Release, upload all artifacts
5. Velopack update feed points at GitHub Releases

**Versioning:** SemVer. Version comes from the git tag. Use `MinVer` or `GitVersion` NuGet package to derive version from tags automatically.

---

## 7. Async Model & UI Threading

### How long-running migrations don't freeze the UI

The `MigrationEngine.RunAsync` method is fully async and accepts:
- `IProgress<MigrationProgress>` — the engine calls `progress.Report()` after each message. Avalonia's `Progress<T>` marshals the callback to the UI thread automatically.
- `CancellationToken` — wired to the Cancel button. The engine checks `ct.ThrowIfCancellationRequested()` between messages.

**Pause/Resume:** Implemented via a `PauseTokenSource` (a simple `SemaphoreSlim(1,1)` wrapper). The engine awaits `pauseToken.WaitIfPausedAsync()` between messages. The UI toggles it.

**ViewModel pattern:**
```csharp
// In MigrationProgressViewModel
[RelayCommand]
private async Task StartMigrationAsync()
{
    _cts = new CancellationTokenSource();
    var progress = new Progress<MigrationProgress>(p =>
    {
        Completed = p.Completed;
        Total = p.Total;
        // etc — these are [ObservableProperty] fields, UI auto-updates
    });

    Result = await _engine.RunAsync(_options, progress, _cts.Token);
}

[RelayCommand]
private void Cancel() => _cts?.Cancel();

[RelayCommand]
private void TogglePause() => _pauseSource.Toggle();
```

---

## 8. Error Surfacing

**Principle:** Errors should be informative but not alarming. The app is a tool for technical Discord admins, not end consumers.

**Per-message failures:** Logged, added to `failedMessages` in state, migration continues. The progress UI shows a running "failed" count. No popup per failure.

**Migration summary:** At completion, a summary view shows:
- Total migrated / failed / skipped
- Expandable list of failed messages with reasons
- Retry button for failed messages (re-runs just those)
- Export failed list to CSV for manual review

**Pre-flight warnings:** Shown in the preview screen as a warning list (yellow, not red). Examples:
- "Bot is missing READ_MESSAGE_HISTORY on #source-channel"
- "3 attachments exceed 8MB and will be skipped"
- "Source channel contains 12,000 messages — estimated migration time: ~2 hours"

**Fatal errors** (lost network, bot token revoked, webhook deleted mid-migration): State is persisted, error dialog with "Resume later" option. Not a crash — graceful stop.

---

## 9. Reaction Migration

Reactions are migrated in a **second pass** after all messages are posted. This is because:
1. We need the `messageIdMap` to know the reposted message IDs to add reactions to
2. Reaction API calls are heavily rate-limited (1 req per reaction per message)
3. Separating phases means a crash during reactions doesn't lose message progress

**Process:**
1. During message fetch, collect reactions per message (the message payload includes reaction emoji + count, but not who reacted)
2. For each message with reactions, call `GET /channels/{id}/messages/{id}/reactions/{emoji}` to get the list of users who reacted
3. For each reaction, call `PUT /channels/{id}/messages/{reposted_id}/reactions/{emoji}/@me` to add the bot's reaction
4. **Limitation:** The bot adds all reactions as itself — there's no way to add reactions as other users. This means reactions show as "BotName reacted" rather than the original users. This is a Discord API limitation and should be documented clearly in the UI preview.

**User's decision point:** The preview should warn about this limitation and let the user opt out of reaction migration if the bot-attribution is unacceptable.

---

## 10. CLI Companion

`DustBowlGames.DiscordConduit.Cli` using `System.CommandLine`:

```
discordconduit profile add <name> --token <token>
discordconduit profile list
discordconduit profile remove <name>
discordconduit validate --profile <name> --source <channel_id> --dest <channel_id>
discordconduit migrate --profile <name> --source <channel_id> --dest <channel_id> [--dry-run] [--no-reactions]
discordconduit migrate resume <state_file>
```

Same Core library, same state files, same credential store. The CLI is a thin shell.

---

## 11. Build Order (Vertical Slices)

### Slice 1: Foundation
- Solution scaffold, project structure, .gitignore, LICENSE, README stub
- `DiscordRestClient` + `RateLimiter` with unit tests
- `FakeHttpHandler` test fixture
- Basic API models (Guild, Channel, Message, User)
- CI workflow (build + test)

### Slice 2: Bot Profile + Channel Browser
- `ICredentialStore` + Windows implementation (other platforms stubbed)
- `ProfileManager` with tests
- Avalonia app scaffold with FluentAvalonia
- Profile management view (add/remove/select)
- Guild → Channel → Thread tree fetch via API
- Channel browser view (side-by-side source/destination panels)

### Slice 3: Minimal End-to-End Migration
- `MigrationEngine.RunAsync` — fetch 100 messages, repost via webhook, no attachments, no reactions
- Auto webhook creation/cleanup
- State file persistence after each message
- Basic progress view (count, cancel button)
- **Milestone: can run and watch 100 messages migrate**

### Slice 4: Full Message Migration
- Attachment download + re-upload
- Oversized attachment detection and preview flagging
- Reply reference formatting
- Embed passthrough (bot embeds best-effort)
- Resume from interrupted state
- Pause/resume in UI

### Slice 5: Reactions + Preview + Validation
- Reaction migration (second pass)
- `MigrationPreview` with message count, attachment summary, oversized list, ETA
- `PermissionValidator` — pre-flight permission checks
- Preview view with warnings and oversized attachment review
- Dry-run mode

### Slice 6: Polish + Error Handling
- Migration summary view with retry for failed messages
- Per-message error resilience (403, oversized, malformed)
- macOS + Linux credential store implementations
- Serilog file logging setup

### Slice 7: CLI + Packaging
- CLI project with System.CommandLine
- Velopack integration (auto-update check on startup)
- GitHub Actions release workflow
- Platform builds (win-x64, osx-x64, osx-arm64, linux-x64)

### Slice 8: Final
- README with usage docs, screenshots, limitations
- About dialog (DustBowl.Games attribution, MIT license, version)
- CLAUDE.md with project conventions for future development
- Manual testing on all 3 platforms

---

## Avalonia-Specific Notes

Since this is your first Avalonia project, some gotchas to be aware of:

1. **No `Dispatcher.Invoke` like WPF.** Avalonia uses `Dispatcher.UIThread.Post()` or `Dispatcher.UIThread.InvokeAsync()`, but `IProgress<T>` handles this for you in most cases.
2. **FluentAvalonia** provides the Windows 11 / Fluent Design look. It's an Avalonia-native theme, not a WPF port. TreeView, NavigationView, InfoBar (for warnings) are all available.
3. **AXAML is not XAML.** Almost identical syntax but some binding differences. `{Binding}` works, `{x:Bind}` does not exist. Use `CompiledBindings` for type safety.
4. **No `WindowChrome` like WPF.** FluentAvalonia provides its own window customization via `FluentWindow`.
5. **Linux rendering:** Avalonia defaults to X11. Wayland support exists but may have issues. We'll default to X11 and let users opt in to Wayland via env var.
6. **Publish as self-contained.** Avalonia apps on Linux need .NET bundled — don't assume the runtime is installed.
