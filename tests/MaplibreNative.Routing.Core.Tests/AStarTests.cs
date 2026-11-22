using MaplibreNative.Routing.Core.Graph;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Routing;
using Xunit;

namespace MaplibreNative.Routing.Core.Tests;

public class AStarTests
{
    [Fact]
    public void TheCheapestRouteIsFoundNotTheFirstMet()
    {
        // On foot: a track straight from S to G (4 km/h), met at once from both ends, and a road arcing a little
        // north of it (5 km/h), longer but quicker. With an estimate as tight as walking's, the search must go on
        // past the first route met to find the road
        var s = (-71.86, 42.35); var g = (-71.84, 42.35);
        var track = new MvtRoadSegment { Coordinates = [s, g], RoadClass = "track" };
        var road = new MvtRoadSegment { Coordinates = [s, (-71.855, 42.351), (-71.85, 42.3515), (-71.845, 42.351), g], RoadClass = "minor" };
        var costing = new MvtCostingModel(RouteProfile.OfflinePedestrian);
        var graph = RoadGraph.Build([track, road], costing);
        var start = graph.NearestNode(s.Item2, s.Item1)!;
        var goal = graph.NearestNode(g.Item2, g.Item1)!;
        var path = BidirectionalAStarSolver.FindPath(graph, start, goal, costing.MaxSpeedMps);
        Assert.NotNull(path);
        Assert.All(path!, e => Assert.Equal("minor", e.RoadClass));
    }
}
