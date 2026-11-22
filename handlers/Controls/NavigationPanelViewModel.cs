using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaplibreNative.Routing.Core.Models;

namespace MaplibreNative.Routing.Controls;

/// <summary>A turn in the panel's list of directions: its arrow, what to do, and how far after the turn before.</summary>
public sealed record DirectionItem(string Icon, string Instruction, string Distance);

public partial class NavigationPanelViewModel : ObservableObject
{
    [ObservableProperty] private bool _isNavigating;
    [ObservableProperty] private string _instruction = "";
    [ObservableProperty] private string _distanceLabel = "";
    [ObservableProperty] private string _etaLabel = "";
    [ObservableProperty] private string _totalDistanceLabel = "";
    [ObservableProperty] private string _maneuverIcon = "↑";
    [ObservableProperty] private bool _hasHighwayWarning;
    [ObservableProperty] private string _highwayWarningText =
        "⚠ Route includes highway segments — verify before riding";

    // Tap the instruction for the rest of the turns
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandGlyph))]
    private bool _isDirectionsOpen;
    public string ExpandGlyph => IsDirectionsOpen ? "▴" : "▾";

    /// <summary>The turns after the next one, to the destination.</summary>
    public ObservableCollection<DirectionItem> Directions { get; } = [];
    private (DirectionsRoute Route, int Leg, int Step)? _listedFrom;

    [RelayCommand]
    private void ToggleDirections() => IsDirectionsOpen = !IsDirectionsOpen;

    public void Apply(RouteProgress progress)
    {
        IsNavigating = true;
        // The next maneuver, and the distance to it (a step ends where the next begins)
        var next = progress.UpcomingStep ?? progress.CurrentStep;
        Instruction = next.Instruction;
        DistanceLabel = FormatDistance(progress.StepDistanceRemainingMeters);
        EtaLabel = FormatDuration(progress.DurationRemainingSeconds);
        TotalDistanceLabel = FormatDistance(progress.DistanceRemainingMeters);
        ManeuverIcon = GetManeuverIcon(next.Type);
        ListDirections(progress);
    }

    /// <summary>The list again when the route or the step changes; distances between turns don't change on the way.</summary>
    private void ListDirections(RouteProgress progress)
    {
        var at = (progress.Route, progress.CurrentLegIndex, progress.CurrentStepIndex);
        if (_listedFrom is { } last && ReferenceEquals(last.Route, at.Route) && last.Leg == at.CurrentLegIndex
            && last.Step == at.CurrentStepIndex) return;
        _listedFrom = at;
        Directions.Clear();
        // Every step from the one after the next maneuver, this leg and those after
        var steps = progress.Route.Legs.Skip(progress.CurrentLegIndex).SelectMany(l => l.Steps).ToList();
        for (int i = progress.CurrentStepIndex + 2; i < steps.Count; i++)
            Directions.Add(new DirectionItem(GetManeuverIcon(steps[i].Type), steps[i].Instruction,
                FormatDistance(steps[i - 1].Distance)));
    }

    public void SetHighwayWarning(HighwayWarning? warning)
    {
        HasHighwayWarning = warning is not null;
        if (warning is not null)
        {
            var km = warning.TotalHighwayDistanceMeters / 1000.0;
            HighwayWarningText = $"⚠ Route includes {km:F1} km of highway — verify before riding";
        }
    }

    public void Clear()
    {
        IsNavigating = false;
        HasHighwayWarning = false;
        Instruction = "";
        IsDirectionsOpen = false;
        Directions.Clear();
        _listedFrom = null;
    }

    [RelayCommand]
    private void DismissHighwayWarning() => HasHighwayWarning = false;

    private static string FormatDistance(double meters) =>
        meters >= 1000
            ? $"{meters / 1000.0:F1} km"
            : $"{Math.Round(meters / 10) * 10:F0} m";

    private static string FormatDuration(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m"
            : $"{ts.Minutes} min";
    }

    private static string GetManeuverIcon(ManeuverType type) => type switch
    {
        ManeuverType.SlightRight or ManeuverType.RampRight => "↗",
        ManeuverType.Right or ManeuverType.SharpRight => "→",
        ManeuverType.SlightLeft or ManeuverType.RampLeft => "↖",
        ManeuverType.Left or ManeuverType.SharpLeft => "←",
        ManeuverType.UturnLeft or ManeuverType.UturnRight => "↩",
        ManeuverType.Continue or ManeuverType.StayStraight => "↑",
        ManeuverType.RoundaboutEnter => "⟳",
        ManeuverType.FerryEnter => "⛴",
        ManeuverType.Destination => "⦿",
        ManeuverType.Start => "▶",
        _ => "↑",
    };
}
