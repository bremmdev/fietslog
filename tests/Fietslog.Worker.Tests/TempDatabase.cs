using Fietslog.Worker;
using Microsoft.Data.Sqlite;

namespace Fietslog.Worker.Tests;

/// <summary>An initialized SQLite database in a temp directory, deleted on dispose.</summary>
public sealed class TempDatabase : IDisposable
{
    private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fietslog-tests", Guid.NewGuid().ToString("N"));

    public TempDatabase()
    {
        // Nested path checks that Initialize creates missing directories (like /data on a fresh volume).
        Path = System.IO.Path.Combine(_directory, "nested", "fietslog.db");
        Database = new Database(Path);
        Database.Initialize();
    }

    public string Path { get; }

    public Database Database { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
