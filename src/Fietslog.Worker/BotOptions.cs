using System.ComponentModel.DataAnnotations;

namespace Fietslog.Worker;

public sealed class BotOptions
{
    public const string SectionName = "Bot";

    [Required]
    [RegularExpression(@"^\d+:[A-Za-z0-9_-]+$", ErrorMessage = "Bot:Token must look like '123456:ABC-def_...' (from @BotFather).")]
    public string Token { get; set; } = "";

    /// <summary>Numeric Telegram user ID of the only person allowed to log rides.</summary>
    [Range(1, long.MaxValue)]
    public long AllowedUserId { get; set; }

    [Required]
    public string DatabasePath { get; set; } = "/data/fietslog.db";

    /// <summary>IANA time zone used to determine "today".</summary>
    [Required]
    public string TimeZone { get; set; } = "Europe/Amsterdam";
}
