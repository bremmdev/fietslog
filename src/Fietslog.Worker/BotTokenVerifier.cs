using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace Fietslog.Worker;

/// <summary>
/// Stops the worker when Telegram rejects the bot token, at startup or later (e.g. after it is revoked),
/// instead of polling with it forever. <see cref="Rejected"/> tells <c>Program</c> to exit non-zero.
/// </summary>
public sealed class BotTokenVerifier(
    ITelegramBotClient bot,
    IHostApplicationLifetime lifetime,
    ILogger<BotTokenVerifier> logger)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public bool Rejected { get; private set; }

    /// <summary>Checks the token before the host starts.</summary>
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
        catch (ApiRequestException ex) when (IsRejection(ex))
        {
            Reject(ex);
            return false;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not reach Telegram to check the bot token; polling will retry");
            return true;
        }
    }

    /// <summary>Stops the running worker if <paramref name="exception"/> means Telegram rejected the token.</summary>
    /// <returns>Whether it was a rejection.</returns>
    public bool StopIfRejected(Exception exception)
    {
        if (exception is not ApiRequestException ex || !IsRejection(ex))
            return false;

        if (!Rejected)
        {
            Reject(ex);
            lifetime.StopApplication();
        }
        return true;
    }

    // Telegram answers 401 for a wrong or revoked token and 404 for a malformed one.
    private static bool IsRejection(ApiRequestException ex) => ex.ErrorCode is 401 or 404;

    private void Reject(ApiRequestException ex)
    {
        Rejected = true;
        logger.LogCritical(
            "Telegram rejected the bot token ({ErrorCode}: {Description}). Check Bot__Token.", ex.ErrorCode, ex.Message);
    }
}
