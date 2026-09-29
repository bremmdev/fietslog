using Dapper;
using Fietslog.Worker;

namespace Fietslog.Worker.Tests;

public sealed class RideRepositoryTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly RideRepository _repository;
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 28, 18, 0, 0, TimeSpan.Zero);

    public RideRepositoryTests() => _repository = new RideRepository(_db.Database);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Stores_all_fields()
    {
        var ride = new Ride(new DateOnly(2026, 8, 30), 20.5, 3730, 19.8);

        Assert.True(await _repository.AddAsync(ride, "20,5km@1:02:10 30-08-2026", 42, 7, CreatedAt));

        var stored = Assert.Single(await _repository.GetAllAsync());
        Assert.Equal("2026-08-30", stored.RideDate);
        Assert.Equal(20.5, stored.DistanceKm);
        Assert.Equal(3730, stored.DurationSeconds);
        Assert.Equal(19.8, stored.AvgSpeedKmh);
        Assert.Equal("20,5km@1:02:10 30-08-2026", stored.RawText);
        Assert.Equal(42, stored.TelegramChatId);
        Assert.Equal(7, stored.TelegramMessageId);
        Assert.Equal("2026-09-28T18:00:00.0000000Z", stored.CreatedAtUtc);
    }

    [Fact]
    public async Task Stores_nulls_for_distance_only_ride()
    {
        await _repository.AddAsync(new Ride(new DateOnly(2026, 9, 28), 16, null, null), "16km", 42, 7, CreatedAt);

        var stored = Assert.Single(await _repository.GetAllAsync());
        Assert.Null(stored.DurationSeconds);
        Assert.Null(stored.AvgSpeedKmh);
    }

    [Fact]
    public async Task Same_telegram_message_is_stored_once()
    {
        var ride = new Ride(new DateOnly(2026, 9, 28), 16, null, null);

        Assert.True(await _repository.AddAsync(ride, "16km", 42, 7, CreatedAt));
        Assert.False(await _repository.AddAsync(ride, "16km", 42, 7, CreatedAt));
        Assert.True(await _repository.AddAsync(ride, "16km", 42, 8, CreatedAt));

        Assert.Equal(2, (await _repository.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Initialize_is_idempotent_and_keeps_data()
    {
        await _repository.AddAsync(new Ride(new DateOnly(2026, 9, 28), 16, null, null), "16km", 42, 7, CreatedAt);

        _db.Database.Initialize();

        Assert.Single(await _repository.GetAllAsync());
        using var connection = _db.Database.Open();
        Assert.Equal(1, connection.ExecuteScalar<long>("PRAGMA user_version;"));
        Assert.Equal("wal", connection.ExecuteScalar<string>("PRAGMA journal_mode;"));
    }
}
