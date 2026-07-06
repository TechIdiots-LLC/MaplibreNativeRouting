using System.Diagnostics;
using System.Globalization;
using MapLibreNative.Maui.Handlers;
using MaplibreNative.Routing.Core.Models;
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

    public void ClearRoute()
    {
        if (_controller is null) return;
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
        Dictionary<string, object?> paint)
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
            _controller.AddLineLayer(layerId, sourceId,
                belowLayerId: null, sourceLayer: null, properties: paint);
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
