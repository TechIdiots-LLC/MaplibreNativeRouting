namespace MaplibreNative.Routing.Core.Models;

/// <summary>
/// Kinds of road, by OpenMapTiles' transportation classes, for <see cref="RouteOptions.AvoidRoads"/>. The user's own
/// trails (<see cref="RouteOptions.TrackFeatures"/>) aren't among them: they're always used.
/// </summary>
[Flags]
public enum RoadTypes
{
    None = 0,
    /// <summary>Motorways and trunk roads.</summary>
    Highways = 1 << 0,
    /// <summary>Primary and secondary roads.</summary>
    MainRoads = 1 << 1,
    /// <summary>Tertiary and minor roads: local streets and back roads.</summary>
    LocalRoads = 1 << 2,
    /// <summary>Service roads: parking, driveways, alleys.</summary>
    ServiceRoads = 1 << 3,
    /// <summary>Tracks: dirt, farm and forest roads.</summary>
    Tracks = 1 << 4,
    /// <summary>Paths: footpaths, cycleways, bridleways.</summary>
    Paths = 1 << 5,
    /// <summary>Any road with an unpaved surface, whatever its kind.</summary>
    Unpaved = 1 << 6,
    /// <summary>Ferries.</summary>
    Ferries = 1 << 7,
}
