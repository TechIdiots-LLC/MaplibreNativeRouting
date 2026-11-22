using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MaplibreNative.Routing.Core.Utils;

/// <summary>
/// Road names as people say and write them: "Elm St." is Elm Street, "Rt 9" and "State Route 9" are the road
/// numbered MA 9, "Elm" alone is Elm Street, and "Pheasant Street" is a mishearing of Pleasant Street. Shared by
/// <see cref="Routing.RoadSearch"/> and the street names the offline router gives its steps.
/// </summary>
public static partial class RoadNames
{
    private static readonly Dictionary<string, string> Suffix = new()
    {
        ["st"] = "street", ["rd"] = "road", ["ave"] = "avenue", ["av"] = "avenue", ["dr"] = "drive", ["ln"] = "lane",
        ["rt"] = "route", ["rte"] = "route", ["hwy"] = "highway", ["blvd"] = "boulevard", ["ct"] = "court",
        ["pl"] = "place", ["pkwy"] = "parkway", ["cir"] = "circle", ["ter"] = "terrace", ["tpke"] = "turnpike",
        ["expy"] = "expressway", ["fwy"] = "freeway", ["sq"] = "square", ["trl"] = "trail", ["ext"] = "extension",
        ["n"] = "north", ["s"] = "south", ["e"] = "east", ["w"] = "west",
    };

    // The kind of road, at the end of its name: "Elm" is what tells Elm Street from Oak Street
    private static readonly HashSet<string> Types =
    [
        "street", "road", "avenue", "drive", "lane", "boulevard", "court", "place", "parkway", "circle", "terrace",
        "way", "trail", "turnpike", "pike", "expressway", "freeway", "square", "row", "loop", "extension", "alley",
        "highway",
    ];

    private static readonly HashSet<string> Dirs =
        ["north", "south", "east", "west", "northeast", "northwest", "southeast", "southwest"];

    private static readonly HashSet<string> Numbered = ["route", "highway", "interstate", "i", "us"];

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotWord();

    [GeneratedRegex(@"^\d+[a-z]?$")]
    private static partial Regex Number();

    [GeneratedRegex(@"^\d+(st|nd|rd|th)$")]
    private static partial Regex Ordinal();

    [GeneratedRegex(@"^\s*([a-z]+)[\s-]*(\d+[a-z]?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RefParts();

    [GeneratedRegex(@"\b(?:and|at|on|near|by|off|corner of)\b")]
    private static partial Regex Joiners();

    [GeneratedRegex(@"^US:([A-Z]{2})$")]
    private static partial Regex StateNetwork();

    /// <summary>Words, lowercased, accents and street abbreviations spelled out: "Elm St." is "elm street".</summary>
    public static string Words(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var plain = new StringBuilder(s.Length);
        foreach (char c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) plain.Append(c);
        }
        return string.Join(' ', NotWord().Split(plain.ToString().ToLowerInvariant())
            .Where(w => w.Length > 0).Select(w => Suffix.GetValueOrDefault(w, w)));
    }

    /// <summary>
    /// A name without its kind and directions ("north elm street" is "elm"), for a name said without them or
    /// misheard; null for a number, which is only ever itself (Route 8 isn't a mishearing of Route 9).
    /// </summary>
    public static string? Base(string key)
    {
        var t = key.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        bool typed = false;
        while (true)
        {
            if (t.Count > 1 && Dirs.Contains(t[0])) t.RemoveAt(0);
            else if (t.Count > 1 && Dirs.Contains(t[^1])) t.RemoveAt(t.Count - 1);
            else if (t.Count > 1 && !typed && Types.Contains(t[^1])) { t.RemoveAt(t.Count - 1); typed = true; }
            else break;
        }
        // North Street is North Street, not "street"
        if (t.All(w => Types.Contains(w) || Dirs.Contains(w)))
            return key.Split(' ').Any(w => Number().IsMatch(w)) ? null : key;
        if (t.Any(w => Number().IsMatch(w) && !Ordinal().IsMatch(w))) return null;
        return string.Join(' ', t);
    }

    /// <summary>The ways a road number is said: "MA 9" is Route 9, "I 290" Interstate 290. Several refs with ";".</summary>
    public static IReadOnlyList<string> RefNames(string? @ref)
    {
        var out_ = new List<string>();
        foreach (var one in (@ref ?? "").Split(';'))
        {
            var m = RefParts().Match(one);
            if (!m.Success)
            {
                var w = Words(one);
                if (w.Length > 0) out_.Add(w);
                continue;
            }
            string p = m.Groups[1].Value.ToLowerInvariant();
            string n = m.Groups[2].Value.ToLowerInvariant();
            if (p == "i") out_.AddRange([$"i {n}", $"interstate {n}", $"route {n}", $"highway {n}"]);
            else if (p == "us") out_.AddRange([$"us {n}", $"us route {n}", $"us highway {n}", $"route {n}", $"highway {n}"]);
            else if (p is "cr" or "co" or "c") out_.AddRange([$"county road {n}", $"county route {n}", $"route {n}"]);
            else out_.AddRange([$"{p} {n}", $"route {n}", $"state route {n}", $"highway {n}", $"state highway {n}"]);
        }
        return out_;
    }

    /// <summary>
    /// A road number as it's written ("MA 31", "I 190", "US 20") from OpenMapTiles' transportation_name, which gives
    /// a bare <c>ref</c> ("31") with its <c>network</c> and <c>route_1_network</c> ("US:MA").
    /// </summary>
    public static string? FormatRef(IReadOnlyDictionary<string, object> props)
    {
        string @ref = props.TryGetValue("ref", out var r) ? r?.ToString()?.Trim() ?? "" : "";
        if (@ref.Length == 0) return null;
        if (!Number().IsMatch(@ref.ToLowerInvariant())) return @ref;
        string network = props.TryGetValue("network", out var n) ? n?.ToString() ?? "" : "";
        if (network == "us-interstate") return $"I {@ref}";
        if (network == "us-highway") return $"US {@ref}";
        string? state = null;
        if (props.TryGetValue("route_1_network", out var rn) && StateNetwork().Match(rn?.ToString() ?? "") is { Success: true } sm)
            state = sm.Groups[1].Value;
        return $"{state ?? (network == "us-state" ? "SR" : "Route")} {@ref}";
    }

    /// <summary>Every key a road is found by: its name, and its number however it's said.</summary>
    public static IReadOnlyList<string> Keys(string? name, string? @ref)
    {
        var keys = new List<string>();
        var w = Words(name);
        if (w.Length > 0) keys.Add(w);
        foreach (var k in RefNames(@ref)) if (!keys.Contains(k)) keys.Add(k);
        return keys;
    }

    /// <summary>The road a phrase names: "412 Elm St" is "elm street"; "Route 9" keeps its number.</summary>
    public static string Phrase(string s)
    {
        var w = Words(s).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', w.Where((x, i) => !(i < w.Length - 1 && Number().IsMatch(x) && (i == 0 || !Numbered.Contains(w[i - 1])))));
    }

    /// <summary>The roads an address names: "Main St and Route 9" is two, a corner.</summary>
    public static IReadOnlyList<string> AddressRoads(string address) =>
        Joiners().Split(Words(address)).Select(p => Phrase(p.Trim())).Where(p => p.Length > 0).ToList();

    /// <summary>Whether a phrase is only kind and direction words ("street"), which says no road in particular.</summary>
    public static bool IsOnlyKind(string key) =>
        key.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => Types.Contains(w) || Dirs.Contains(w));

    /// <summary>How far a misheard name can be from the real one: none for a short one, which would match anything.</summary>
    public static int Slack(string @base) => @base.Length >= 8 ? 2 : @base.Length >= 5 ? 1 : 0;

    /// <summary>How many edits apart two strings are, or <paramref name="max"/> + 1 once that's certain.</summary>
    public static int Distance(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            int low = i;
            for (int j = 1; j <= b.Length; j++)
            {
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                low = Math.Min(low, cur[j]);
            }
            if (low > max) return max + 1;
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
