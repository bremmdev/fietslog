using Microsoft.Extensions.Options;

namespace Fietslog.Worker;

/// <summary>Turns an incoming chat message into a stored ride and a reply. Independent of Telegram types.</summary>
public sealed class RideMessageHandler(
    RideRepository repository,
    IOptions<BotOptions> options,
    TimeProvider timeProvider,
    ILogger<RideMessageHandler> logger)
{
    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);

    /// <returns>The reply to send, or <c>null</c> to stay silent.</returns>
    public async Task<string?> HandleAsync(
        long? userId, long chatId, long messageId, string? text, CancellationToken cancellationToken)
    {
        if (userId != options.Value.AllowedUserId)
        {
            logger.LogWarning("Ignoring message {MessageId} from unauthorized user {UserId}", messageId, userId);
            return null;
        }

        if (text is null)
            return Messages.Help;

        var command = text.Trim().Split(' ', '@')[0].ToLowerInvariant();
        if (command is "/start" or "/help")
            return Messages.Help;

        switch (RideParser.Parse(text, Today()))
        {
            case ParseResult.Failure failure:
                logger.LogInformation("Could not parse message {MessageId}: {Error}", messageId, failure.Error);
                return Messages.ParseError(failure.Error);

            case ParseResult.Success { Ride: var ride, TimeSpeedMismatch: var mismatch }:
                try
                {
                    var inserted = await repository.AddAsync(
                        ride, text, chatId, messageId, timeProvider.GetUtcNow(), cancellationToken);
                    logger.LogInformation(
                        "Message {MessageId}: {Ride} (inserted: {Inserted}, time/speed mismatch: {Mismatch})",
                        messageId, ride, inserted, mismatch);
                    var reply = inserted ? Messages.Saved(ride) : Messages.AlreadySaved(ride);
                    return mismatch ? $"{reply}\n{Messages.TimeSpeedMismatch(ride)}" : reply;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Failed to store ride from message {MessageId}", messageId);
                    return Messages.SaveFailed;
                }

            default:
                throw new InvalidOperationException("Unexpected parse result.");
        }
    }

    private DateOnly Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), _timeZone).DateTime);
}
