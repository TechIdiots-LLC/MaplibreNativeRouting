namespace MaplibreNative.Routing.Core.Models;

/// <summary>Attached to a DirectionsRoute when the route includes motorway or trunk
/// segments, so the UI can warn the user and render those segments differently.</summary>
public class HighwayWarning
{
    /// <summary>Indices (into Legs[0].Steps) of steps that start or traverse a highway.</summary>
    public IReadOnlyList<int> HighwayStepIndices { get; init; } = [];

    /// <summary>Approximate total highway distance in meters.</summary>
    public double TotalHighwayDistanceMeters { get; init; }
}
