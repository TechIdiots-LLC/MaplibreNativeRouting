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

    public MvtCostingModel(RouteProfile profile, double useHighways = 0.1, double useFerry = 0.0)
    {
        _profile = profile;
        _useHighways = Math.Clamp(useHighways, 0, 1);
        _useFerry = Math.Clamp(useFerry, 0, 1);
        // The heuristic's speed: the cheapest way per metre, the speed over the preference of the best class
        _maxSpeedKmh = AllClasses.Select(c => GetSpeed(c, null) / Preference(c)).Max();
    }

    private bool IsHybridMoto => _profile is RouteProfile.HybridOfflineMotorcycle or RouteProfile.HybridMotorcycle;
    private bool IsHybridBike => _profile is RouteProfile.HybridOfflineBicycle or RouteProfile.HybridBicycle;

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

    public bool IsTraversable(string roadClass, string? subclass, bool accessRestricted)
    {
        if (accessRestricted) return false;
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

        if (roadClass is "motorway" or "trunk")
            baseSeconds *= 1.0 + (1.0 - _useHighways) * 9.0;

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
