namespace MaplibreNative.Routing.Core.Models;

/// <summary>One leg of a route (origin→waypoint or waypoint→destination).</summary>
public class RouteLeg
{
    /// <summary>Total distance of this leg in meters.</summary>
    public double Distance { get; init; }

    /// <summary>Total estimated duration in seconds.</summary>
    public double Duration { get; init; }

    /// <summary>Summary text (e.g. street names).</summary>
    public string Summary { get; init; } = "";

    /// <summary>Ordered maneuver steps.</summary>
    public IReadOnlyList<LegStep> Steps { get; init; } = [];

    /// <summary>Decoded geometry as [lon, lat] coordinate pairs, in step order.</summary>
    public IReadOnlyList<(double Lon, double Lat)> Shape { get; init; } = [];
}
