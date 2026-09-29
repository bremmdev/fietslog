using System.Globalization;
using System.Text.RegularExpressions;

namespace Fietslog.Worker;

public abstract record ParseResult
{
    /// <param name="TimeSpeedMismatch">
    /// Both time and speed were typed and they do not match the distance, even allowing for rounding.
    /// </param>
    public sealed record Success(Ride Ride, bool TimeSpeedMismatch = false) : ParseResult;

    /// <param name="Error">Dutch description of what was wrong.</param>
    public sealed record Failure(string Error) : ParseResult;
}

/// <summary>
/// Parses messages like <c>20km@52:34 30-08-2026</c>: a ride token
/// (<c>&lt;km&gt;km[@&lt;time|speed&gt;][@&lt;time|speed&gt;]</c>) plus an optional date,
/// separated by whitespace in either order.
/// </summary>
public static partial class RideParser
{
    public const double MaxDistanceKm = 1000;

    // Plausible average cycling speeds; catches typos such as 1:05 meant as 1 hour 5 minutes.
    public const double MinSpeedKmh = 1;
    public const double MaxSpeedKmh = 100;

    // Loose on purpose: the time is often read off a watch mid-ride, and a missed warning is better than a false one.
    private const int MismatchTimeMarginSeconds = 60;
    private const double MismatchSpeedMarginKmh = 0.5;

    private const string Number = @"(\d+(?:[.,]\d+)?)";

    [GeneratedRegex(@"\s*@\s*")]
    private static partial Regex AtSpacing();

    // "20 km" -> "20km", "23,3 km/h" -> "23,3km/h"
    [GeneratedRegex(@"(\d)\s+km")]
    private static partial Regex NumberKmSpacing();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex($"^{Number}km$")]
    private static partial Regex Distance();

    [GeneratedRegex($"^{Number}km/[hu]$")]
    private static partial Regex Speed();

    // h:mm:ss or mm:ss (minutes may exceed 59 in the two-part form)
    [GeneratedRegex(@"^(?:(\d{1,3}):(\d{2})|(\d{1,4})):(\d{2})$")]
    private static partial Regex Duration();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"^\d{2}-\d{2}-\d{4}$")]
    private static partial Regex DutchDate();

    [GeneratedRegex(@"^\d")]
    private static partial Regex StartsWithDigit();

    public static ParseResult Parse(string? text, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Fail("Leeg bericht.");

        var normalized = text.Trim().ToLowerInvariant();
        normalized = AtSpacing().Replace(normalized, "@");
        normalized = NumberKmSpacing().Replace(normalized, "$1km");
        var tokens = Whitespace().Split(normalized);

        string? rideToken = null;
        DateOnly? date = null;

        foreach (var token in tokens)
        {
            if (IsoDate().IsMatch(token) || DutchDate().IsMatch(token))
            {
                if (date is not null)
                    return Fail("Meer dan één datum.");
                var format = IsoDate().IsMatch(token) ? "yyyy-MM-dd" : "dd-MM-yyyy";
                if (!DateOnly.TryParseExact(token, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    return Fail($"Ongeldige datum: '{token}'.");
                date = d;
            }
            else if (StartsWithDigit().IsMatch(token) && token.Contains("km"))
            {
                if (rideToken is not null)
                    return Fail("Meer dan één rit in één bericht.");
                rideToken = token;
            }
            else
            {
                return Fail($"Onbekende invoer: '{token}'.");
            }
        }

        if (rideToken is null)
            return Fail("Afstand ontbreekt.");

        var rideDate = date ?? today;
        if (rideDate > today)
            return Fail($"Datum {rideDate:dd-MM-yyyy} ligt in de toekomst.");

        var parts = rideToken.Split('@');

        var distanceMatch = Distance().Match(parts[0]);
        if (!distanceMatch.Success)
            return Fail($"Ongeldige afstand: '{parts[0]}'.");
        var distanceText = distanceMatch.Groups[1].Value;
        var distance = ParseNumber(distanceText);
        if (distance <= 0 || distance > MaxDistanceKm)
            return Fail($"Afstand moet tussen 0 en {MaxDistanceKm:0} km liggen.");

        int? duration = null;
        double? speed = null;

        foreach (var part in parts.Skip(1))
        {
            if (Speed().Match(part) is { Success: true } speedMatch)
            {
                if (speed is not null)
                    return Fail("Snelheid staat er meer dan één keer in.");
                speed = ParseNumber(speedMatch.Groups[1].Value);
                if (speed is < MinSpeedKmh or > MaxSpeedKmh)
                    return Fail($"Snelheid moet tussen {MinSpeedKmh:0} en {MaxSpeedKmh:0} km/u liggen.");
            }
            else if (Duration().Match(part) is { Success: true } durationMatch)
            {
                if (duration is not null)
                    return Fail("Tijd staat er meer dan één keer in.");
                duration = ParseDuration(durationMatch);
                if (duration is null)
                    return Fail($"Ongeldige tijd: '{part}'.");
            }
            else
            {
                return Fail($"Onbekend onderdeel: '{part}'. Gebruik een tijd (52:34) of snelheid (23,3km/u).");
            }
        }

        // Derive the missing one; when both are given they are kept as typed but checked against each other.
        var mismatch = false;
        if (duration is int s && speed is null)
        {
            speed = Math.Round(distance / (s / 3600.0), 2);
            if (speed is < MinSpeedKmh or > MaxSpeedKmh)
                return Fail(
                    $"Berekende snelheid van {FormatNumber(speed.Value)} km/u ligt niet tussen {MinSpeedKmh:0} en {MaxSpeedKmh:0} km/u. " +
                    "Controleer afstand en tijd (mm:ss of u:mm:ss).");
        }
        else if (speed is double v && duration is null)
        {
            duration = (int)Math.Round(distance / v * 3600);
            if (duration < 1)
                return Fail("Berekende tijd is korter dan 1 seconde. Controleer afstand en snelheid.");
        }
        else if (duration is int typedSeconds && speed is double typedSpeed)
        {
            mismatch = !TimeAndSpeedMatch(distance, distanceText, typedSeconds, typedSpeed);
        }

        return new ParseResult.Success(new Ride(rideDate, distance, duration, speed), mismatch);
    }

    /// <summary>
    /// Whether the typed speed fits the typed distance and time, allowing the distance to be rounded to the
    /// digits typed (so <c>20km</c> may be 19.5–20.5), the time to be off by
    /// <see cref="MismatchTimeMarginSeconds"/> and the speed by <see cref="MismatchSpeedMarginKmh"/>.
    /// </summary>
    private static bool TimeAndSpeedMatch(double distance, string distanceText, int seconds, double speed)
    {
        var distanceMargin = RoundingMargin(distanceText);
        var longestHours = (seconds + MismatchTimeMarginSeconds) / 3600.0;
        var shortestHours = Math.Max(seconds - MismatchTimeMarginSeconds, 1) / 3600.0;
        var slowest = (distance - distanceMargin) / longestHours;
        var fastest = (distance + distanceMargin) / shortestHours;
        return speed + MismatchSpeedMarginKmh >= slowest && speed - MismatchSpeedMarginKmh <= fastest;
    }

    /// <summary>Half a unit of the last typed digit: 0.5 for "20", 0.05 for "20,5".</summary>
    private static double RoundingMargin(string number)
    {
        var separator = number.IndexOfAny(['.', ',']);
        var decimals = separator < 0 ? 0 : number.Length - separator - 1;
        return 0.5 * Math.Pow(10, -decimals);
    }

    private static int? ParseDuration(Match m)
    {
        int hours, minutes, seconds;
        if (m.Groups[1].Success)
        {
            hours = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            minutes = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (minutes >= 60)
                return null;
        }
        else
        {
            hours = 0;
            minutes = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        }
        seconds = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
        if (seconds >= 60)
            return null;

        var total = hours * 3600 + minutes * 60 + seconds;
        return total > 0 ? total : null;
    }

    private static double ParseNumber(string value) =>
        double.Parse(value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    private static string FormatNumber(double value) =>
        value.ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',');

    private static ParseResult.Failure Fail(string error) => new(error);
}
