using MaplibreNative.Routing.Core.Mvt;
using Xunit;

namespace MaplibreNative.Routing.Core.Tests;

public class TileCoordTests
{
    [Fact]
    public void ACorridorCoversTheWayAndLeavesTheBoxCornersOut()
    {
        // Vermont to New Hampshire, about 130 km on a diagonal, 30% of it either side
        const double aLat = 43.70, aLon = -72.80, bLat = 44.40, bLon = -71.40, width = 39_000;
        var corridor = TileCoord.CoverCorridor(aLat, aLon, bLat, bLon, width, 14).ToHashSet();
        double dLat = width / 110_574.0, dLon = width / (Math.Cos(44.05 * Math.PI / 180) * 111_320);
        var box = TileCoord.CoverBoundingBox(aLat - dLat, aLon - dLon, bLat + dLat, bLon + dLon, 14);

        Assert.True(corridor.Count < box.Count * 0.6, $"{corridor.Count} of the box's {box.Count}");
        Assert.Contains(TileCoord.FromLatLon(aLat, aLon, 14), corridor);
        Assert.Contains(TileCoord.FromLatLon(bLat, bLon, 14), corridor);
        Assert.Contains(TileCoord.FromLatLon((aLat + bLat) / 2, (aLon + bLon) / 2, 14), corridor);
        Assert.Contains(TileCoord.FromLatLon(aLat - 0.3, aLon, 14), corridor);       // 33 km short of A, inside
        Assert.DoesNotContain(TileCoord.FromLatLon(aLat - dLat, bLon + dLon, 14), corridor);   // the box's far corner
        Assert.DoesNotContain(TileCoord.FromLatLon(bLat + dLat, aLon - dLon, 14), corridor);
    }

    [Fact]
    public void AShortTripsCorridorIsAllItsBox()
    {
        var corridor = TileCoord.CoverCorridor(42.35, -71.85, 42.36, -71.84, 5_000, 14);
        Assert.NotEmpty(corridor);
        Assert.Contains(TileCoord.FromLatLon(42.35, -71.85, 14), corridor);
    }
}
