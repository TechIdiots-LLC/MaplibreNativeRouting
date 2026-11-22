using System.Diagnostics;
using System.Globalization;
using MapLibreNative.Maui.Handlers;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Routing;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing;

/// <summary>
/// Manages the MapLibre source/layer for the active route and optional alternate routes.
/// Primary route: blue. Alternate routes: grey, rendered below the primary.
/// </summary>
public class RouteOverlay
{
    private const string RouteSourceId   = "routing-route-src";
    private const string RouteLayerId    = "routing-route-line";
    private const string AltSourceId     = "routing-alt-src";
    private const string AltLayerId      = "routing-alt-line";
    private const string HighwaySourceId = "routing-highway-src";
    private const string HighwayLayerId  = "routing-highway-line";
    private const string PositionSourceId = "routing-position-src";
    private const string PositionLayerId  = "routing-position";
    private const string WaypointSourceId = "routing-waypoints-src";
    private const string WaypointLayerId  = "routing-waypoints";
    private const string WaypointLabelId  = "routing-waypoint-labels";

    private IMapLibreMapController? _controller;
    private readonly HashSet<string> _activeLayers = [];

    public void SetController(IMapLibreMapController? controller)
    {
        _controller = controller;
        _activeLayers.Clear();
    }

    /// <summary>Show a single route (no alternatives).</summary>
    public void ShowRoute(DirectionsRoute route) => ShowRoutes([route], 0);

    /// <summary>Show multiple routes. The route at <paramref name="selectedIndex"/> is
    /// rendered blue (primary); all others are rendered grey below it.</summary>
    public void ShowRoutes(IReadOnlyList<DirectionsRoute> routes, int selectedIndex)
    {
        if (_controller is null || routes.Count == 0) return;
        try
        {
            // ── Alternate routes (grey, below primary) ────────────────────────────
            var alts = routes.Where((_, i) => i != selectedIndex).ToList();
            if (alts.Count > 0)
            {
                SetOrAddSource(AltSourceId, AltLayerId, BuildAltsGeoJson(alts),
                    new Dictionary<string, object?>
                    {
                        ["line-color"]   = "#9E9E9E",
                        ["line-width"]   = 4.0,
                        ["line-opacity"] = 0.55,
                    });
            }
            else
            {
                TryRemove(AltLayerId, AltSourceId);
            }

            // ── Primary route (blue) ──────────────────────────────────────────────
            var primary = routes[selectedIndex];
            SetOrAddSource(RouteSourceId, RouteLayerId, BuildRouteGeoJson(primary),
                new Dictionary<string, object?>
                {
                    ["line-color"]   = "#1565C0",
                    ["line-width"]   = 5.0,
                    ["line-opacity"] = 0.9,
                });

            // ── Highway overlay (orange dashed) ───────────────────────────────────
            if (primary.HighwayWarning is { HighwayStepIndices.Count: > 0 })
            {
                SetOrAddSource(HighwaySourceId, HighwayLayerId, BuildHighwayGeoJson(primary),
                    new Dictionary<string, object?>
                    {
                        ["line-color"]     = "#FF6600",
                        ["line-width"]     = 5.0,
                        ["line-opacity"]   = 0.9,
                        ["line-dasharray"] = new object[] { 3.0, 2.0 },
                    });
            }
            else
            {
                TryRemove(HighwayLayerId, HighwaySourceId);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RouteOverlay] ShowRoutes threw: {ex}");
        }
    }

    /// <summary>
    /// A marker where the user is, for a position the map's own location puck doesn't show: a simulated drive's.
    /// Null removes it.
    /// </summary>
    public void ShowPosition(LocationFix? fix)
    {
        if (_controller is null) return;
        if (fix is not { } f)
        {
            TryRemove(PositionLayerId, PositionSourceId);
            return;
        }
        try
        {
            string geoJson = string.Create(CultureInfo.InvariantCulture,
                $"{{\"type\":\"FeatureCollection\",\"features\":[{{\"type\":\"Feature\",\"properties\":{{}},\"geometry\":{{\"type\":\"Point\",\"coordinates\":[{f.Lon:F6},{f.Lat:F6}]}}}}]}}");
            SetOrAddSource(PositionSourceId, PositionLayerId, geoJson, new Dictionary<string, object?>
            {
                ["circle-radius"]       = 8.0,
                ["circle-color"]        = "#1565C0",
                ["circle-stroke-color"] = "#FFFFFF",
                ["circle-stroke-width"] = 3.0,
            }, circle: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RouteOverlay] ShowPosition threw: {ex}");
        }
    }

    /// <summary>
    /// The points a route is planned through: the origin green ("A"), stops amber (numbered), the destination red
    /// ("B"). The labels need the style's glyphs; without them the circles still show. Null, or none, removes them.
    /// </summary>
    public void ShowWaypoints(RouteWaypoints? waypoints)
    {
        if (_controller is null) return;
        if (waypoints is null || waypoints.Count == 0)
        {
            try { _controller.RemoveLayer(WaypointLabelId); } catch { }
            _activeLayers.Remove(WaypointLabelId);
            TryRemove(WaypointLayerId, WaypointSourceId);
            return;
        }
        var sb = new System.Text.StringBuilder("{\"type\":\"FeatureCollection\",\"features\":[");
        for (int i = 0; i < waypoints.Count; i++)
        {
            var (lat, lon) = waypoints.Points[i];
            var kind = waypoints.KindOf(i);
            string label = kind switch { WaypointKind.Origin => "A", WaypointKind.Destination => "B", _ => i.ToString(CultureInfo.InvariantCulture) };
            if (i > 0) sb.Append(',');
            sb.Append(string.Create(CultureInfo.InvariantCulture,
                $"{{\"type\":\"Feature\",\"properties\":{{\"index\":{i},\"kind\":\"{kind.ToString().ToLowerInvariant()}\",\"label\":\"{label}\"}},\"geometry\":{{\"type\":\"Point\",\"coordinates\":[{lon:F6},{lat:F6}]}}}}"));
        }
        sb.Append("]}");
        try
        {
            SetOrAddSource(WaypointSourceId, WaypointLayerId, sb.ToString(), new Dictionary<string, object?>
            {
                ["circle-radius"]       = 11.0,
                ["circle-color"]        = new object[] { "match", new object[] { "get", "kind" }, "origin", "#22AA22", "destination", "#CC2222", "#F9A825" },
                ["circle-stroke-color"] = "#FFFFFF",
                ["circle-stroke-width"] = 2.5,
            }, circle: true);
            if (!_activeLayers.Contains(WaypointLabelId))
            {
                _controller.AddSymbolLayer(WaypointLabelId, WaypointSourceId, belowLayerId: null, sourceLayer: null,
                    properties: new Dictionary<string, object?>
                    {
                        ["text-field"]            = new object[] { "get", "label" },
                        ["text-size"]             = 12.0,
                        ["text-color"]            = "#FFFFFF",
                        ["text-allow-overlap"]    = true,
                        ["text-ignore-placement"] = true,
                    });
                _activeLayers.Add(WaypointLabelId);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RouteOverlay] ShowWaypoints threw: {ex}");
        }
    }

    public void ClearRoute()
    {
        if (_controller is null) return;
        TryRemove(PositionLayerId, PositionSourceId);
        TryRemove(AltLayerId,     AltSourceId);
        TryRemove(RouteLayerId,   RouteSourceId);
        TryRemove(HighwayLayerId, HighwaySourceId);
    }

    // Defensively removes any stale source/layer from MapLibre before adding fresh ones.
    // This handles the case where a previous ClearRoute's RemoveSource silently failed
    // (some MapLibre platform implementations don't throw on unknown IDs), which would
    // cause the subsequent AddGeoJsonSource to fail because the source already exists.
    private void SetOrAddSource(
        string sourceId, string layerId, string geoJson,
        Dictionary<string, object?> paint, bool circle = false)
    {
        if (_activeLayers.Contains(layerId))
        {
            _controller!.SetGeoJsonSource(sourceId, geoJson);
        }
        else
        {
            try { _controller!.RemoveLayer(layerId); } catch { }
            try { _controller!.RemoveSource(sourceId); } catch { }

            _controller!.AddGeoJsonSource(sourceId, geoJson);
            // A route line goes below the markers (the stops, the position), so they stay on top
            string? below = circle ? null
                : _activeLayers.Contains(WaypointLayerId) ? WaypointLayerId
                : _activeLayers.Contains(PositionLayerId) ? PositionLayerId : null;
            if (circle)
                _controller.AddCircleLayer(layerId, sourceId, belowLayerId: null, sourceLayer: null, properties: paint);
            else
                _controller.AddLineLayer(layerId, sourceId, belowLayerId: below, sourceLayer: null, properties: paint);
            _activeLayers.Add(layerId);
        }
    }

    private void TryRemove(string layerId, string sourceId)
    {
        try { _controller?.RemoveLayer(layerId); } catch { }
        try { _controller?.RemoveSource(sourceId); } catch { }
        _activeLayers.Remove(layerId);
    }

    private static string BuildRouteGeoJson(DirectionsRoute route)
        => ShapeToLineStringGeoJson(route.Legs.SelectMany(l => l.Shape).ToList());

    private static string BuildAltsGeoJson(IEnumerable<DirectionsRoute> routes)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"type\":\"FeatureCollection\",\"features\":[");
        bool first = true;
        foreach (var route in routes)
        {
            if (!first) sb.Append(',');
            first = false;
            var coords = route.Legs.SelectMany(l => l.Shape).ToList();
            sb.Append("{\"type\":\"Feature\",\"properties\":{},\"geometry\":{\"type\":\"LineString\",\"coordinates\":[");
            for (int i = 0; i < coords.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('[');
                sb.Append(coords[i].Lon.ToString("F6", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(coords[i].Lat.ToString("F6", CultureInfo.InvariantCulture));
                sb.Append(']');
            }
            sb.Append("]}}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static string BuildHighwayGeoJson(DirectionsRoute route)
    {
        if (route.HighwayWarning is null) return "{\"type\":\"FeatureCollection\",\"features\":[]}";

        var hwCoords = new List<(double Lon, double Lat)>();
        var indices = new HashSet<int>(route.HighwayWarning.HighwayStepIndices);

        if (route.Legs.Count > 0)
        {
            var leg = route.Legs[0];
            for (int si = 0; si < leg.Steps.Count; si++)
            {
                if (!indices.Contains(si)) continue;
                var step = leg.Steps[si];
                for (int ci = step.BeginShapeIndex;
                     ci <= Math.Min(step.EndShapeIndex, leg.Shape.Count - 1); ci++)
                    hwCoords.Add(leg.Shape[ci]);
            }
        }

        return ShapeToLineStringGeoJson(hwCoords);
    }

    private static string ShapeToLineStringGeoJson(IReadOnlyList<(double Lon, double Lat)> coords)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"properties\":{},\"geometry\":{\"type\":\"LineString\",\"coordinates\":[");
        for (int i = 0; i < coords.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('[');
            sb.Append(coords[i].Lon.ToString("F6", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(coords[i].Lat.ToString("F6", CultureInfo.InvariantCulture));
            sb.Append(']');
        }
        sb.Append("]}}]}");
        return sb.ToString();
    }
}
