using System.Net;
using System.Text;
using Fietslog.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot;

namespace Fietslog.Worker.Tests;

public class BotTokenVerifierTests
{
    private const string Token = "123456:ABC-def_ghi";

    [Fact]
    public async Task Accepts_token_telegram_knows()
    {
        var verifier = Verifier(new StubHandler(
            HttpStatusCode.OK,
            """{"ok":true,"result":{"id":123456,"is_bot":true,"first_name":"Fietslog","username":"fietslog_bot"}}"""));

        Assert.True(await verifier.VerifyAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, 401, "Unauthorized")]
    [InlineData(HttpStatusCode.NotFound, 404, "Not Found")]
    public async Task Rejects_token_telegram_refuses(HttpStatusCode status, int code, string description)
    {
        var verifier = Verifier(new StubHandler(
            status, $$"""{"ok":false,"error_code":{{code}},"description":"{{description}}"}"""));

        Assert.False(await verifier.VerifyAsync());
    }

    [Fact]
    public async Task Does_not_fail_when_telegram_is_unreachable()
    {
        var verifier = Verifier(new StubHandler(new HttpRequestException("Network is unreachable")));

        Assert.True(await verifier.VerifyAsync());
    }

    [Fact]
    public async Task Does_not_fail_on_telegram_server_error()
    {
        var verifier = Verifier(new StubHandler(
            HttpStatusCode.BadGateway, """{"ok":false,"error_code":502,"description":"Bad Gateway"}"""));

        Assert.True(await verifier.VerifyAsync());
    }

    private static BotTokenVerifier Verifier(HttpMessageHandler handler) =>
        new(new TelegramBotClient(Token, new HttpClient(handler)), NullLogger<BotTokenVerifier>.Instance);

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
}
