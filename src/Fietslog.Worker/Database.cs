using Dapper;
using Microsoft.Data.Sqlite;

namespace Fietslog.Worker;

public sealed class Database(string path)
{
    private const int SchemaVersion = 1;

    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadWriteCreate,
        DefaultTimeout = 5,
    }.ToString();

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>Creates the database file and applies schema changes up to <see cref="SchemaVersion"/>.</summary>
    public void Initialize()
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var connection = Open();
        connection.Execute("PRAGMA journal_mode = WAL;");

        var version = connection.ExecuteScalar<long>("PRAGMA user_version;");
        if (version >= SchemaVersion)
            return;

        using var transaction = connection.BeginTransaction();
        if (version < 1)
        {
            connection.Execute(
                """
                CREATE TABLE IF NOT EXISTS rides (
                    id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                    ride_date            TEXT    NOT NULL,
                    distance_km          REAL    NOT NULL,
                    duration_seconds     INTEGER NULL,
                    avg_speed_kmh        REAL    NULL,
                    raw_text             TEXT    NOT NULL,
                    telegram_chat_id     INTEGER NOT NULL,
                    telegram_message_id  INTEGER NOT NULL,
                    created_at_utc       TEXT    NOT NULL,
                    UNIQUE (telegram_chat_id, telegram_message_id)
                );
                CREATE INDEX IF NOT EXISTS ix_rides_ride_date ON rides (ride_date);
                """,
                transaction: transaction);
        }
        connection.Execute($"PRAGMA user_version = {SchemaVersion};", transaction: transaction);
        transaction.Commit();
    }
}
