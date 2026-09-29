using Fietslog.Worker;
using Microsoft.Extensions.Options;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    o.UseUtcTimestamp = true;
});

builder.Services.AddOptions<BotOptions>()
    .BindConfiguration(BotOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(o => TimeZoneInfo.TryFindSystemTimeZoneById(o.TimeZone, out _), "Bot:TimeZone is not a known time zone.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new Database(sp.GetRequiredService<IOptions<BotOptions>>().Value.DatabasePath));
builder.Services.AddSingleton<RideRepository>();
builder.Services.AddSingleton<RideMessageHandler>();
builder.Services.AddSingleton<BotTokenVerifier>();
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
    new TelegramBotClient(sp.GetRequiredService<IOptions<BotOptions>>().Value.Token));
builder.Services.AddHostedService<TelegramPollingService>();

var host = builder.Build();

// Fail fast (non-zero exit, so Railway restarts/flags the deploy) on bad config or an unwritable volume.
host.Services.GetRequiredService<Database>().Initialize();

// Checked here rather than in the polling service: a failing hosted service stops the host with exit code 0.
if (!await host.Services.GetRequiredService<BotTokenVerifier>().VerifyAsync())
    return 1;

await host.RunAsync();
return 0;
