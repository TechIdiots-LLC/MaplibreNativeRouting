using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaplibreNative.Routing.Core.Models;

namespace MaplibreNative.Routing.Wpf.Controls;

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

    public void Apply(RouteProgress progress)
    {
        IsNavigating = true;
        var step = progress.CurrentStep;
        Instruction = step.VerbalPreInstruction ?? step.Instruction;
        DistanceLabel = FormatDistance(progress.StepDistanceRemainingMeters);
        EtaLabel = FormatDuration(progress.DurationRemainingSeconds);
        TotalDistanceLabel = FormatDistance(progress.DistanceRemainingMeters);
        ManeuverIcon = GetManeuverIcon(step.Type);
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
        ManeuverType.SlightRight or ManeuverType.RampRight  => "↗",
        ManeuverType.Right or ManeuverType.SharpRight       => "→",
        ManeuverType.SlightLeft or ManeuverType.RampLeft    => "↖",
        ManeuverType.Left or ManeuverType.SharpLeft         => "←",
        ManeuverType.UturnLeft or ManeuverType.UturnRight   => "↩",
        ManeuverType.Continue or ManeuverType.StayStraight  => "↑",
        ManeuverType.RoundaboutEnter                        => "⟳",
        ManeuverType.FerryEnter                             => "⛴",
        ManeuverType.Destination                            => "⦿",
        ManeuverType.Start                                  => "▶",
        _                                                   => "↑",
    };
}
