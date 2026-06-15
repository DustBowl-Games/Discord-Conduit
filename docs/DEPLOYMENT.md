# Deploying Discord Conduit (bot mode)

This guide covers running the **Discord Conduit CLI bot** (`discordconduit bot start`)
as a long-running container or Kubernetes workload.

> The published image is **`ghcr.io/dustbowl-games/discord-conduit`** (GitHub
> Container Registry, lowercase).

## What runs in a container — and what doesn't

Only the **CLI bot** is containerized. The Avalonia **desktop GUI is NOT**
containerized, and intentionally so:

- The GUI needs a display server (X11/Wayland) and a graphics stack — pointless
  in a headless server/Kubernetes environment.
- The GUI relies on the **OS credential store** (Windows Credential Manager /
  macOS Keychain / libsecret) to hold bot tokens. Containers have no such store.

Inside a container there is **no credential store and no saved profile**, so
`bot start --profile <name>` cannot work. The bot instead reads its token from:

1. `--token-file <path>` — contents of the file (trimmed). Use this for
   Kubernetes/Docker mounted secrets.
2. The **`DISCORD_CONDUIT_TOKEN`** environment variable.
3. `--profile <name>` — only works where an OS credential store exists (i.e. not
   in a container).

The container image defaults to `bot start` with no `--profile`, so it reads the
token from `DISCORD_CONDUIT_TOKEN` out of the box.

## Singleton constraint (read this first)

Run **exactly one** bot instance. The Discord gateway connection is a singleton:
two running bots both receive every interaction and would **double-handle every
slash command**. Do not scale beyond one replica / one container.

---

## 1. Docker

### Build

```bash
docker build -t ghcr.io/dustbowl-games/discord-conduit:latest .
```

### Run

```bash
docker run --rm \
  -e DISCORD_CONDUIT_TOKEN="your-bot-token" \
  ghcr.io/dustbowl-games/discord-conduit:latest
```

The container starts `dotnet discordconduit.dll bot start`, reads the token from
`DISCORD_CONDUIT_TOKEN`, connects to the Discord gateway, registers the slash
commands, and stays running until stopped (`Ctrl+C` / `docker stop`).

To pull the published image instead of building:

```bash
docker pull ghcr.io/dustbowl-games/discord-conduit:latest
```

#### Token from a file instead of an env var

Mount a file and point the bot at it:

```bash
docker run --rm \
  -v /path/to/token.txt:/run/secrets/token:ro \
  ghcr.io/dustbowl-games/discord-conduit:latest \
  bot start --token-file /run/secrets/token
```

---

## 2. Docker Compose

A `docker-compose.yml` is provided at the repo root. It builds the image and
reads the token from a local `.env` file.

```bash
# One-time: create your .env from the template and fill in the token.
cp .env.example .env
# edit .env -> set DISCORD_CONDUIT_TOKEN=...

docker compose up -d        # start in the background
docker compose logs -f      # follow logs
docker compose down         # stop and remove
```

`restart: unless-stopped` keeps the bot running across daemon restarts. The
compose file documents the singleton constraint — do not `docker compose up
--scale bot=2`.

> `.env` is git-ignored. Never commit a real token.

---

## 3. Kubernetes (Helm)

A Helm chart lives under `helm/discord-conduit/`. It deploys a single-replica
Deployment (strategy `Recreate`, so a rollout never briefly runs two gateway
connections). There is **no Service or Ingress** — the bot has no inbound ports;
it is an outbound gateway/REST client only.

### Install with an inline token

The chart renders a Secret from the inline value and wires it into the pod:

```bash
helm install discord-conduit ./helm/discord-conduit \
  --set token.value=YOUR_BOT_TOKEN
```

### Install with an existing Secret (recommended for production)

Create the Secret yourself (so the token never passes through Helm values), then
reference it:

```bash
kubectl create secret generic discord-conduit-token \
  --from-literal=token=YOUR_BOT_TOKEN

helm install discord-conduit ./helm/discord-conduit \
  --set token.existingSecret=discord-conduit-token \
  --set token.existingSecretKey=token
```

When `token.existingSecret` is set, the chart does **not** create a Secret and
ignores `token.value`.

### Common values

| Value                    | Default                                      | Notes                                              |
| ------------------------ | -------------------------------------------- | -------------------------------------------------- |
| `image.repository`       | `ghcr.io/dustbowl-games/discord-conduit`     |                                                    |
| `image.tag`              | `""` (-> chart `appVersion`)                 | Override to pin a specific image tag.              |
| `replicaCount`           | `1`                                          | **Must stay 1** (gateway singleton).               |
| `token.value`            | `""`                                         | Inline token -> chart-managed Secret.              |
| `token.existingSecret`   | `""`                                         | Reference a Secret you manage instead.             |
| `token.existingSecretKey`| `token`                                      | Key within the (existing) Secret.                  |
| `resources`              | small requests / limits                      | Tune for your cluster.                             |
| `extraEnv`               | `[]`                                         | Extra env vars passed to the container.            |

### View logs

```bash
kubectl logs -f -l app.kubernetes.io/instance=discord-conduit
```

### Upgrade / uninstall

```bash
helm upgrade discord-conduit ./helm/discord-conduit --reuse-values
helm uninstall discord-conduit
```

---

## Logs and state

The bot writes Serilog logs and migration state under the user's application-data
directory. In the container this resolves under `$HOME` (`/home/conduit`), which
the image creates and makes writable for the non-root user. If you run the image
with a custom user or a read-only root filesystem, ensure `$HOME` remains
writable or mount a volume there.

## Image provenance

Images are published to **`ghcr.io/dustbowl-games/discord-conduit`** and carry
OCI labels (`org.opencontainers.image.source` points back to
<https://github.com/DustBowl-Games/Discord-Conduit>, license `MIT`).
