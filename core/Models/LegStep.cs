namespace MaplibreNative.Routing.Core.Models;

/// <summary>One step (maneuver) within a route leg. Ported from maplibre-navigation-android
/// LegStep, adapted for Valhalla's maneuver object structure.</summary>
public class LegStep
{
    /// <summary>Distance of this step in meters.</summary>
    public double Distance { get; init; }

    /// <summary>Estimated travel time in seconds.</summary>
    public double Duration { get; init; }

    /// <summary>Primary street name(s) for this step.</summary>
    public IReadOnlyList<string> StreetNames { get; init; } = [];

    /// <summary>Human-readable instruction, e.g. "Turn right onto Main St".</summary>
    public string Instruction { get; init; } = "";

    /// <summary>Short verbal instruction for before the maneuver.</summary>
    public string? VerbalPreInstruction { get; init; }

    /// <summary>Short verbal instruction for after the maneuver.</summary>
    public string? VerbalPostInstruction { get; init; }

    public ManeuverType Type { get; init; }

    /// <summary>Valhalla road_class at this step's start edge.</summary>
    public string? RoadClass { get; init; }

    /// <summary>Start index into the parent leg's decoded coordinate array.</summary>
    public int BeginShapeIndex { get; init; }

    /// <summary>End index into the parent leg's decoded coordinate array.</summary>
    public int EndShapeIndex { get; init; }

    /// <summary>[lon, lat] of the maneuver point.</summary>
    public (double Lon, double Lat) ManeuverLocation { get; init; }

    public string? TravelMode { get; init; }
}
