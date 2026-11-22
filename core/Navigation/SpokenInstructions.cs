using System.Text.RegularExpressions;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Navigation;

/// <summary>
/// What to say, from a route's written steps: the routes here come without the voice instructions a route service's
/// carry, so they're made from each step's instruction, worded for speech ("Head SW on Main Street / MA 31" is said
/// "Head southwest on Main Street, Route 31").
/// </summary>
public static partial class SpokenInstructions
{
    private static readonly Dictionary<string, string> Compass = new()
    {
        ["N"] = "north", ["NE"] = "northeast", ["E"] = "east", ["SE"] = "southeast",
        ["S"] = "south", ["SW"] = "southwest", ["W"] = "west", ["NW"] = "northwest",
    };

    [GeneratedRegex(@"^Head (N|NE|E|SE|S|SW|W|NW)\b")]
    private static partial Regex HeadCompass();

    [GeneratedRegex(@"\bI[ -](\d+[A-Z]?)\b")]
    private static partial Regex Interstate();

    [GeneratedRegex(@"\bUS[ -](\d+[A-Z]?)\b")]
    private static partial Regex UsRoute();

    [GeneratedRegex(@"\b(?:[A-Z]{2}|SR|Route)[ -](\d+[A-Z]?)\b")]
    private static partial Regex StateRoute();

    /// <summary>A step's instruction, worded to be spoken.</summary>
    public static string Say(LegStep step)
    {
        string s = step.Instruction.Trim().TrimEnd('.');
        s = HeadCompass().Replace(s, m => "Head " + Compass[m.Groups[1].Value]);
        s = Interstate().Replace(s, "Interstate $1");
        s = UsRoute().Replace(s, "U.S. Route $1");
        s = StateRoute().Replace(s, "Route $1");
        return s.Replace(" / ", ", ");
    }

    /// <summary>A turn ahead: "In 400 feet, turn left onto Main Street", or, when it's close, the turn itself.</summary>
    public static string Maneuver(LegStep step, double distanceMeters, DistanceFormatter distances, bool imminent)
    {
        string say = Say(step);
        if (imminent || say.Length == 0) return say;
        return $"In {distances.Spoken(distanceMeters)}, {char.ToLowerInvariant(say[0])}{say[1..]}";
    }

    public const string Arrival = "You have arrived at your destination";
    public const string Rerouted = "Route recalculated";
}
