# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Undo for the bot move flow: after a move, an **Undo (remove the copies)** button
  deletes the messages just reposted into the destination, leaving the source
  originals untouched.
- Sticker fallback: PNG/APNG/GIF stickers are re-posted as their image (`--no-stickers`
  to disable); Lottie stickers can't be migrated. Voice messages migrate as their
  audio attachment.
- Original timestamps: `--timestamps` appends each message's original send time as
  a footer.
- Channel & category cloning: `discordconduit clone` creates the destination
  channel(s) and migrates messages in one step, including across servers
  (`--dest-guild`; `--category` to clone a whole category). Plain `migrate` also
  works cross-server.
- Channel export: `discordconduit export` archives a channel to HTML, JSON, CSV,
  or plain text (read-only — nothing is posted), with the same filter flags as
  `migrate`.
- Message filtering: migrate a subset by author (`--from-author`), date range
  (`--since`/`--until`), keyword (`--contains`), attachments-only
  (`--attachments-only`), or excluding bots (`--no-bots`).
- Pin preservation: messages pinned in the source are re-pinned in the
  destination (`--no-pins` to skip).
- Poll migration: polls are re-created with their question and answers
  (votes/timing restart; `--no-polls` to skip).
- Forum tag mapping: moving a forum post to another forum carries its tags
  across, matched by name.
- Docker image and Helm chart for running the CLI bot mode as a long-lived service.
- `discordconduit bot start` can read the token from the `DISCORD_CONDUIT_TOKEN`
  environment variable or a `--token-file` (for container/Kubernetes secrets).
- `profile add` supports masked interactive token entry and the
  `DISCORD_CONDUIT_TOKEN` environment variable, so the token need not be passed
  on the command line.
- Migration preview now surfaces bot permission issues before a run.
- Working pause/resume during migration.
- Project packaging metadata, SourceLink, `Directory.Build.props`, and community
  health files (CONTRIBUTING, SECURITY, CODE_OF_CONDUCT, issue/PR templates).

### Changed

- Attachment upload limit raised from 8 MB to 25 MB (Discord's current default).
- Attachment downloads are now capped to a hard byte limit independent of the
  size reported by the API.
- CLI commands now return non-zero exit codes on failure (for scripting/CI).

### Fixed

- Cancelling a migration mid-message is no longer recorded as a failed message.
- The gateway client keeps honoring the caller's cancellation token across
  reconnects, and heartbeat/socket state is accessed safely across threads.
- Rate limiter admits same-bucket requests one at a time and applies a
  proactive global request budget.
- Thread-destination selection acknowledges the interaction before any network
  call, so it no longer exceeds Discord's response deadline.
- Concurrent move flows by the same user no longer clobber each other's state
  (per-flow nonce on component IDs).
- `/move-thread` verifies the invoking user can read the source channel.
- Removing the connected profile now disconnects the live session.
- App shutdown no longer blocks the UI thread on async disconnect.

## [1.0.0] - 2026-04

### Added

- Initial release: migrate Discord messages between channels and threads via
  webhooks, preserving the original author's username and avatar.
- Attachment re-upload, reaction migration (best-effort), and reply references.
- Resumable migrations with state persisted after each message.
- Dry-run mode and live progress tracking with ETA.
- Avalonia desktop app, CLI companion, and a gateway bot with slash and
  context-menu commands.
- Cross-platform credential storage (Windows Credential Manager, macOS Keychain,
  libsecret).

[Unreleased]: https://github.com/DustBowl-Games/Discord-Conduit/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/DustBowl-Games/Discord-Conduit/releases/tag/v1.0.0
