using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Fietslog.Worker;

/// <summary>Receives messages from Telegram via long polling and replies to them.</summary>
public sealed class TelegramPollingService(
    ITelegramBotClient bot,
    RideMessageHandler handler,
    ILogger<TelegramPollingService> logger) : BackgroundService
{
    private static readonly TimeSpan PollingErrorDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var me = await bot.GetMe(stoppingToken);
            logger.LogInformation("Polling Telegram as @{Username}", me.Username);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not fatal: the polling loop below retries and logs persistent failures (e.g. a bad token).
            logger.LogWarning(ex, "Could not reach Telegram at startup");
        }

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message],
            // Keep rides sent while the worker was down (e.g. during a redeploy).
            DropPendingUpdates = false,
        };

        await bot.ReceiveAsync(
            new DefaultUpdateHandler(HandleUpdateAsync, HandleErrorAsync), receiverOptions, stoppingToken);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { } message)
            return;

        var reply = await handler.HandleAsync(
            message.From?.Id, message.Chat.Id, message.MessageId, message.Text, cancellationToken);
        if (reply is null)
            return;

        await client.SendMessage(
            message.Chat.Id,
            reply,
            replyParameters: new ReplyParameters { MessageId = message.MessageId, AllowSendingWithoutReply = true },
            cancellationToken: cancellationToken);
    }

    private async Task HandleErrorAsync(
        ITelegramBotClient client, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
    {
        if (exception is ApiRequestException { ErrorCode: 409 })
            logger.LogError("Another instance is polling this bot (409 Conflict). Run exactly one replica.");
        else
            logger.LogError(exception, "Telegram {Source}", source);

        // Avoid a tight retry loop when Telegram or the network is unavailable.
        if (source == HandleErrorSource.PollingError)
            await Task.Delay(PollingErrorDelay, cancellationToken);
    }
}
