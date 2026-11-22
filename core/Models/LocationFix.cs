namespace MaplibreNative.Routing.Core.Models;

/// <summary>
/// One position report: from the device's GPS, an NMEA feed, or <see cref="Navigation.RouteSimulator"/>.
/// </summary>
/// <param name="Lat">Latitude, degrees.</param>
/// <param name="Lon">Longitude, degrees.</param>
/// <param name="AccuracyMeters">Horizontal accuracy, when the source gives it: widens the off-route radius.</param>
/// <param name="Bearing">Direction of travel, degrees clockwise from north, when known.</param>
/// <param name="SpeedMps">Speed, metres a second, when known.</param>
public readonly record struct LocationFix(
    double Lat,
    double Lon,
    double? AccuracyMeters = null,
    double? Bearing = null,
    double? SpeedMps = null);
