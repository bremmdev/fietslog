using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace Fietslog.Worker;

/// <summary>Checks the bot token with Telegram at startup, so a wrong token stops the worker instead of polling forever.</summary>
public sealed class BotTokenVerifier(ITelegramBotClient bot, ILogger<BotTokenVerifier> logger)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <returns>
    /// <c>false</c> only if Telegram rejected the token. When Telegram can't be reached the token might still be
    /// fine, so that is logged and left to the polling loop to retry.
    /// </returns>
    public async Task<bool> VerifyAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var me = await bot.GetMe(timeout.Token);
            logger.LogInformation("Bot token accepted for @{Username}", me.Username);
            return true;
        }
        // Telegram answers 401 for a wrong token and 404 for a malformed one.
        catch (ApiRequestException ex) when (ex.ErrorCode is 401 or 404)
        {
            logger.LogCritical(
                "Telegram rejected the bot token ({ErrorCode}: {Description}). Check Bot__Token.", ex.ErrorCode, ex.Message);
            return false;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not reach Telegram to check the bot token; polling will retry");
            return true;
        }
    }
}
