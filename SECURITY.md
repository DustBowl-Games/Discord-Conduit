# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 1.0.x   | :white_check_mark: |
| < 1.0   | :x:                |

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Instead, report them privately by email to **security@dustbowl.games**, or via
GitHub's [private vulnerability reporting](https://github.com/DustBowl-Games/Discord-Conduit/security/advisories/new)
for this repository.

Please include:

- A description of the vulnerability and its impact.
- Steps to reproduce, or a proof of concept.
- Affected version(s) and platform(s).
- Any suggested remediation, if you have one.

We aim to acknowledge reports within **3 business days** and to provide a more
detailed response within **10 business days** indicating the next steps.

## Scope

Discord Conduit handles sensitive material, so the following areas are
especially in scope:

- **Bot token handling** — tokens are stored in the OS credential store
  (Windows Credential Manager / macOS Keychain / libsecret) and must never be
  written to disk or logs in plaintext.
- **Webhook tokens** — created per migration; they are not persisted to the
  state file and are re-fetched on resume.
- **Attachment download** — files are fetched only from Discord CDN hosts and
  the download is size-capped to guard against memory exhaustion.
- **Interaction authorization** — slash/context-menu commands must not let a
  user move messages out of a channel they cannot read.
- **Credential store integration** — the macOS/Linux stores shell out to
  `security`/`secret-tool`; arguments are passed via `ArgumentList` and secrets
  via stdin (never on the command line).

## Disclosure

We follow a coordinated disclosure process. Once a fix is available we will
publish a security advisory and credit the reporter, unless anonymity is
requested.
