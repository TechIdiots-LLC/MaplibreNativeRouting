using System.Diagnostics;
using MapLibreNative.Maui.Handlers;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing;

/// <summary>
/// Manages the MapLibre source/layer for the active route and, when present, a
/// separate orange dashed layer for highway segments.
/// </summary>
public class RouteOverlay
{
    private const string RouteSourceId = "routing-route-src";
    private const string RouteLayerId = "routing-route-line";
    private const string HighwaySourceId = "routing-highway-src";
    private const string HighwayLayerId = "routing-highway-line";

    private IMapLibreMapController? _controller;

    public void SetController(IMapLibreMapController? controller)
    {
        _controller = controller;
    }

    public void ShowRoute(DirectionsRoute route)
    {
        if (_controller is null) return;
        try
        {
            var routeGeoJson = BuildRouteGeoJson(route);
            SetOrAddSource(RouteSourceId, RouteLayerId, routeGeoJson,
                new Dictionary<string, object?>
                {
                    ["line-color"] = "#1565C0",
                    ["line-width"] = 5.0,
                    ["line-opacity"] = 0.9,
                });

            if (route.HighwayWarning is { HighwayStepIndices.Count: > 0 })
            {
                var hwGeoJson = BuildHighwayGeoJson(route);
                SetOrAddSource(HighwaySourceId, HighwayLayerId, hwGeoJson,
                    new Dictionary<string, object?>
                    {
                        ["line-color"] = "#FF6600",
                        ["line-width"] = 5.0,
                        ["line-opacity"] = 0.9,
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
            Debug.WriteLine($"[RouteOverlay] ShowRoute threw: {ex}");
        }
    }

    public void ClearRoute()
    {
        if (_controller is null) return;
        TryRemove(RouteLayerId, RouteSourceId);
        TryRemove(HighwayLayerId, HighwaySourceId);
    }

    // Adds the source + line layer, or updates the source if already present.
    private void SetOrAddSource(
        string sourceId, string layerId, string geoJson,
        Dictionary<string, object?> paint)
    {
        try
        {
            _controller!.SetGeoJsonSource(sourceId, geoJson);
        }
        catch
        {
            _controller!.AddGeoJsonSource(sourceId, geoJson);
            _controller.AddLineLayer(layerId, sourceId,
                belowLayerId: null, sourceLayer: null, properties: paint);
        }
    }

    private void TryRemove(string layerId, string sourceId)
    {
        try { _controller?.RemoveLayer(layerId); } catch { }
        try { _controller?.RemoveSource(sourceId); } catch { }
    }

    private static string BuildRouteGeoJson(DirectionsRoute route)
    {
        var coords = route.Legs.SelectMany(l => l.Shape).ToList();
        return ShapeToLineStringGeoJson(coords);
    }

    private static string BuildHighwayGeoJson(DirectionsRoute route)
    {
        if (route.HighwayWarning is null) return "{\"type\":\"FeatureCollection\",\"features\":[]}";

        var hwCoords = new List<(double Lon, double Lat)>();
        var indices = new HashSet<int>(route.HighwayWarning.HighwayStepIndices);

        // Collect shape coords for highway steps in leg 0.
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
            sb.Append(coords[i].Lon.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(coords[i].Lat.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(']');
        }
        sb.Append("]}}]}");
        return sb.ToString();
    }
}
