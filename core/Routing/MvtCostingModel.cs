using MaplibreNative.Routing.Core.Models;

namespace MaplibreNative.Routing.Core.Routing;

internal sealed class MvtCostingModel
{
    /// <summary>The class given the user's own trails when they're routed on with the roads (HybridGraphRouter).</summary>
    public const string TrailClass = "trail";

    private static readonly string[] AllClasses =
        [TrailClass, "motorway", "trunk", "primary", "secondary", "tertiary", "minor", "service", "track", "path", "ferry"];

    private static readonly HashSet<string> UnpavedSurfaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "unpaved"
    };

    private readonly RouteProfile _profile;
    private readonly double _useHighways;
    private readonly double _useFerry;
    private readonly double _maxSpeedKmh;
    private readonly RoadTypes _avoid;

    public MvtCostingModel(RouteProfile profile, double useHighways = 0.1, double useFerry = 0.0, RoadTypes avoid = RoadTypes.None)
    {
        _profile = profile;
        _avoid = avoid;
        _useHighways = Math.Clamp(useHighways, 0, 1);
        _useFerry = Math.Clamp(useFerry, 0, 1);
        // The heuristic's speed: the cheapest way per metre, the speed over the preference of the best class
        _maxSpeedKmh = AllClasses.Select(c => GetSpeed(c, null) / Preference(c)).Max();
    }

    private bool IsHybridMoto => _profile is RouteProfile.HybridOfflineMotorcycle or RouteProfile.HybridMotorcycle;
    private bool IsHybridBike => _profile is RouteProfile.HybridOfflineBicycle or RouteProfile.HybridBicycle;
    private bool IsMotor => IsHybridMoto || _profile is RouteProfile.OfflineAuto or RouteProfile.Auto
                                                    or RouteProfile.Motorcycle;
    private const double ServiceFactor = 3;

    /// <summary>
    /// How much more a metre of a road costs than its time, on the hybrid profiles: the user's trails are the way,
    /// rural roads the way round a gap or a closure, bigger roads a last resort.
    /// </summary>
    private double Preference(string roadClass)
    {
        if (!IsHybridMoto && !IsHybridBike) return 1;
        return roadClass switch
        {
            TrailClass => 1,
            "track" or "minor" or "service" or "path" => 2,
            "tertiary" or "secondary" => 3,
            _ => 4,
        };
    }

    public double MaxSpeedMps => _maxSpeedKmh / 3.6;

    /// <summary>The kind of road a class is, for <see cref="RouteOptions.AvoidRoads"/>; the user's trails are none.</summary>
    internal static RoadTypes TypeOf(string roadClass) => roadClass switch
    {
        "motorway" or "trunk" => RoadTypes.Highways,
        "primary" or "secondary" => RoadTypes.MainRoads,
        "tertiary" or "minor" => RoadTypes.LocalRoads,
        "service" => RoadTypes.ServiceRoads,
        "track" => RoadTypes.Tracks,
        "path" => RoadTypes.Paths,
        "ferry" => RoadTypes.Ferries,
        _ => RoadTypes.None,
    };

    public bool IsTraversable(string roadClass, string? subclass, bool accessRestricted, string? surface = null)
    {
        if (accessRestricted) return false;
        // Roads only: the transportation layer has railways (rail, transit), raceways and busways too
        if (Array.IndexOf(AllClasses, roadClass) < 0) return false;
        // Left out by the user
        if ((_avoid & TypeOf(roadClass)) != 0) return false;
        if ((_avoid & RoadTypes.Unpaved) != 0 && roadClass != TrailClass && surface is not null && UnpavedSurfaces.Contains(surface))
            return false;
        // OpenMapTiles' roads being built: minor_construction, primary_construction…
        if (roadClass.EndsWith("_construction", StringComparison.Ordinal)) return false;

        return _profile switch
        {
            RouteProfile.OfflineAuto or RouteProfile.Auto => roadClass switch
            {
                "path" or "track" => false,
                "ferry" => _useFerry > 0,
                _ => true,
            },
            RouteProfile.OfflineBicycle or RouteProfile.Bicycle => roadClass switch
            {
                "motorway" or "trunk" => false,
                "path" when subclass is "steps" => false,
                "ferry" => _useFerry > 0,
                _ => true,
            },
            RouteProfile.OfflinePedestrian or RouteProfile.Pedestrian => roadClass switch
            {
                "motorway" or "trunk" => false,
                "ferry" => false,
                _ => true,
            },
            // Trail bikes: the trails, and roads to link them, but not footpaths or motorways
            RouteProfile.HybridOfflineMotorcycle or RouteProfile.HybridMotorcycle => roadClass switch
            {
                "motorway" or "path" => false,
                "ferry" => _useFerry > 0,
                _ => true,
            },
            RouteProfile.HybridOfflineBicycle or RouteProfile.HybridBicycle => roadClass switch
            {
                "motorway" or "trunk" => false,
                "path" when subclass is "steps" => false,
                "ferry" => _useFerry > 0,
                _ => true,
            },
            _ => true,
        };
    }

    public double GetSpeed(string roadClass, string? subclass)
    {
        return _profile switch
        {
            RouteProfile.OfflineAuto or RouteProfile.Auto => roadClass switch
            {
                "motorway" => 110,
                "trunk" => 90,
                "primary" => 70,
                "secondary" => 60,
                "tertiary" => 50,
                "minor" => 40,
                "service" => 25,
                "ferry" => 15,
                _ => 30,
            },
            RouteProfile.OfflineBicycle or RouteProfile.Bicycle => roadClass switch
            {
                "primary" => 20,
                "secondary" => 22,
                "tertiary" => 24,
                "minor" => 20,
                "service" => 15,
                "track" => 12,
                "path" when subclass is "cycleway" => 18,
                "path" when subclass is "footway" => 8,
                "path" => 10,
                "ferry" => 15,
                _ => 18,
            },
            RouteProfile.OfflinePedestrian or RouteProfile.Pedestrian => roadClass switch
            {
                "path" when subclass is "steps" => 3,
                "track" => 4,
                "path" => 4,
                _ => 5,
            },
            RouteProfile.HybridOfflineMotorcycle or RouteProfile.HybridMotorcycle => roadClass switch
            {
                TrailClass => 25,
                "track" => 25,
                "service" => 20,
                "minor" => 40,
                "tertiary" => 50,
                "secondary" => 60,
                "primary" => 70,
                "trunk" => 80,
                "ferry" => 15,
                _ => 30,
            },
            RouteProfile.HybridOfflineBicycle or RouteProfile.HybridBicycle => roadClass switch
            {
                TrailClass => 12,
                "primary" => 20,
                "secondary" => 22,
                "tertiary" => 24,
                "minor" => 20,
                "service" => 15,
                "track" => 12,
                "path" when subclass is "cycleway" => 18,
                "path" => 10,
                "ferry" => 15,
                _ => 18,
            },
            _ => 30,
        };
    }

    public double ComputeCost(double distanceMeters, string roadClass, string? subclass,
        string? surface, bool isToll, bool isRamp)
    {
        double speedKmh = GetSpeed(roadClass, subclass);
        if (speedKmh <= 0) return double.MaxValue;

        double baseSeconds = distanceMeters / (speedKmh / 3.6);

        // Highways avoided as asked. A trunk is often a region's only two-lane road (NH 16 through Pinkham Notch):
        // the hybrid profiles weigh it by size already (Preference), so only their motorways are avoided twice
        if (roadClass is "motorway" || (roadClass is "trunk" && !IsHybridMoto && !IsHybridBike))
            baseSeconds *= 1.0 + (1.0 - _useHighways) * 9.0;

        // A service road (parking, a driveway, a gas station) is a way in or out, not a way through: cheaper than the
        // road beside it, a route hopped through every parking lot along it
        if (roadClass is "service" && IsMotor)
            baseSeconds *= ServiceFactor;

        if (roadClass is "ferry")
            baseSeconds *= 1.0 + (1.0 - _useFerry) * 9.0;

        bool isAuto = _profile is RouteProfile.OfflineAuto or RouteProfile.Auto;
        bool isBicycle = _profile is RouteProfile.OfflineBicycle or RouteProfile.Bicycle;

        if (isToll)
            baseSeconds *= isAuto ? 1.5 : isBicycle ? 2.0 : 1.0;

        if (surface != null && UnpavedSurfaces.Contains(surface))
            baseSeconds *= isAuto ? 1.3 : isBicycle ? 1.5 : 1.0;

        return baseSeconds * Preference(roadClass);
    }

}
