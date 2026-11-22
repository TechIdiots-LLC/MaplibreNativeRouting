using System.Globalization;

namespace MaplibreNative.Routing.Core.Utils;

/// <summary>The units distances are given in.</summary>
public enum DistanceUnits
{
    /// <summary>Metres and kilometres.</summary>
    Metric,
    /// <summary>Feet and miles (the US).</summary>
    Imperial,
    /// <summary>Yards and miles (the UK).</summary>
    UK,
}

/// <summary>
/// Distances as a driver wants to hear and read them: rounded the further they are ("400 metres", not "387"),
/// in the units of the region. Ported from maplibre-navigation-ios's DistanceFormatter rounding tables (NOTICE.md).
/// </summary>
public sealed class DistanceFormatter
{
    private const double MetersPerMile = 1609.344;
    private const double FeetPerMeter = 3.28084;
    private const double YardsPerMeter = 1.09361;

    private readonly record struct Threshold(double MaximumMeters, double RoundingIncrement, string Unit, int FractionDigits);

    // The last threshold of each is used beyond it
    private static readonly Threshold[] Metric =
    [
        new(25, 5, "meter", 0), new(100, 25, "meter", 0), new(999, 50, "meter", 0),
        new(3000, 0, "kilometer", 1), new(5000, 0, "kilometer", 0),
    ];

    private static readonly Threshold[] UK =
    [
        new(20 / YardsPerMeter, 10, "yard", 0), new(100 / YardsPerMeter, 25, "yard", 0),
        new(0.1 * MetersPerMile, 50, "yard", 0), new(3 * MetersPerMile, 0.1, "mile", 1), new(5 * MetersPerMile, 0, "mile", 0),
    ];

    private static readonly Threshold[] Imperial =
    [
        new(0.1 * MetersPerMile, 50, "foot", 0), new(3 * MetersPerMile, 0.1, "mile", 1), new(5 * MetersPerMile, 0, "mile", 0),
    ];

    public DistanceFormatter(DistanceUnits? units = null) => Units = units ?? UnitsForRegion(RegionInfo.CurrentRegion);

    public DistanceUnits Units { get; }

    /// <summary>The units a region measures roads in: miles in the US, Liberia and Myanmar; miles and yards in the UK.</summary>
    public static DistanceUnits UnitsForRegion(RegionInfo region) => region.TwoLetterISORegionName switch
    {
        "US" or "LR" or "MM" => DistanceUnits.Imperial,
        "GB" => DistanceUnits.UK,
        _ => DistanceUnits.Metric,
    };

    /// <summary>For speech: "400 meters", "1.5 kilometers", "500 feet", "0.3 miles".</summary>
    public string Spoken(double meters)
    {
        var (value, unit) = Round(meters);
        string number = value.ToString(value % 1 == 0 ? "0" : "0.0", CultureInfo.InvariantCulture);
        return $"{number} {Plural(unit, value)}";
    }

    /// <summary>For a screen: "400 m", "1.5 km", "500 ft", "0.3 mi".</summary>
    public string Short(double meters)
    {
        var (value, unit) = Round(meters);
        string number = value.ToString(value % 1 == 0 ? "0" : "0.0", CultureInfo.InvariantCulture);
        string abbr = unit switch { "meter" => "m", "kilometer" => "km", "foot" => "ft", "yard" => "yd", _ => "mi" };
        return $"{number} {abbr}";
    }

    private (double Value, string Unit) Round(double meters)
    {
        var table = Units switch { DistanceUnits.Imperial => Imperial, DistanceUnits.UK => UK, _ => Metric };
        var t = table.FirstOrDefault(x => meters < x.MaximumMeters, table[^1]);
        double value = t.Unit switch
        {
            "kilometer" => meters / 1000,
            "mile" => meters / MetersPerMile,
            "foot" => meters * FeetPerMeter,
            "yard" => meters * YardsPerMeter,
            _ => meters,
        };
        if (t.RoundingIncrement > 0) value = Math.Round(value / t.RoundingIncrement) * t.RoundingIncrement;
        value = Math.Round(value, t.FractionDigits);
        // Never "0 feet": the least is one increment
        if (value <= 0) value = t.RoundingIncrement > 0 ? t.RoundingIncrement : Math.Pow(10, -t.FractionDigits);
        return (value, t.Unit);
    }

    private static string Plural(string unit, double value) =>
        value == 1 ? unit : unit switch { "foot" => "feet", _ => unit + "s" };
}
