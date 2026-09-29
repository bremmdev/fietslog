# Fietslog

A small .NET 10 background worker that logs bike rides sent to a Telegram bot. It stores them in SQLite and runs on Railway with the database on a volume. The design is described in [PLAN.md](PLAN.md).

## Sending rides

Send the bot a message in this form:

```
<distance>km[@time][@speed] [date]
```

| Message | Stored |
|---|---|
| `16km` | 16 km today |
| `20km@52:34` | 20 km, 52:34, speed computed (22.83 km/h) |
| `20km@23,3km/u` | 20 km, 23.3 km/h, time computed (51:30) |
| `20km@52:12@24km/h` | both stored as given, with a warning if they don't match |
| `20,5km@1:02:10 30-08-2026` | on 30 August 2026 |

- **Time:** `mm:ss` or `h:mm:ss`. A two-part time is always minutes:seconds, so `1:05` is 65 seconds.
- **Speed:** `km/h` or `km/u`. Decimal commas and points both work. Typed and computed speeds must be 1–100 km/h, so a slip like `16km@1:05` (65 seconds) is rejected instead of stored.
- **Date:** `yyyy-mm-dd` or `dd-mm-yyyy`. It defaults to today in Europe/Amsterdam time. Future dates are rejected.
- **Replies:** the bot confirms every save in Dutch. `/help` shows the format.
- **Access:** messages from anyone other than `Bot__AllowedUserId` are ignored.

## Setup

1. **Create a bot:** message [@BotFather](https://t.me/BotFather), send `/newbot` and copy the token.
2. **Find your user ID:** message [@userinfobot](https://t.me/userinfobot). It replies with your numeric ID.

### Configuration

| Environment variable | Required | Default |
|---|---|---|
| `Bot__Token` | yes | none |
| `Bot__AllowedUserId` | yes | none |
| `Bot__DatabasePath` | no | `/data/fietslog.db` (`data/fietslog.db` in Development) |
| `Bot__TimeZone` | no | `Europe/Amsterdam` |

The worker exits at startup with a non-zero code if the token or user ID is missing, if the token isn't in BotFather's `123456:ABC...` format, or if Telegram rejects the token. If Telegram rejects the token later, for example because you revoked it in @BotFather, the worker also stops with a non-zero code. If Telegram can't be reached, the worker keeps running and retrying.

## Running locally

```sh
cd src/Fietslog.Worker
dotnet user-secrets set Bot:Token "123456:ABC..."
dotnet user-secrets set Bot:AllowedUserId 123456789
dotnet run          # writes to src/Fietslog.Worker/data/fietslog.db
```

To check what was stored:

```sh
sqlite3 src/Fietslog.Worker/data/fietslog.db "SELECT * FROM rides;"
```

Stop any Railway deployment first. Telegram allows only one process to poll a bot at a time, and a second one gets `409 Conflict`. You can also use a separate test bot locally.

### Tests

```sh
dotnet test
```

## Deploying to Railway

1. Create a new project and choose **Deploy from GitHub repo** with this repository. Railway picks up `railway.json` and builds the `Dockerfile`.
2. Attach a volume to the service at mount path **`/data`**. You can do this from the service's context menu (**Attach volume**) or with `railway volume add --mount-path /data`.
3. Under **Variables**, set `Bot__Token` and `Bot__AllowedUserId`.
4. Deploy, then send `/start` to your bot.

Notes:

- **No public domain needed.** The worker uses long polling, so it has no HTTP endpoint.
- **Keep one replica.** Telegram allows only one poller per bot, and a volume attaches to only one instance.
- **Messages sent during a redeploy are kept.** Telegram holds them for 24 hours and the worker processes them when it starts again.
- **Volume permissions.** The image runs as root, which can write to Railway's root-owned volume. If you change the Dockerfile to `USER $APP_UID`, also set `RAILWAY_RUN_UID=0`.
- **Getting the data out.** `railway ssh` into the service and use `/data/fietslog.db`. You can also copy the file out from a volume backup.

## Data

```sql
rides(id, ride_date 'yyyy-mm-dd', distance_km, duration_seconds, avg_speed_kmh,
      raw_text, telegram_chat_id, telegram_message_id, created_at_utc)
```

- **Time and speed:** when you give only one, the other is computed and stored. When you give both, both are stored as typed. If they don't match the distance, the reply includes a warning. The check is loose on purpose, so small differences don't trigger it. The distance may be off by half its last typed digit (`20km` could be 19.5–20.5), the time by 1 minute, and the speed by 0.5 km/h.
- **Original message:** `raw_text` always keeps what you typed.
- **Duplicates:** a message Telegram delivers twice is stored once, because `(telegram_chat_id, telegram_message_id)` is unique.
