using Fietslog.Worker;

namespace Fietslog.Worker.Tests;

public class MessagesTests
{
    [Fact]
    public void Describes_ride_in_dutch_format()
    {
        var ride = new Ride(new DateOnly(2026, 9, 28), 20, 3154, 22.8);

        Assert.Equal("20 km op 28-09-2026 · 52:34 · 22,8 km/u", Messages.Describe(ride));
    }

    [Fact]
    public void Describes_distance_only_ride()
    {
        var ride = new Ride(new DateOnly(2026, 8, 30), 20.5, null, null);

        Assert.Equal("20,5 km op 30-08-2026", Messages.Describe(ride));
    }

    [Fact]
    public void Describes_time_and_speed_mismatch()
    {
        var ride = new Ride(new DateOnly(2026, 9, 28), 20, 3154, 60);

        Assert.Equal(
            "⚠️ Tijd en snelheid komen niet overeen: 20 km in 52:34 is gemiddeld 22,8 km/u, niet 60,0 km/u. " +
            "Beide zijn opgeslagen zoals ingevoerd.",
            Messages.TimeSpeedMismatch(ride));
    }

    [Theory]
    [InlineData(65, "1:05")]
    [InlineData(3154, "52:34")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3730, "1:02:10")]
    [InlineData(90061, "25:01:01")]
    public void Formats_duration(int seconds, string expected)
    {
        Assert.Equal(expected, Messages.FormatDuration(seconds));
    }
}
