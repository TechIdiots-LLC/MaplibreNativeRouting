using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Navigation;
using MaplibreNative.Routing.Core.Utils;
using Xunit;

namespace MaplibreNative.Routing.Core.Tests;

public class VoiceTests
{
    [Theory]
    [InlineData(DistanceUnits.Metric, 12, "10 meters", "10 m")]
    [InlineData(DistanceUnits.Metric, 387, "400 meters", "400 m")]
    [InlineData(DistanceUnits.Metric, 1460, "1.5 kilometers", "1.5 km")]
    [InlineData(DistanceUnits.Metric, 7400, "7 kilometers", "7 km")]
    [InlineData(DistanceUnits.Imperial, 30, "100 feet", "100 ft")]
    [InlineData(DistanceUnits.Imperial, 122, "400 feet", "400 ft")]
    [InlineData(DistanceUnits.Imperial, 800, "0.5 miles", "0.5 mi")]
    [InlineData(DistanceUnits.Imperial, 1609.344, "1 mile", "1 mi")]
    [InlineData(DistanceUnits.UK, 70, "75 yards", "75 yd")]
    [InlineData(DistanceUnits.Metric, 1, "5 meters", "5 m")]   // never "0"
    public void DistancesAreRoundedAsADriverHearsThem(DistanceUnits units, double meters, string spoken, string shown)
    {
        var f = new DistanceFormatter(units);
        Assert.Equal(spoken, f.Spoken(meters));
        Assert.Equal(shown, f.Short(meters));
    }

    [Theory]
    [InlineData("Head SW on Maple Street", "Head southwest on Maple Street")]
    [InlineData("Turn left onto Main Street / MA 122A", "Turn left onto Main Street, Route 122A")]
    [InlineData("Continue onto I 190", "Continue onto Interstate 190")]
    [InlineData("Bear right onto US 20.", "Bear right onto U.S. Route 20")]
    public void InstructionsAreWordedToBeSpoken(string written, string said) =>
        Assert.Equal(said, SpokenInstructions.Say(new LegStep { Instruction = written }));

    [Fact]
    public void TheTurnAheadIsAnnouncedNotTheStepBeingDriven()
    {
        var route = TestRoutes.L();
        var tracker = new RouteProgressTracker(route);
        var announcer = new ManeuverAnnouncerHelper(new DistanceFormatter(DistanceUnits.Metric));
        var said = new List<ManeuverAnnouncementEventArgs>();
        announcer.AnnouncementNeeded += (_, e) => said.Add(e);
        // At 13.7 m/s (the step's own speed): 1000 m, 205 m and 55 m before the turn
        foreach (var fix in new RouteSimulator(route.Legs[0].Shape.Take(3).ToList(), 13.7).All())
            announcer.Update(tracker.Update(fix));
        Assert.Equal(["In 1 kilometer, turn left onto Main Street", "In 200 meters, turn left onto Main Street", "Turn left onto Main Street"],
            said.Select(e => e.Text));
        Assert.All(said, e => Assert.Equal(ManeuverType.Left, e.Step.Type));
        Assert.True(said[^1].IsImminent);
        Assert.InRange(said[^1].DistanceMeters, 40, 56);
    }

    [Fact]
    public void AStepBeganCloseToItsTurnSkipsTheFarAnnouncements()
    {
        // 40 m to the turn from the start: past the far announcement, so only the medium and the near
        var shape = new (double Lon, double Lat)[] { (-71.84049, 42.35), (-71.84, 42.35), (-71.84, 42.355) };
        var route = new DirectionsRoute
        {
            Distance = 595, Duration = 60,
            Legs = [new RouteLeg
            {
                Shape = shape, Distance = 595, Duration = 60,
                Steps =
                [
                    new() { Type = ManeuverType.Start, BeginShapeIndex = 0, EndShapeIndex = 1, Distance = 40, Duration = 10, Instruction = "Head E" },
                    new() { Type = ManeuverType.Left, BeginShapeIndex = 1, EndShapeIndex = 2, Distance = 555, Duration = 50, Instruction = "Turn left onto Main Street" },
                    new() { Type = ManeuverType.Destination, BeginShapeIndex = 2, EndShapeIndex = 2, Instruction = "Arrive" },
                ],
            }],
        };
        var tracker = new RouteProgressTracker(route);
        var announcer = new ManeuverAnnouncerHelper(new DistanceFormatter(DistanceUnits.Metric));
        var said = new List<string>();
        announcer.AnnouncementNeeded += (_, e) => said.Add(e.Text);
        foreach (var fix in new RouteSimulator(shape.Take(2).ToList(), 4).All()) announcer.Update(tracker.Update(fix));
        Assert.Equal(["In 50 meters, turn left onto Main Street", "Turn left onto Main Street"], said);
    }

    [Fact]
    public async Task ADriveSaysHowToStartEachTurnAndTheArrival()
    {
        var engine = new NavigationEngine(new NavigationOptions { Units = DistanceUnits.Imperial }, _ => new OneRoute());
        await engine.PlanAsync(new RouteOptions { Origin = (42.35, -71.86), Destination = (42.36, -71.84), Profile = RouteProfile.OfflineAuto });
        var said = new List<string>();
        engine.VoiceInstruction += (_, t) => said.Add(t);
        foreach (var fix in RouteSimulator.ForRoute(engine.Route!).All()) engine.UpdateLocation(fix);
        Assert.Equal(
        [
            "Head east on Elm Street",
            "In 0.6 miles, turn left onto Main Street",
            "In 0.1 miles, turn left onto Main Street",   // 15 s ahead at the step's 13.7 m/s: 205 m
            "Turn left onto Main Street",
            "In 0.6 miles, arrive",
            "In 0.1 miles, arrive",
            "You have arrived at your destination",
        ], said);
    }

    [Fact]
    public void GoingStraightOnIntoANewNameIsSaidOnceAsItsReached()
    {
        var l = TestRoutes.L();
        var steps = l.Legs[0].Steps.ToList();
        steps[1] = new LegStep { Type = ManeuverType.Continue, BeginShapeIndex = 2, EndShapeIndex = 4, Distance = 1110, Duration = 160, Instruction = "Continue onto Main Street" };
        var route = l with { Legs = [new RouteLeg { Shape = l.Legs[0].Shape, Steps = steps, Distance = 2750, Duration = 280 }] };
        var tracker = new RouteProgressTracker(route);
        var announcer = new ManeuverAnnouncerHelper(new DistanceFormatter(DistanceUnits.Metric));
        var said = new List<string>();
        announcer.AnnouncementNeeded += (_, e) => said.Add(e.Text);
        foreach (var fix in new RouteSimulator(route.Legs[0].Shape.Take(3).ToList(), 13.7).All()) announcer.Update(tracker.Update(fix));
        Assert.Equal(["Continue onto Main Street"], said);
    }

    private sealed class OneRoute : Routing.IRoutingEngine
    {
        public Task<DirectionsRoute?> RouteAsync(RouteOptions o) => Task.FromResult<DirectionsRoute?>(TestRoutes.L());
    }
}
