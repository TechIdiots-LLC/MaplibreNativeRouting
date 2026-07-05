namespace MaplibreNative.Routing.Core.Models;

/// <summary>A complete routed path from origin to destination.</summary>
public record DirectionsRoute
{
    /// <summary>Total distance in meters.</summary>
    public double Distance { get; init; }

    /// <summary>Total estimated duration in seconds.</summary>
    public double Duration { get; init; }

    public IReadOnlyList<RouteLeg> Legs { get; init; } = [];

    /// <summary>The profile used to generate this route.</summary>
    public RouteProfile Profile { get; init; }

    /// <summary>Non-null when the route uses highway segments; contains warning detail.</summary>
    public HighwayWarning? HighwayWarning { get; init; }
}
