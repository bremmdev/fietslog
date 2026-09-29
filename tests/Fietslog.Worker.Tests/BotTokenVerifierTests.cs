using System.Net;
using System.Text;
using Fietslog.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace Fietslog.Worker.Tests;

public class BotTokenVerifierTests
{
    private const string Token = "123456:ABC-def_ghi";
    private const string Unauthorized = """{"ok":false,"error_code":401,"description":"Unauthorized"}""";

    private readonly FakeLifetime _lifetime = new();

    [Fact]
    public async Task Accepts_token_telegram_knows()
    {
        var verifier = Verifier(new StubHandler(
            HttpStatusCode.OK,
            """{"ok":true,"result":{"id":123456,"is_bot":true,"first_name":"Fietslog","username":"fietslog_bot"}}"""));

        Assert.True(await verifier.VerifyAsync());
        Assert.False(verifier.Rejected);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, 401, "Unauthorized")]
    [InlineData(HttpStatusCode.NotFound, 404, "Not Found")]
    public async Task Rejects_token_telegram_refuses(HttpStatusCode status, int code, string description)
    {
        var verifier = Verifier(new StubHandler(
            status, $$"""{"ok":false,"error_code":{{code}},"description":"{{description}}"}"""));

        Assert.False(await verifier.VerifyAsync());
        Assert.True(verifier.Rejected);
    }

    [Fact]
    public async Task Does_not_fail_when_telegram_is_unreachable()
    {
        var verifier = Verifier(new StubHandler(new HttpRequestException("Network is unreachable")));

        Assert.True(await verifier.VerifyAsync());
        Assert.False(verifier.Rejected);
    }

    [Fact]
    public async Task Does_not_fail_on_telegram_server_error()
    {
        var verifier = Verifier(new StubHandler(
            HttpStatusCode.BadGateway, """{"ok":false,"error_code":502,"description":"Bad Gateway"}"""));

        Assert.True(await verifier.VerifyAsync());
        Assert.False(verifier.Rejected);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(404)]
    public void Stops_worker_once_when_token_is_rejected_while_running(int code)
    {
        var verifier = Verifier(new StubHandler(HttpStatusCode.OK, "{}"));

        Assert.True(verifier.StopIfRejected(new ApiRequestException("Unauthorized", code)));
        Assert.True(verifier.StopIfRejected(new ApiRequestException("Unauthorized", code)));

        Assert.True(verifier.Rejected);
        Assert.Equal(1, _lifetime.StopCount);
    }

    public static TheoryData<Exception> OtherErrors => new()
    {
        new ApiRequestException("Conflict: terminated by other getUpdates request", 409),
        new ApiRequestException("Too Many Requests", 429),
        new ApiRequestException("Bad Gateway", 502),
        new RequestException("Bot API Service Failure", new HttpRequestException("Network is unreachable")),
    };

    [Theory]
    [MemberData(nameof(OtherErrors))]
    public void Keeps_running_on_other_errors(Exception exception)
    {
        var verifier = Verifier(new StubHandler(HttpStatusCode.OK, "{}"));

        Assert.False(verifier.StopIfRejected(exception));

        Assert.False(verifier.Rejected);
        Assert.Equal(0, _lifetime.StopCount);
    }

    [Fact]
    public async Task Polling_stops_worker_when_token_is_revoked()
    {
        using var db = new TempDatabase();
        var bot = new TelegramBotClient(Token, new HttpClient(new StubHandler(HttpStatusCode.Unauthorized, Unauthorized)));
        var verifier = new BotTokenVerifier(bot, _lifetime, NullLogger<BotTokenVerifier>.Instance);
        var options = Options.Create(new BotOptions { Token = Token, AllowedUserId = 1, DatabasePath = db.Path });
        var handler = new RideMessageHandler(
            new RideRepository(db.Database), options, TimeProvider.System, NullLogger<RideMessageHandler>.Instance);
        using var service = new TelegramPollingService(bot, handler, verifier, NullLogger<TelegramPollingService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await _lifetime.StopRequested.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        Assert.True(verifier.Rejected);
        Assert.Equal(1, _lifetime.StopCount);
    }

    private BotTokenVerifier Verifier(HttpMessageHandler handler) =>
        new(new TelegramBotClient(Token, new HttpClient(handler)), _lifetime, NullLogger<BotTokenVerifier>.Instance);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;

        public StubHandler(HttpStatusCode status, string json) =>
            _respond = () => new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

        public StubHandler(Exception exception) => _respond = () => throw exception;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond());
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        private readonly TaskCompletionSource _stopRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StopCount { get; private set; }

        public Task StopRequested => _stopRequested.Task;

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
            StopCount++;
            _stopRequested.TrySetResult();
        }
    }
}
