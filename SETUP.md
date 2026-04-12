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
   - **Server Members Intent** -- enable this if you plan to use member-related features
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

- **Windows**: `DiscordConduit-win-x64.zip`
- **macOS**: `DiscordConduit-osx-x64.zip` (Intel) or `DiscordConduit-osx-arm64.zip` (Apple Silicon)
- **Linux**: `DiscordConduit-linux-x64.zip`

Extract the archive and run the executable.

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

Verify the profile was saved:

```bash
discordconduit profile list
```

### 2. Validate Permissions

Before running a migration, verify the bot has the required permissions in both channels:

```bash
discordconduit validate --profile mybot --source SOURCE_CHANNEL_ID --dest DEST_CHANNEL_ID --guild GUILD_ID
```

This checks for View Channels, Read Message History, Send Messages, Manage Webhooks, and Add Reactions in both the source and destination channels.

### 3. Run a Migration

```bash
discordconduit migrate --profile mybot --source SOURCE_CHANNEL_ID --dest DEST_CHANNEL_ID --guild GUILD_ID
```

The CLI will display a preview (message count, attachment sizes, warnings) and then begin migrating with a live progress indicator.

**Options:**

| Flag              | Description                              |
|-------------------|------------------------------------------|
| `--dry-run`       | Validate the entire pipeline without posting any messages |
| `--no-reactions`  | Skip reaction migration                  |

### 4. Resume an Interrupted Migration

If a migration is interrupted (network error, crash, Ctrl+C), resume it from where it left off:

```bash
discordconduit migrate resume STATE_FILE --profile mybot
```

Migration state files are saved to the migrations directory (see [Data Storage](#data-storage)) after each message is posted.

### 5. Start the Bot for Slash Commands

To enable the interactive slash commands (Move This, Move This & Below, /move-range, /move-thread):

```bash
discordconduit bot start --profile mybot
```

The bot connects to Discord's gateway, registers its commands, and listens for interactions. Press Ctrl+C to stop.

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
- **Polls and button components are not preserved.** These interactive elements cannot be reproduced via webhook.
- **Pin status is not migrated.** Pinned messages in the source channel are not automatically pinned in the destination.
- **Reactions are added as the bot.** Discord's API does not allow adding reactions on behalf of other users, so all migrated reactions show the bot as the reactor.
- **Attachment size limits.** Files larger than the server's upload limit (based on boost level) are skipped and listed as warnings in the preview.

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
