# Fietslog: plan

A small .NET 10 background worker. It receives ride messages from one Telegram user, parses them, stores them in SQLite and replies with a confirmation in Dutch. It runs on Railway, with the database file on a Railway volume.

## Decisions

| Topic | Decision |
|---|---|
| Runtime | .NET 10 (LTS), `Microsoft.NET.Sdk.Worker` (Generic Host + `BackgroundService`) |
| Telegram | `Telegram.Bot` NuGet package, **long polling** (`getUpdates`). No public URL, domain or webhook is needed. |
| Data access | Dapper + `Microsoft.Data.Sqlite` |
| Schema management | `CREATE TABLE IF NOT EXISTS` at startup, versioned with `PRAGMA user_version` |
| Access | One allowed Telegram user ID. Messages from anyone else are ignored and logged. |
| Replies | Dutch confirmation for each save, or a Dutch error with examples |
| "Today" | Computed in `Europe/Amsterdam` |
| Deployment | Railway, Dockerfile build, 1 replica, volume mounted at `/data` |

## Message format

A message contains one ride token and, optionally, one date token, separated by whitespace in either order. Matching ignores case and tolerates spaces around `@`.

```
<distance>km[@<part>][@<part>]  [date]
```

- **distance** (required): `16km`, `20.5km` or `20,5km` (decimal comma accepted).
- **part** (optional, at most one of each, in either order):
  - duration: `mm:ss` (`52:34`) or `h:mm:ss` (`1:05:12`). A value with two parts **always** means minutes:seconds.
  - average speed: `23.3km/h`, `23,3km/h` or `23,3km/u`.
- **date** (optional, defaults to today in Europe/Amsterdam): `2026-08-30` or `30-08-2026`.

| Input | Stored |
|---|---|
| `16km` | 16 km, today, no duration, no speed |
| `20km@52:34` | 20 km, 3154 s, speed **computed** as 22.8 km/h |
| `20km@23.3km/h` | 20 km, 23.3 km/h, duration **computed** as 3090 s |
| `20km@52:12@24km/h` | 20 km, 3132 s, 24 km/h, **both as given** (no cross-check) |
| `20,5km@1:02:10 30-08-2026` | 20.5 km, 3730 s, 19.79 km/h (computed), 2026-08-30 |

A message is rejected, with a Dutch error reply, when:
- it has no distance, or the distance is not in the range 0–1000 km
- a duration or speed appears more than once, or cannot be parsed (for example, seconds ≥ 60)
- the date is invalid or in the future
- it contains unknown extra tokens

`/start` and `/help` reply with the format and examples. Non-text messages get the help text.

### Example replies (Dutch)

```
✅ Opgeslagen: 20 km op 28-09-2026 · 52:34 · 22,8 km/u
❌ Dat begrijp ik niet. Voorbeelden: 16km · 20km@52:34 · 20km@23,3km/u · 20km@52:12@24km/u 30-08-2026
```

## Data model

```sql
CREATE TABLE IF NOT EXISTS rides (
    id                   INTEGER PRIMARY KEY AUTOINCREMENT,
    ride_date            TEXT    NOT NULL,          -- yyyy-MM-dd
    distance_km          REAL    NOT NULL,
    duration_seconds     INTEGER NULL,
    avg_speed_kmh        REAL    NULL,
    raw_text             TEXT    NOT NULL,          -- original message, for audit and re-parsing
    telegram_chat_id     INTEGER NOT NULL,
    telegram_message_id  INTEGER NOT NULL,
    created_at_utc       TEXT    NOT NULL,          -- ISO 8601
    UNIQUE (telegram_chat_id, telegram_message_id)  -- idempotent if Telegram re-delivers an update
);
```

When only one of duration or speed is given, the other is computed and stored. When both are given, both are stored exactly as typed. `raw_text` records what was actually entered.

Connection setup: `PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;`. Only one process ever opens the file.

Dates are stored as ISO text and mapped by hand in the repository, which avoids Dapper `DateOnly` type handlers.

## Project layout

```
fietslog/
├── Fietslog.slnx
├── Dockerfile
├── .dockerignore
├── railway.json
├── README.md
├── src/Fietslog.Worker/
│   ├── Fietslog.Worker.csproj
│   ├── Program.cs                # host, options, DI
│   ├── BotOptions.cs             # token, allowed user id, db path, time zone
│   ├── TelegramPollingService.cs # BackgroundService: receive loop, send replies
│   ├── RideMessageHandler.cs     # auth, parse, store, choose reply (no Telegram types)
│   ├── RideParser.cs             # string -> ParseResult (pure, no I/O)
│   ├── Ride.cs                   # record type
│   ├── RideRepository.cs         # Dapper insert
│   ├── Database.cs               # connection factory + schema init
│   └── Messages.cs               # Dutch reply strings/formatting
└── tests/Fietslog.Worker.Tests/
    ├── RideParserTests.cs        # table-driven parser cases
    ├── RideRepositoryTests.cs    # temp-file SQLite, insert + duplicate message id
    ├── RideMessageHandlerTests.cs # auth, help, errors, Amsterdam "today"
    ├── MessagesTests.cs          # Dutch formatting
    └── TempDatabase.cs           # test fixture
```

## Worker flow

1. At startup, validate options (fail fast if the token or user ID is missing), create the `/data` directory if needed, and initialise the schema.
2. `TelegramPollingService` calls `bot.ReceiveAsync(...)` with `AllowedUpdates = [Message]` and `DropPendingUpdates = false`, so rides sent while the worker was down (for example, during a redeploy) are still processed. Telegram keeps them for 24 hours.
3. For each message:
   - `from.id != AllowedUserId`: log and ignore, with no reply.
   - `/start` or `/help`: reply with help.
   - Otherwise, parse the message. On success, insert the ride (a duplicate `message_id` means it was already saved) and reply ✅. On failure, reply ❌ with examples.
4. Errors are logged and never crash the loop. `Telegram.Bot` backs off and retries polling errors.
5. The loop exits cleanly on `SIGTERM` via the host's cancellation token (Railway redeploys).

## Configuration (environment variables)

| Variable | Example | Notes |
|---|---|---|
| `Bot__Token` | `123456:ABC...` | From @BotFather. Kept as a Railway secret. |
| `Bot__AllowedUserId` | `123456789` | Your numeric Telegram user ID (from @userinfobot) |
| `Bot__DatabasePath` | `/data/fietslog.db` | This is the default. Use `./data/fietslog.db` locally. |
| `Bot__TimeZone` | `Europe/Amsterdam` | This is the default. |

## Deployment on Railway

1. **Dockerfile**: multi-stage build, `mcr.microsoft.com/dotnet/sdk:10.0` to build and `mcr.microsoft.com/dotnet/runtime:10.0` to run. The runtime is the Debian image rather than the chiseled one, so it includes `tzdata` for `Europe/Amsterdam`.
2. **railway.json**: `builder: DOCKERFILE`, `numReplicas: 1`, `restartPolicyType: ON_FAILURE`. No healthcheck path, because the service has no HTTP endpoint.
3. **Volume**: attach a volume to the service at mount path `/data`, in the dashboard or with `railway volume add --mount-path /data`.
4. **Permissions**: Railway mounts volumes as root. The .NET 10 runtime image runs as root unless `USER $APP_UID` is set, so no extra setting is needed. If the image is later switched to the non-root `app` user, set `RAILWAY_RUN_UID=0`.
5. **Variables**: set `Bot__Token` and `Bot__AllowedUserId`.
6. **Single instance**: Telegram allows only one `getUpdates` consumer per bot, and a volume attaches to one replica. Keep replicas at 1. Railway stops the old container before starting the new one on services with a volume, so polling doesn't conflict during a deploy.

Backups are out of scope. Later options include `railway volume` snapshots or a scheduled `sqlite3 .backup` copy.

## Testing

- **Parser**: table-driven xUnit cases covering every example above, decimal comma, `km/u`, both date formats, `@` in either order, `1:05` = 65 s, and invalid inputs such as `52:75`, duplicate parts, future dates and missing `km`.
- **Derivation**: rounding of computed speed (1 decimal) and duration (whole seconds).
- **Repository**: insert into a temp database file. A second insert with the same message ID is a no-op.
- **Manual**: run locally with a test bot token (`dotnet run`), send the examples and check the rows with `sqlite3`.

## Implementation steps

1. Scaffold the solution, worker and test projects. Add `Telegram.Bot`, `Dapper`, `Microsoft.Data.Sqlite` and xUnit.
2. Build `RideParser` and its tests (the core logic, written test-first).
3. Add `Database`, `RideRepository` and the repository tests.
4. Add `TelegramPollingService` with the allowlist, Dutch replies and help.
5. Add `Program.cs` with options binding and validation.
6. Add the Dockerfile, `.dockerignore` and `railway.json`, then build the image locally.
7. Write a README covering BotFather setup, finding your user ID, local run and Railway steps.
8. Deploy to Railway, attach the volume, set the variables and do a smoke test from Telegram.
