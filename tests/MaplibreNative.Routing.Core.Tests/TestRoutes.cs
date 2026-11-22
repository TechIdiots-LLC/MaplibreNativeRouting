using MaplibreNative.Routing.Core.Models;

namespace MaplibreNative.Routing.Core.Tests;

/// <summary>Routes for the tests.</summary>
internal static class TestRoutes
{
    // East along 42.35 for about 1.6 km, then a left turn north for about 1.1 km
    public static readonly (double Lon, double Lat)[] Shape =
        [(-71.86, 42.35), (-71.85, 42.35), (-71.84, 42.35), (-71.84, 42.355), (-71.84, 42.36)];

    /// <summary>The L: east along 42.35, then left and north; or a plain line through <paramref name="shape"/>.</summary>
    public static DirectionsRoute L(params (double Lon, double Lat)[] shape)
    {
        if (shape.Length == 0) shape = Shape;
        bool turns = shape.Length == Shape.Length;
        var steps = turns
            ? new List<LegStep>
            {
                new() { Type = ManeuverType.Start, BeginShapeIndex = 0, EndShapeIndex = 2, Distance = 1640, Duration = 120, Instruction = "Head east on Elm Street" },
                new() { Type = ManeuverType.Left, BeginShapeIndex = 2, EndShapeIndex = 4, Distance = 1110, Duration = 160, Instruction = "Turn left onto Main Street" },
                new() { Type = ManeuverType.Destination, BeginShapeIndex = 4, EndShapeIndex = 4, Instruction = "Arrive" },
            }
            : new List<LegStep>
            {
                new() { Type = ManeuverType.Start, BeginShapeIndex = 0, EndShapeIndex = shape.Length - 1, Distance = 500, Duration = 50 },
                new() { Type = ManeuverType.Destination, BeginShapeIndex = shape.Length - 1, EndShapeIndex = shape.Length - 1 },
            };
        var leg = new RouteLeg { Distance = turns ? 2750 : 500, Duration = turns ? 280 : 50, Steps = steps, Shape = shape };
        return new DirectionsRoute { Distance = leg.Distance, Duration = leg.Duration, Legs = [leg] };
    }
}
