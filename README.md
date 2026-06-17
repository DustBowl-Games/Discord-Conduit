# Discord Conduit

[![CI](https://github.com/DustBowl-Games/Discord-Conduit/actions/workflows/ci.yml/badge.svg)](https://github.com/DustBowl-Games/Discord-Conduit/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Latest Release](https://img.shields.io/github/v/release/DustBowl-Games/Discord-Conduit)](https://github.com/DustBowl-Games/Discord-Conduit/releases)

A cross-platform desktop application that migrates Discord messages between channels and threads while preserving the original poster's identity.

> **Getting started?** See the [Setup Guide](SETUP.md) for step-by-step instructions on creating a bot, configuring permissions, and running your first migration.

## What it does

Discord Conduit re-posts messages from a source channel or thread to a destination channel or thread via webhooks using the original author's username and avatar, so migrated messages keep the original poster's identity. Attachments are re-uploaded (not linked), and reactions are preserved.

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

- .NET 10 SDK (for building from source)
- A Discord bot token (see [Setup Guide](SETUP.md#creating-a-discord-bot) for how to create one)

### Required Bot Permissions

The bot needs these permissions in **both** the source and destination channels:

| Permission              | Required for                                    |
|-------------------------|-------------------------------------------------|
| View Channels           | Browsing available channels and threads          |
| Read Message History    | Reading messages from the source channel         |
| Send Messages           | Posting migrated messages to the destination     |
| Manage Webhooks         | Creating webhooks to preserve author identity    |
| Add Reactions           | Re-adding reactions to migrated messages          |

**OAuth2 Scopes:** `bot`, `applications.commands`

## Installation

### Download a Release

Download the latest build for your platform from [GitHub Releases](https://github.com/DustBowl-Games/Discord-Conduit/releases).

### Build from Source

```bash
git clone https://github.com/DustBowl-Games/Discord-Conduit.git
cd Discord-Conduit
dotnet restore
dotnet build
dotnet run --project src/DustBowlGames.DiscordConduit.App
```

For detailed setup instructions including bot creation, CLI usage, and troubleshooting, see the [Setup Guide](SETUP.md).

### Running as a bot service (Docker)

The CLI can run the bot long-lived (`discordconduit bot start`) to serve the slash and context-menu commands. For running it as a container or deploying to Kubernetes via Helm, see the [Deployment Guide](./docs/DEPLOYMENT.md).

## License

MIT License. See [LICENSE](LICENSE).

---

Maintained by [DustBowl Games](https://github.com/DustBowl-Games).
