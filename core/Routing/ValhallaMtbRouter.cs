// Valhalla HTTP routing client. Sends a route request to a Valhalla endpoint and
// converts the response into a DirectionsRoute.
//
// Valhalla API reference: https://valhalla.github.io/valhalla/api/turn-by-turn/api-reference/
// Highway avoidance: use_highways costing option (0.0 = impossible, 1.0 = neutral).
// Highway detection: Valhalla maneuver types 26 (EnterHighway is not in the enum —
// see ManeuverType.cs; we detect via road_class field on the maneuver object).
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Routing;

public class ValhallaMtbRouter : IRoutingEngine
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<DirectionsRoute?> RouteAsync(RouteOptions options)
    {
        var costing = options.Profile switch
        {
            RouteProfile.Motorcycle or RouteProfile.HybridMotorcycle => "motorcycle",
            RouteProfile.Bicycle or RouteProfile.HybridBicycle => "bicycle",
            RouteProfile.Pedestrian => "pedestrian",
            _ => "auto",
        };

        var costingOptions = new JsonObject
        {
            ["use_highways"] = options.UseHighways,
            ["use_ferry"] = options.UseFerry,
        };
        foreach (var kv in options.ValhallaExtras)
            costingOptions[kv.Key] = JsonValue.Create(kv.Value);

        var body = new JsonObject
        {
            ["locations"] = new JsonArray(
                new JsonObject { ["lat"] = options.Origin.Lat,      ["lon"] = options.Origin.Lon },
                new JsonObject { ["lat"] = options.Destination.Lat, ["lon"] = options.Destination.Lon }),
            ["costing"] = costing,
            ["costing_options"] = new JsonObject { [costing] = costingOptions },
            ["directions_type"] = "maneuvers",
            ["shape_format"] = "geojson",
            ["units"] = "kilometers",
        };

        HttpResponseMessage response;
        try
        {
            response = await Http.PostAsJsonAsync(options.ValhallaUrl, body,
                options.CancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new RoutingException($"Valhalla request failed: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(options.CancellationToken);
            throw new RoutingException($"Valhalla returned {(int)response.StatusCode}: {error}");
        }

        var json = await response.Content.ReadAsStringAsync(options.CancellationToken);
        return ParseResponse(json, options.Profile);
    }

    private static DirectionsRoute ParseResponse(string json, RouteProfile profile)
    {
        using var doc = JsonDocument.Parse(json);
        var trip = doc.RootElement.GetProperty("trip");

        double totalDist = trip.TryGetProperty("summary", out var ts) &&
                           ts.TryGetProperty("length", out var tsl)
            ? tsl.GetDouble() * 1000  // km → m
            : 0;
        double totalTime = ts.TryGetProperty("time", out var tst) ? tst.GetDouble() : 0;

        var legs = new List<RouteLeg>();
        foreach (var legEl in trip.GetProperty("legs").EnumerateArray())
        {
            var shape = ParseGeoJsonShape(legEl.GetProperty("shape"));
            var steps = ParseManeuvers(legEl.GetProperty("maneuvers"));
            var summary = legEl.TryGetProperty("summary", out var ls) ? ls : default;

            legs.Add(new RouteLeg
            {
                Distance = summary.TryGetProperty("length", out var ll) ? ll.GetDouble() * 1000 : 0,
                Duration = summary.TryGetProperty("time", out var lt) ? lt.GetDouble() : 0,
                Summary = summary.TryGetProperty("has_time_restrictions", out _) ? "" : "",
                Steps = steps,
                Shape = shape,
            });
        }

        var route = new DirectionsRoute
        {
            Distance = totalDist,
            Duration = totalTime,
            Legs = legs,
            Profile = profile,
        };

        return RouteUtils.AttachHighwayWarning(route);
    }

    private static IReadOnlyList<(double Lon, double Lat)> ParseGeoJsonShape(JsonElement shapeEl)
    {
        // shape_format=geojson → {"type":"LineString","coordinates":[[lon,lat],...]}
        // or just the coordinates array directly depending on Valhalla version
        JsonElement coords;
        if (shapeEl.ValueKind == JsonValueKind.Object &&
            shapeEl.TryGetProperty("coordinates", out var c))
            coords = c;
        else
            coords = shapeEl;

        var result = new List<(double, double)>();
        foreach (var pt in coords.EnumerateArray())
        {
            var arr = pt.EnumerateArray().ToArray();
            if (arr.Length >= 2)
                result.Add((arr[0].GetDouble(), arr[1].GetDouble()));
        }
        return result;
    }

    private static IReadOnlyList<LegStep> ParseManeuvers(JsonElement maneuversEl)
    {
        var steps = new List<LegStep>();
        foreach (var m in maneuversEl.EnumerateArray())
        {
            var type = m.TryGetProperty("type", out var t)
                ? (ManeuverType)t.GetInt32()
                : ManeuverType.None;

            var names = new List<string>();
            if (m.TryGetProperty("street_names", out var sn))
                foreach (var n in sn.EnumerateArray())
                    names.Add(n.GetString() ?? "");

            double lon = 0, lat = 0;
            if (m.TryGetProperty("begin_shape_index", out _))
            {
                // location resolved from shape at caller level; use 0/0 placeholder
            }
            if (m.TryGetProperty("lon", out var mlon) && m.TryGetProperty("lat", out var mlat))
            { lon = mlon.GetDouble(); lat = mlat.GetDouble(); }

            steps.Add(new LegStep
            {
                Distance = m.TryGetProperty("length", out var ml) ? ml.GetDouble() * 1000 : 0,
                Duration = m.TryGetProperty("time", out var mt) ? mt.GetDouble() : 0,
                StreetNames = names,
                Instruction = m.TryGetProperty("instruction", out var ins) ? ins.GetString() ?? "" : "",
                VerbalPreInstruction = m.TryGetProperty("verbal_pre_transition_instruction", out var vp)
                    ? vp.GetString() : null,
                VerbalPostInstruction = m.TryGetProperty("verbal_post_transition_instruction", out var vpo)
                    ? vpo.GetString() : null,
                Type = type,
                RoadClass = m.TryGetProperty("road_class", out var rc) ? rc.GetString() : null,
                BeginShapeIndex = m.TryGetProperty("begin_shape_index", out var bsi) ? bsi.GetInt32() : 0,
                EndShapeIndex = m.TryGetProperty("end_shape_index", out var esi) ? esi.GetInt32() : 0,
                ManeuverLocation = (lon, lat),
                TravelMode = m.TryGetProperty("travel_mode", out var tm) ? tm.GetString() : null,
            });
        }
        return steps;
    }
}
