using Fietslog.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Fietslog.Worker.Tests;

public sealed class RideMessageHandlerTests : IDisposable
{
    private const long Me = 1234;
    private const long ChatId = 1234;

    private readonly TempDatabase _db = new();
    private readonly RideRepository _repository;
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly RideMessageHandler _handler;

    public RideMessageHandlerTests()
    {
        _repository = new RideRepository(_db.Database);
        var options = Options.Create(new BotOptions
        {
            Token = "test",
            AllowedUserId = Me,
            DatabasePath = _db.Path,
            TimeZone = "Europe/Amsterdam",
        });
        _handler = new RideMessageHandler(_repository, options, _time, NullLogger<RideMessageHandler>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private Task<string?> Send(string? text, long? userId = Me, long messageId = 1) =>
        _handler.HandleAsync(userId, ChatId, messageId, text, CancellationToken.None);

    [Fact]
    public async Task Stores_ride_and_confirms_in_dutch()
    {
        var reply = await Send("20km@52:34");

        Assert.Equal("✅ Opgeslagen: 20 km op 28-09-2026 · 52:34 · 22,8 km/u", reply);
        var stored = Assert.Single(await _repository.GetAllAsync());
        Assert.Equal("20km@52:34", stored.RawText);
    }

    [Fact]
    public async Task Stores_mismatched_time_and_speed_with_a_warning()
    {
        var reply = await Send("20km@52:34@60km/u");

        Assert.Equal(
            "✅ Opgeslagen: 20 km op 28-09-2026 · 52:34 · 60,0 km/u\n" +
            "⚠️ Tijd en snelheid komen niet overeen: 20 km in 52:34 is gemiddeld 22,8 km/u, niet 60,0 km/u. " +
            "Beide zijn opgeslagen zoals ingevoerd.",
            reply);
        var stored = Assert.Single(await _repository.GetAllAsync());
        Assert.Equal(3154, stored.DurationSeconds);
        Assert.Equal(60, stored.AvgSpeedKmh);
    }

    [Fact]
    public async Task Redelivered_mismatched_message_repeats_the_warning()
    {
        await Send("20km@52:34@60km/u", messageId: 5);
        var reply = await Send("20km@52:34@60km/u", messageId: 5);

        Assert.StartsWith("ℹ️ Al opgeslagen", reply);
        Assert.Contains("⚠️ Tijd en snelheid komen niet overeen", reply);
    }

    [Fact]
    public async Task Matching_time_and_speed_get_no_warning()
    {
        Assert.Equal("✅ Opgeslagen: 20 km op 28-09-2026 · 52:12 · 24,0 km/u", await Send("20km@52:12@24km/h"));
    }

    [Fact]
    public async Task Ignores_other_users_silently()
    {
        Assert.Null(await Send("20km", userId: 999));
        Assert.Null(await Send("20km", userId: null));

        Assert.Empty(await _repository.GetAllAsync());
    }

    [Theory]
    [InlineData("/start")]
    [InlineData("/help")]
    [InlineData("/help@fietslog_bot")]
    [InlineData(null)]
    public async Task Replies_with_help(string? text)
    {
        Assert.Equal(Messages.Help, await Send(text));
    }

    [Fact]
    public async Task Replies_with_error_for_unparseable_message()
    {
        var reply = await Send("hallo");

        Assert.StartsWith("❌ Onbekende invoer: 'hallo'.", reply);
        Assert.Contains("Voorbeelden:", reply);
        Assert.Empty(await _repository.GetAllAsync());
    }

    [Fact]
    public async Task Redelivered_message_is_not_stored_twice()
    {
        await Send("16km", messageId: 5);
        var reply = await Send("16km", messageId: 5);

        Assert.StartsWith("ℹ️ Al opgeslagen", reply);
        Assert.Single(await _repository.GetAllAsync());
    }

    [Fact]
    public async Task Today_is_determined_in_amsterdam_time()
    {
        // 22:30 UTC on 28 Sept is 00:30 on 29 Sept in Amsterdam (CEST, UTC+2).
        _time.Now = new DateTimeOffset(2026, 9, 28, 22, 30, 0, TimeSpan.Zero);

        var reply = await Send("16km");

        Assert.Equal("✅ Opgeslagen: 16 km op 29-09-2026", reply);
        Assert.Equal("2026-09-29", Assert.Single(await _repository.GetAllAsync()).RideDate);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
