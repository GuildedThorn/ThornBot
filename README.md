# ThornBot

My personal Discord bot — music via LavaLink, an auto-joining radio integration with
[GuildedThorn.com](https://guildedthorn.com)'s self-hosted radio relay, and guestbook
notifications piped over RabbitMQ.

## Features

- **Music** — play/queue/skip/stop/resume via [Victoria](https://github.com/Yucked/Victoria)
  (LavaLink client). The bot spawns its own Lavalink process on startup.
- **Radio** — polls GuildedThorn.com's `/api/radio/status` every 5s; when the radio goes
  live it auto-joins a dedicated voice channel, plays the stream, and posts now-playing
  updates. Music commands (`/play`, `/join`, `/leave`, `/skip`, `/stop`, `/resume`) are
  disabled in that guild while the radio is live, since LavaLink only holds one voice
  connection per guild.
- **Guestbook notifications** — consumes the `guestbook_messages` RabbitMQ queue that
  GuildedThorn.com publishes to, and posts new guestbook entries to a configured channel.
- **Uptime Kuma push** — periodically pings a configured push URL for uptime monitoring.

## Slash commands

| Command | Description |
|---|---|
| `/join` | Join your voice channel |
| `/leave` | Leave the voice channel |
| `/play <query or URL>` | Play or queue a song (YouTube search, SoundCloud with `sc:` prefix, or a direct URL) |
| `/stop` | Stop playback and clear the queue |
| `/skip` | Vote-skip the current song (needs >85% of non-bot listeners) |
| `/resume` | Resume a paused song |
| `/radio` | Show whether the radio is live and what's playing |
| `/songrequest <song>` | Log a song request to a configured channel |
| `/info` | Bot stats (uptime, guild/user counts, ping, version) |

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- A JVM (Lavalink is spawned as a child process) — `jdk21` in the Nix dev shell
- RabbitMQ (for guestbook notifications)
- A `Lavalink.jar` — not bundled; download one from
  [lavalink-devs/Lavalink releases](https://github.com/lavalink-devs/Lavalink/releases)
  and place it next to the built binary, or point `Lavalink:JarPath` at it

## Getting started

With [Nix](https://nixos.org) (recommended — matches `flake.nix`):

```bash
nix develop           # dotnet SDK 10, git, nuget, jdk21
bash dev/up.sh         # spins up a local RabbitMQ container + writes a dev .env
dotnet run
```

Without Nix, install the .NET 10 SDK and a JDK yourself, then:

```bash
cp example.env .env    # fill in TOKEN
dotnet run
```

Tear down the dev RabbitMQ container with `bash dev/down.sh`.

## Configuration

Settings live in `Resources/config.json` and can be overridden by environment variables
using the standard `Section__Key` convention (e.g. `Radio__BaseUrl`), which is how
secrets and per-deployment values are injected in production (see `.env`/`EnvironmentFile`).

| Section | Key | Purpose |
|---|---|---|
| `discord` | `developmentGuildId` | Guild slash commands are registered to |
| | `uptimeKumaPushUrl` | Uptime Kuma push monitor URL |
| | `GuestBookGuildId` / `GuestBookChannelId` | Where new guestbook messages are posted |
| `radio` | `baseUrl` | GuildedThorn.com base URL (serves `/api/radio/status` and `/api/radio/stream`) |
| | `notifyChannelId` | Channel for radio online/offline/now-playing messages |
| | `radioGuildId` / `radioChannelId` | Guild + dedicated voice channel the radio auto-joins |
| | `songRequestGuildId` / `songRequestChannelId` | Where `/songrequest` logs requests |
| `lavalink` | `host` / `port` / `authorization` / `selfdeaf` | Lavalink connection settings |
| | `jarPath` / `javaPath` | Path to `Lavalink.jar` and the `java` binary to run it with |

`TOKEN` (bot token) and the `RabbitMQ__*` variables are read directly from the
environment (see `example.env`).

## Deployment

`flake.nix` provides a `nixosModules.default` (`services.thornbot`) that packages the
bot with Nix and runs it as a systemd service — see the module for its options
(`package`, `lavalinkPackage`, `javaPackage`, `environmentFile`).

## License

MIT — see [LICENSE](LICENSE).
