# Discord Conduit

A cross-platform desktop application that migrates Discord messages between channels and threads while preserving the original poster's identity.

## What it does

Discord Conduit re-posts messages from a source channel or thread to a destination channel or thread using webhooks, spoofing the original author's username and avatar so migrated messages look like the original poster sent them. Attachments are re-uploaded (not linked), and reactions are preserved.

## Features

- **Bot profile management** — securely store multiple bot tokens in your OS credential store
- **Channel browser** — browse guilds, channels, and threads with side-by-side source/destination selection
- **Migration preview** — see message counts, attachment sizes, oversized file warnings, and permission checks before starting
- **Message migration** — re-post messages via webhook with original author identity preserved
- **Attachment re-upload** — download and re-upload files (Discord CDN links expire)
- **Reaction migration** — best-effort reaction preservation (added as bot, not original users)
- **Reply references** — reply chains include a snippet of the referenced message
- **Resumable migrations** — state persisted after each message, resume interrupted migrations
- **Dry-run mode** — validate the entire pipeline without posting
- **Progress tracking** — live progress bar, ETA, pause/resume, cancel
- **CLI companion** — run migrations from the command line

## Requirements

- A Discord bot token with the following permissions in both source and destination:
  - `READ_MESSAGE_HISTORY`
  - `MANAGE_WEBHOOKS`
  - `SEND_MESSAGES`
  - `ADD_REACTIONS` (if migrating reactions)

## Building from source

```bash
dotnet restore
dotnet build
dotnet run --project src/DustBowlGames.DiscordConduit.App
```

## License

MIT License. See [LICENSE](LICENSE).

---

Maintained by [DustBowl Games](https://github.com/DustBowlGames).
