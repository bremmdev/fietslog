namespace Fietslog.Worker;

/// <summary>A parsed ride. Duration and speed are either both set or both null.</summary>
public sealed record Ride(
    DateOnly Date,
    double DistanceKm,
    int? DurationSeconds,
    double? AvgSpeedKmh);
