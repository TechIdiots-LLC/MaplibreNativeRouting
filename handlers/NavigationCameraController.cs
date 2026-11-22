using MapLibreNative.Maui.Handlers;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Navigation;

namespace MaplibreNative.Routing;

/// <summary>
/// Moves a MAUI map with navigation: on each fix (the GPS's or a simulated drive's) the camera eases to where
/// <see cref="NavigationCamera"/> puts it. Panning or zooming the map pauses it (<see cref="IsPaused"/>) until
/// <see cref="Recenter"/>; <see cref="ShowOverview"/> shows the rest of the route.
///
/// The map doesn't say what moved its camera, so a move is taken to be the user's when the map comes to rest with
/// the followed point away from where the camera put it.
/// </summary>
public sealed class NavigationCameraController
{
    private const long EaseMs = 900;              // a little under the GPS's 1 s, so the motion is continuous
    private const double MovedAwayPixels = 60;
    private const double MovedZoom = 0.5;

    private readonly NavigationSession _session;
    private IMapLibreMapController? _controller;
    private NavigationCameraPosition? _last;
    private DateTime _lastMoveAt;
    private RouteProgress? _progress;

    public NavigationCameraController(NavigationSession session)
    {
        _session = session;
        session.ProgressUpdated += (_, p) =>
        {
            _progress = p;
            if (IsEnabled && !IsPaused) Move(p);
        };
    }

    /// <summary>The camera's state and settings: its mode, the view's size, the panels covering it.</summary>
    public NavigationCamera Camera { get; } = new();

    /// <summary>Follow navigation with the map's camera.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>The user has moved the map; the camera waits for <see cref="Recenter"/>.</summary>
    public bool IsPaused { get; private set; }

    public event EventHandler? PausedChanged;

    /// <summary>The map to move; call again when its controller changes (a new style keeps it).</summary>
    public void SetController(IMapLibreMapController? controller)
    {
        if (_controller is not null) _controller.OnCameraIdleReceived -= OnCameraIdle;
        _controller = controller;
        if (_controller is not null) _controller.OnCameraIdleReceived += OnCameraIdle;
    }

    /// <summary>The map's size, so the camera can fit the turn ahead and place the user in it.</summary>
    public void SetViewport(double width, double height)
    {
        if (width > 0) Camera.ViewportWidth = width;
        if (height > 0) Camera.ViewportHeight = height;
    }

    /// <summary>Follow again, from where the user is now.</summary>
    public void Recenter()
    {
        SetPaused(false);
        if (Camera.Mode == NavigationCameraMode.Overview) Camera.Mode = NavigationCameraMode.Following;
        if (_progress is not null) Move(_progress, durationMs: 600);
    }

    /// <summary>The rest of the route, until <see cref="Recenter"/>.</summary>
    public void ShowOverview()
    {
        Camera.Mode = NavigationCameraMode.Overview;
        SetPaused(false);
        if (_progress is not null) Move(_progress, durationMs: 800);
    }

    private void Move(RouteProgress progress, long durationMs = EaseMs)
    {
        if (_controller is null) return;
        var c = Camera.For(progress);
        try
        {
            _controller.EaseTo(c.Lat, c.Lon, c.Zoom, c.Bearing, c.Pitch, c.PaddingTop, 0, c.PaddingBottom, 0, durationMs);
            _last = c;
            _lastMoveAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NavigationCamera] EaseTo threw: {ex.Message}");
        }
    }

    /// <summary>The map at rest: if it isn't where the camera put it, the user moved it.</summary>
    private void OnCameraIdle()
    {
        if (!IsEnabled || IsPaused || _controller is null || _last is not { } last || !_session.IsNavigating) return;
        if (Camera.Mode == NavigationCameraMode.Overview) return;
        // Still settling from the camera's own move
        if ((DateTime.UtcNow - _lastMoveAt).TotalMilliseconds < EaseMs + 250) return;
        try
        {
            var (x, y) = _controller.LatLngToScreenPoint(last.Lat, last.Lon);
            double free = Camera.ViewportHeight - last.PaddingTop - last.PaddingBottom;
            double expectX = Camera.ViewportWidth / 2, expectY = last.PaddingTop + free / 2;
            bool moved = Math.Sqrt(Math.Pow(x - expectX, 2) + Math.Pow(y - expectY, 2)) > MovedAwayPixels
                         || Math.Abs(_controller.GetZoom() - last.Zoom) > MovedZoom;
            if (moved) MainThread.BeginInvokeOnMainThread(() => SetPaused(true));
        }
        catch { /* a projection the platform can't answer: leave following as it is */ }
    }

    private void SetPaused(bool paused)
    {
        if (IsPaused == paused) return;
        IsPaused = paused;
        PausedChanged?.Invoke(this, EventArgs.Empty);
    }
}
