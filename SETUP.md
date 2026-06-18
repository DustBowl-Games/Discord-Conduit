# Discord Conduit -- Setup Guide

Complete guide to setting up and using Discord Conduit for migrating messages between Discord channels and threads.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Creating a Discord Bot](#creating-a-discord-bot)
- [Installation](#installation)
- [CLI Quick Start](#cli-quick-start)
- [Desktop App Quick Start](#desktop-app-quick-start)
- [Bot Commands](#bot-commands)
- [Troubleshooting](#troubleshooting)
- [Known Limitations](#known-limitations)
- [Data Storage](#data-storage)

---

## Prerequisites

- **.NET 10 SDK** -- Download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0) (required only when building from source)
- **A Discord account** with administrator or sufficient permissions on the server(s) you want to migrate messages in
- **A Discord bot token** from the Discord Developer Portal (see next section)

---

## Creating a Discord Bot

Follow these steps to create a bot application and invite it to your server.

### Step 1: Create an Application

1. Go to the [Discord Developer Portal](https://discord.com/developers/applications).
2. Click **New Application**.
3. Name it something recognizable (e.g., "Discord Conduit").
4. Accept the Terms of Service and click **Create**.

### Step 2: Configure the Bot

1. In the left sidebar, click **Bot**.
2. Click **Add Bot** and confirm.
3. Under **Privileged Gateway Intents**, enable:
   - **Message Content Intent** -- required to read message content for migration
4. Click **Save Changes**.

### Step 3: Copy Your Bot Token

1. On the Bot page, click **Reset Token** (or **Copy** if the token is still visible).
2. Copy the token and store it somewhere safe temporarily. You will enter it into Discord Conduit's profile manager, which stores it securely in your OS credential store.
3. **Never share your bot token or commit it to source control.**

### Step 4: Generate an Invite URL

1. In the left sidebar, click **OAuth2**, then **URL Generator**.
2. Under **Scopes**, select:
   - `bot`
   - `applications.commands`
3. Under **Bot Permissions**, select:

   | Permission              | Why it's needed                                |
   |-------------------------|------------------------------------------------|
   | View Channels           | Browse available channels and threads           |
   | Read Message History    | Read messages from the source channel           |
   | Send Messages           | Post migrated messages to the destination        |
   | Manage Webhooks         | Create webhooks to spoof original author identity|
   | Add Reactions           | Re-add reactions to migrated messages            |

4. Copy the generated URL at the bottom of the page.

### Step 5: Invite the Bot

1. Open the generated URL in your browser.
2. Select the server you want to add the bot to.
3. Click **Authorize** and complete the CAPTCHA if prompted.
4. The bot should now appear in your server's member list.

**Important:** The bot needs the permissions listed above in *both* the source and destination channels. If your server uses per-channel permission overrides, verify the bot has access to both channels.

---

## Installation

### Download a Release (Recommended)

Download the latest release for your platform from the [GitHub Releases](https://github.com/DustBowl-Games/Discord-Conduit/releases) page:

- **Windows**: `discord-conduit-win-x64.tar.gz`
- **macOS**: `discord-conduit-osx-x64.tar.gz` (Intel) or `discord-conduit-osx-arm64.tar.gz` (Apple Silicon)
- **Linux**: `discord-conduit-linux-x64.tar.gz`

Extract the `.tar.gz` tarball and run the executable.

### Build from Source

```bash
git clone https://github.com/DustBowl-Games/Discord-Conduit.git
cd Discord-Conduit
dotnet restore
dotnet build
```

Run the desktop app:

```bash
dotnet run --project src/DustBowlGames.DiscordConduit.App
```

Run the CLI:

```bash
dotnet run --project src/DustBowlGames.DiscordConduit.Cli -- <command>
```

---

## CLI Quick Start

The CLI executable is `discordconduit` (or `dotnet run --project src/DustBowlGames.DiscordConduit.Cli --` when running from source).

### 1. Add a Bot Profile

Profiles store bot tokens securely in your OS credential store. You only need to do this once per bot.

```bash
discordconduit profile add mybot --token YOUR_BOT_TOKEN
```

#### Supplying the token securely

Passing `--token` on the command line exposes the token in your shell history and process listings. The CLI supports two safer alternatives, used automatically when `--token` is omitted:

- **Environment variable** — set `DISCORD_CONDUIT_TOKEN` and run `discordconduit profile add mybot`. The token is read from the variable.

  ```bash
  # Linux/macOS
  DISCORD_CONDUIT_TOKEN=YOUR_BOT_TOKEN discordconduit profile add mybot
  ```

  ```powershell
  # Windows PowerShell
  $env:DISCORD_CONDUIT_TOKEN = "YOUR_BOT_TOKEN"; discordconduit profile add mybot
  ```

- **Interactive masked prompt** — run `discordconduit profile add mybot` with no token and no environment variable. You will be prompted to enter the token; keystrokes are masked and never echoed. (When stdin is piped, the token is read as a single line, so `echo $TOKEN | discordconduit profile add mybot` also works.)

Resolution order is `--token` first, then `DISCORD_CONDUIT_TOKEN`, then the interactive prompt.

Verify the profile was saved:

```bash
discordconduit profile list
```

### 2. Validate Permissions

Before running a migration, verify the bot has the required permissions in both channels:

```bash
discordconduit validate --profile mybot --source SOURCE_CHANNEL_ID --dest DEST_CHANNEL_ID --guild GUILD_ID
```

This checks Read Message History on the source channel, and View Channel, Manage Webhooks, and Send Messages on the destination channel (the latter verified by creating and using a test webhook).

> **Note:** Add Reactions is required for reaction migration but is not verified by `validate`.

### 3. Run a Migration

```bash
discordconduit migrate --profile mybot --source SOURCE_CHANNEL_ID --dest DEST_CHANNEL_ID --guild GUILD_ID
```

The CLI will display a preview (message count, attachment sizes, warnings) and then begin migrating with a live progress indicator.

**Options:**

| Flag                 | Description                              |
|----------------------|------------------------------------------|
| `--dry-run`          | Validate the entire pipeline without posting any messages |
| `--no-reactions`     | Skip reaction migration                  |
| `--no-pins`          | Don't re-pin messages that were pinned in the source |
| `--no-polls`         | Don't re-create polls attached to messages |
| `--from-author <id>` | Only migrate messages from this author (user ID) |
| `--since <date>`     | Only migrate messages on/after this date/time (e.g. `2024-01-01` or `2024-01-01T12:00:00Z`) |
| `--until <date>`     | Only migrate messages on/before this date/time |
| `--contains <text>`  | Only migrate messages whose text contains this (case-insensitive) |
| `--attachments-only` | Only migrate messages that have attachments |
| `--no-bots`          | Exclude messages authored by bots        |

Filters combine with AND — e.g. `--from-author 123 --since 2024-06-01 --attachments-only` migrates only that user's attachment messages from June onward. The preview message count reflects the filtered set.

### 4. Resume an Interrupted Migration

If a migration is interrupted (network error, crash, Ctrl+C), resume it from where it left off:

```bash
discordconduit migrate resume STATE_FILE --profile mybot
```

Migration state files are saved to the migrations directory (see [Data Storage](#data-storage)) after each message is posted.

### 5. Export a Channel to a File

Export (archive) a channel's messages to a file without posting anything:

```bash
discordconduit export --profile mybot --channel CHANNEL_ID --format html --output archive.html
```

**Formats:** `html` (styled, human-readable; the default), `json` (structured, machine-readable), `csv` (tabular), `txt` (plain text). If `--output` is omitted, the file is named `export-<channel>.<ext>`.

The export accepts the same filter flags as `migrate` (`--from-author`, `--since`, `--until`, `--contains`, `--attachments-only`, `--no-bots`), so you can archive just a subset. Attachment links reference Discord's CDN (note: these URLs expire over time).

### 6. Clone a Channel or Category

Cloning **creates** the destination channel(s) and migrates messages into them, in one step. The destination can be a **different server** (cross-server) — the bot must be a member of both.

Clone a single channel into a server (optionally under a category):

```bash
discordconduit clone --profile mybot --source SOURCE_CHANNEL_ID --dest-guild DEST_GUILD_ID [--parent DEST_CATEGORY_ID]
```

Clone an entire category and all its text channels:

```bash
discordconduit clone --profile mybot --source SOURCE_CATEGORY_ID --dest-guild DEST_GUILD_ID --category
```

`clone` accepts the same `--dry-run`, `--no-reactions`, and filter flags as `migrate`. The bot needs **Manage Channels** in the destination server to create channels.

> **Cross-server with `migrate`:** the plain `migrate` command also works across servers — just give a `--source` and `--dest` in different servers (the bot must be in both). `clone` is the convenience wrapper that also creates the destination.

### 7. Start the Bot for Slash Commands

To enable the interactive slash commands (Move This, Move This & Below, /move-range, /move-thread):

```bash
discordconduit bot start --profile mybot
```

The bot connects to Discord's gateway, registers its commands, and listens for interactions. Press Ctrl+C to stop.

For headless or container use where the OS credential store is unavailable, `bot start` can also read the token from the `DISCORD_CONDUIT_TOKEN` environment variable or from a `--token-file <path>` (for example, a mounted Kubernetes secret) instead of `--profile`. See the [Deployment Guide](docs/DEPLOYMENT.md) for details.

---

## Desktop App Quick Start

1. **Launch the app** -- run the executable or `dotnet run --project src/DustBowlGames.DiscordConduit.App`.
2. **Add a profile** -- go to **Profiles** and click **Add**. Enter a name and paste your bot token.
3. **Browse channels** -- go to **Channel Browser**. The app shows your guilds, channels, and threads. Select a source channel on the left and a destination channel on the right.
4. **Preview** -- click **Preview** to see message counts, attachment sizes, oversized file warnings, and permission validation results.
5. **Start migration** -- click **Start Migration**. The app shows a live progress bar with ETA, pause/resume, and cancel controls.
6. **Resume** -- if a migration was interrupted, it appears in the migration history and can be resumed.

---

## Bot Commands

When the bot is running (`discordconduit bot start`), the following commands are available in Discord.

All move commands follow a multi-step flow: **select action** -> **choose destination** -> **confirm** -> **move** -> **delete or keep originals**.

### Move This (Context Menu)

Right-click a message -> **Apps** -> **Move This**

Moves a single message to the destination channel you select. Useful for relocating an off-topic message.

### Move This & Below (Context Menu)

Right-click a message -> **Apps** -> **Move This & Below**

Moves the right-clicked message and every message below it (to the end of the channel) to the destination. Useful for splitting a conversation.

### /move-range

```
/move-range start:<message_id> end:<message_id>
```

Moves a range of messages defined by their message IDs. To get a message ID, enable Developer Mode in Discord (User Settings -> Advanced -> Developer Mode), then right-click a message and select **Copy Message ID**.

### /move-thread

```
/move-thread thread:<channel>
```

Moves an entire thread or forum post to a destination channel. Select the thread from the channel picker.

---

## Troubleshooting

### "Missing Access" or "Missing Permissions"

The bot does not have the required permissions in one or both channels. Run `discordconduit validate` to see exactly which permissions are missing and in which channel. Check for per-channel permission overrides in Discord's channel settings.

### "Unknown Channel"

The bot cannot see the specified channel. This usually means the bot does not have View Channels permission. Verify the bot's role has access to both the source and destination channels.

### Rate Limiting

Discord rate limits are expected and handled automatically. The bot reads rate limit headers from Discord's responses and waits the required duration before retrying. No action is needed -- you may see brief pauses during migration.

### Migration Failed Mid-Way

Migration state is saved after each message. Use the resume command to pick up where it left off:

```bash
discordconduit migrate resume <state-file> --profile mybot
```

State files are located in the migrations directory (see [Data Storage](#data-storage)).

### Bot Commands Not Appearing

After starting the bot with `discordconduit bot start`, Discord may take a few minutes to propagate global slash commands. If commands still do not appear after 10 minutes, try:

1. Kicking and re-inviting the bot with the `applications.commands` scope.
2. Checking the console output for registration errors.

### Where Are Logs?

Logs are written to the console and to rolling log files:

| Platform | Log directory                                    |
|----------|--------------------------------------------------|
| Windows  | `%APPDATA%\DiscordConduit\logs\`                 |
| macOS    | `~/.config/DiscordConduit/logs/`                 |
| Linux    | `~/.config/DiscordConduit/logs/`                 |

Log files rotate daily and the last 7 days are retained.

---

## Known Limitations

- **Stickers cannot be migrated.** Discord's webhook API does not support sending stickers. The preview warns about messages containing stickers.
- **Original timestamps are not preserved.** Migrated messages show the time they were re-posted, not the original send time. This is a Discord API limitation.
- **Messages appear as webhook/app messages.** While the original author's username and avatar are spoofed via webhook, the messages show an "APP" tag. They are not true messages from the original user.
- **Webhook avatar spoofing may not work in all cases.** If the original author's avatar URL is expired or inaccessible, the webhook falls back to the default avatar.
- **Polls are re-created fresh.** A migrated poll keeps its question and answers, but votes and the original end time are not carried over (the API doesn't allow it) — the poll restarts with a new duration.
- **Button/select components are not preserved.** Interactive components have no backing handler in the destination, so they are not reproduced.
- **Pins have a 50-pin channel cap.** Pinned source messages are re-pinned in the destination, but Discord limits a channel to 50 pins; any beyond that are skipped (and logged).
- **Reactions are added as the bot.** Discord's API does not allow adding reactions on behalf of other users, so all migrated reactions show the bot as the reactor.
- **Attachment size limit.** Files larger than 25 MB are skipped and listed as warnings in the preview. Discord Conduit uses a fixed 25 MB cap and does not raise it for boosted servers.

---

## Data Storage

Discord Conduit stores data in platform-specific locations.

### Bot Tokens (Profiles)

Tokens are stored in your operating system's credential store -- never in plaintext files.

| Platform | Credential backend                               |
|----------|--------------------------------------------------|
| Windows  | Windows Credential Manager (DPAPI)               |
| macOS    | macOS Keychain                                   |
| Linux    | libsecret (GNOME Keyring / KDE Wallet)           |

### Migration State

State files are saved after each message for resumability:

| Platform | Directory                                        |
|----------|--------------------------------------------------|
| Windows  | `%APPDATA%\DiscordConduit\migrations\`           |
| macOS    | `~/.config/DiscordConduit/migrations/`           |
| Linux    | `~/.config/DiscordConduit/migrations/`           |

Each migration gets a JSON state file containing the source/destination IDs, the last successfully migrated message, and a mapping of old-to-new message IDs.

### Logs

| Platform | Directory                                        |
|----------|--------------------------------------------------|
| Windows  | `%APPDATA%\DiscordConduit\logs\`                 |
| macOS    | `~/.config/DiscordConduit/logs/`                 |
| Linux    | `~/.config/DiscordConduit/logs/`                 |

Logs rotate daily with a 7-day retention policy.
