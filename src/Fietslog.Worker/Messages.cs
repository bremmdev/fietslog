using System.Globalization;

namespace Fietslog.Worker;

/// <summary>Dutch bot replies.</summary>
public static class Messages
{
    public const string Examples = "16km · 20km@52:34 · 20km@23,3km/u · 20km@52:12@24km/u 30-08-2026";

    public const string Help =
        """
        Stuur een rit als: <afstand>km[@tijd][@snelheid] [datum]

        • Tijd: mm:ss (52:34) of u:mm:ss (1:05:12)
        • Snelheid: 23,3km/u of 23.3km/h
        • Datum (optioneel, standaard vandaag): 30-08-2026 of 2026-08-30

        Voorbeelden:
        16km
        20km@52:34
        20km@23,3km/u
        20km@52:12@24km/u 30-08-2026
        """;

    public const string SaveFailed = "⚠️ Er ging iets mis bij het opslaan. Probeer het later opnieuw.";

    public static string Saved(Ride ride) => $"✅ Opgeslagen: {Describe(ride)}";

    public static string AlreadySaved(Ride ride) => $"ℹ️ Al opgeslagen: {Describe(ride)}";

    public static string ParseError(string error) => $"❌ {error}\nVoorbeelden: {Examples}";

    public static string Describe(Ride ride)
    {
        var text = $"{FormatNumber(ride.DistanceKm, "0.##")} km op {ride.Date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}";
        if (ride.DurationSeconds is int seconds)
            text += $" · {FormatDuration(seconds)}";
        if (ride.AvgSpeedKmh is double speed)
            text += $" · {FormatNumber(speed, "0.0")} km/u";
        return text;
    }

    public static string FormatDuration(int totalSeconds)
    {
        var t = TimeSpan.FromSeconds(totalSeconds);
        return totalSeconds >= 3600
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }

    // Dutch decimal comma without depending on ICU culture data.
    private static string FormatNumber(double value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture).Replace('.', ',');
}
