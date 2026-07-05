# Changelog

## master
### ✨ Features and improvements
- _...Add new stuff here..._

### 🐞 Bug fixes
- _...Add new stuff here..._

## 0.0.2
### ✨ Features and improvements

### 🐞 Bug fixes

## 0.0.1
### ✨ Features and improvements
- Initial release: `MaplibreNative.Routing.Core` — platform-agnostic routing models, Valhalla router, A\* track-graph router, hybrid router, turn-by-turn navigation tracker, and maneuver announcer
- Initial release: `MaplibreNative.Routing` — MAUI handlers, `NavigationPanel` control, `RouteOverlay`, `NavigationSession`, and `UseMapLibreRouting` builder extension
- Initial release: `MaplibreNative.Routing.Wpf` — WPF handlers, `NavigationPanel` UserControl, GPS-agnostic `NavigationSession`, and `AddMaplibreRouting` DI extension
- Valhalla routing with pedestrian, bicycle, motorcycle, and auto profiles
- Highway avoidance (`use_highways: 0.1`) with `HighwayWarning` annotation and orange dashed overlay
- Hybrid routing: automatically stitches Valhalla road segments with A\* trail segments when both ends snap to imported track features
- `RouteProgressTracker` snaps GPS position to route shape and advances steps at 30 m threshold
- `ManeuverAnnouncerHelper` fires announcement events at 1000 m / 200 m / 30 m thresholds
- `NavigationPanel` dark overlay with dismissible highway warning bar, maneuver icon, instruction text, step distance, and ETA
