using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Navigation;
using Xunit;

namespace MaplibreNative.Routing.Core.Tests;

public class NavigationCameraTests
{
    private static List<(RouteProgress Progress, NavigationCameraPosition Camera)> Drive(NavigationCamera camera)
    {
        var route = TestRoutes.L();
        var tracker = new RouteProgressTracker(route);
        return RouteSimulator.ForRoute(route).All().Select(f => tracker.Update(f)).Select(p => (p, camera.For(p))).ToList();
    }

    [Fact]
    public void FollowingTurnsWithTheRoadAndTiltsLessAsTheTurnNears()
    {
        var drive = Drive(new NavigationCamera { ViewportWidth = 400, ViewportHeight = 800 });
        var start = drive[5].Camera;
        Assert.Equal(90, start.Bearing, 0);                  // east, along Elm Street
        Assert.Equal(60, start.Pitch);                       // the turn far off
        Assert.Equal(0.4 * 800, start.PaddingTop, 6);        // the user 70% of the way down the view
        Assert.Equal(42.35, start.Lat, 6);                   // on the road, not the fix

        // Near the turn: tilt eases off, the view starts to swing toward the new road
        var nearTurn = drive.First(d => d.Progress.CurrentStepIndex == 0 && d.Progress.StepDistanceRemainingMeters < 30);
        Assert.Equal(45, nearTurn.Camera.Pitch);
        Assert.InRange(nearTurn.Camera.Bearing, 0, 89);

        var after = drive.First(d => d.Progress.CurrentStepIndex == 1 && d.Progress.StepDistanceRemainingMeters < 900).Camera;
        Assert.Equal(0, after.Bearing, 0);                   // north, along Main Street
    }

    [Fact]
    public void TheZoomFitsTheTurnAndChangesOnlyAtStepsAndAlerts()
    {
        var drive = Drive(new NavigationCamera());
        var zooms = drive.Select(d => d.Camera.Zoom).ToList();
        Assert.All(zooms, z => Assert.InRange(z, NavigationCamera.MinZoom, NavigationCamera.MaxZoom));
        int changes = zooms.Zip(zooms.Skip(1)).Count(z => z.First != z.Second);
        Assert.InRange(changes, 1, 8);                       // 2 steps × (start + 3 alerts) at most
        // Further from the turn is zoomed further out
        Assert.True(zooms[0] < zooms[drive.FindIndex(d => d.Progress.StepDistanceRemainingMeters < 100)]);
    }

    [Fact]
    public void OffTheRouteTheCameraFollowsTheFixAndNorthUpIsFlat()
    {
        var route = TestRoutes.L();
        var p = new RouteProgressTracker(route).Update(new LocationFix(42.352, -71.855, Bearing: 200));
        var camera = new NavigationCamera();
        var c = camera.For(p);
        Assert.Equal((42.352, -71.855, 200.0), (c.Lat, c.Lon, c.Bearing));
        camera.Mode = NavigationCameraMode.NorthUp;
        c = camera.For(p);
        Assert.Equal((0.0, 0.0), (c.Bearing, c.Pitch));
    }

    [Fact]
    public void TheOverviewShowsTheRestOfTheRoute()
    {
        var route = TestRoutes.L();
        var p = new RouteProgressTracker(route).Update(new LocationFix(42.35, -71.85));
        var c = new NavigationCamera { Mode = NavigationCameraMode.Overview, ViewportWidth = 400, ViewportHeight = 800 }.For(p);
        Assert.Equal((0.0, 0.0), (c.Bearing, c.Pitch));
        Assert.Equal(-71.845, c.Lon, 3);                     // between the user and the end
        Assert.Equal(42.355, c.Lat, 3);
        Assert.InRange(c.Zoom, 12, 15);
    }
}
