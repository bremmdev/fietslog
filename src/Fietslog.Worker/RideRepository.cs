using System.Globalization;
using Dapper;

namespace Fietslog.Worker;

public sealed record StoredRide(
    long Id,
    string RideDate,
    double DistanceKm,
    long? DurationSeconds,
    double? AvgSpeedKmh,
    string RawText,
    long TelegramChatId,
    long TelegramMessageId,
    string CreatedAtUtc);

public sealed class RideRepository(Database database)
{
    /// <returns>
    /// <c>true</c> if the ride was inserted, <c>false</c> if this Telegram message was already stored.
    /// </returns>
    public async Task<bool> AddAsync(
        Ride ride, string rawText, long chatId, long messageId, DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.Open();
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO rides (ride_date, distance_km, duration_seconds, avg_speed_kmh, raw_text,
                               telegram_chat_id, telegram_message_id, created_at_utc)
            VALUES (@RideDate, @DistanceKm, @DurationSeconds, @AvgSpeedKmh, @RawText,
                    @ChatId, @MessageId, @CreatedAtUtc)
            ON CONFLICT (telegram_chat_id, telegram_message_id) DO NOTHING;
            """,
            new
            {
                RideDate = ride.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ride.DistanceKm,
                ride.DurationSeconds,
                ride.AvgSpeedKmh,
                RawText = rawText,
                ChatId = chatId,
                MessageId = messageId,
                CreatedAtUtc = createdAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            },
            cancellationToken: cancellationToken));
        return rows == 1;
    }

    public async Task<IReadOnlyList<StoredRide>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = database.Open();
        var rides = await connection.QueryAsync<StoredRide>(new CommandDefinition(
            """
            SELECT id, ride_date AS RideDate, distance_km AS DistanceKm, duration_seconds AS DurationSeconds,
                   avg_speed_kmh AS AvgSpeedKmh, raw_text AS RawText, telegram_chat_id AS TelegramChatId,
                   telegram_message_id AS TelegramMessageId, created_at_utc AS CreatedAtUtc
            FROM rides ORDER BY ride_date, id;
            """,
            cancellationToken: cancellationToken));
        return rides.AsList();
    }
}
