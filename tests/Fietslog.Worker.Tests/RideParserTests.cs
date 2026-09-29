using Fietslog.Worker;

namespace Fietslog.Worker.Tests;

public class RideParserTests
{
    private static readonly DateOnly Today = new(2026, 9, 28);

    public static TheoryData<string, string, double, int?, double?> ValidInputs => new()
    {
        // input, expected date, km, seconds, km/h
        { "16km", "2026-09-28", 16, null, null },
        { "20km@52:34", "2026-09-28", 20, 3154, 22.83 },
        { "20km@23.3km/h", "2026-09-28", 20, 3090, 23.3 },
        { "20km@23,3km/u", "2026-09-28", 20, 3090, 23.3 },
        { "20km@52:12@24km/h", "2026-09-28", 20, 3132, 24 },
        { "20km@24km/h@52:12", "2026-09-28", 20, 3132, 24 },
        { "20,5km@1:02:10 30-08-2026", "2026-08-30", 20.5, 3730, 19.79 },
        { "2026-08-30 16km", "2026-08-30", 16, null, null },
        { "16km 2026-09-28", "2026-09-28", 16, null, null },
        { "  20KM @ 52:34  ", "2026-09-28", 20, 3154, 22.83 },
        { "20 km@23,3 km/u", "2026-09-28", 20, 3090, 23.3 },
        { "16km@40:00", "2026-09-28", 16, 2400, 24 },
        { "20km@75:00", "2026-09-28", 20, 4500, 16 },
        { "1000km", "2026-09-28", 1000, null, null },
        // speed limits are inclusive
        { "1km@1km/h", "2026-09-28", 1, 3600, 1 },
        { "20km@100km/u", "2026-09-28", 20, 720, 100 },
        { "100km@1:00:00", "2026-09-28", 100, 3600, 100 },
        { "1km@1:00:00", "2026-09-28", 1, 3600, 1 },
    };

    [Theory]
    [MemberData(nameof(ValidInputs))]
    public void Parses_valid_input(string input, string date, double km, int? seconds, double? speed)
    {
        var result = RideParser.Parse(input, Today);

        var ride = Assert.IsType<ParseResult.Success>(result).Ride;
        Assert.Equal(DateOnly.Parse(date), ride.Date);
        Assert.Equal(km, ride.DistanceKm, 6);
        Assert.Equal(seconds, ride.DurationSeconds);
        if (speed is null)
            Assert.Null(ride.AvgSpeedKmh);
        else
            Assert.Equal(speed.Value, ride.AvgSpeedKmh!.Value, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("hallo")]
    [InlineData("km")]
    [InlineData("20")]
    [InlineData("0km")]
    [InlineData("1000.5km")]
    [InlineData("20km@52:75")]
    [InlineData("20km@1:60:00")]
    [InlineData("20km@0:00")]
    [InlineData("20km@0km/h")]
    [InlineData("20km@0,5km/h")]
    [InlineData("20km@100,1km/h")]
    [InlineData("20km@52:34@101km/h")]
    [InlineData("1000km@0,0001km/u")]
    [InlineData("1km@9999km/u")]
    [InlineData("16km@1:05")]
    [InlineData("1km@0:01")]
    [InlineData("20km@100:00:00")]
    [InlineData("0,001km@100km/u")]
    [InlineData("20km@fast")]
    [InlineData("20km@")]
    [InlineData("20km@52:34@53:00")]
    [InlineData("20km@23km/h@24km/h")]
    [InlineData("20km@52:34@24km/h@1:00")]
    [InlineData("16km 2026-09-29")]
    [InlineData("16km 29-09-2026")]
    [InlineData("16km 31-02-2026")]
    [InlineData("16km 2026-08-01 2026-08-02")]
    [InlineData("16km extra")]
    [InlineData("16km 16km")]
    [InlineData("2026-08-30")]
    [InlineData("20mi")]
    public void Rejects_invalid_input(string? input)
    {
        var result = RideParser.Parse(input, Today);

        var failure = Assert.IsType<ParseResult.Failure>(result);
        Assert.False(string.IsNullOrWhiteSpace(failure.Error));
    }

    [Fact]
    public void Two_part_time_is_minutes_and_seconds()
    {
        var ride = Assert.IsType<ParseResult.Success>(RideParser.Parse("1km@3:05", Today)).Ride;

        Assert.Equal(185, ride.DurationSeconds);
    }

    [Fact]
    public void Implausible_computed_speed_is_reported()
    {
        var failure = Assert.IsType<ParseResult.Failure>(RideParser.Parse("16km@1:05", Today));

        Assert.StartsWith("Berekende snelheid van 886,2 km/u ligt niet tussen 1 en 100 km/u.", failure.Error);
    }
}
