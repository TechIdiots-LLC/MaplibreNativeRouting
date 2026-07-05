using MaplibreNative.Routing.Core.Models;

namespace MaplibreNative.Routing.Controls;

public partial class NavigationPanel : ContentView
{
    private readonly NavigationPanelViewModel _vm = new();

    public NavigationPanel()
    {
        InitializeComponent();
        BindingContext = _vm;
    }

    public void Update(RouteProgress progress)
    {
        _vm.Apply(progress);
    }

    public void SetHighwayWarning(HighwayWarning? warning)
    {
        _vm.SetHighwayWarning(warning);
    }

    public void Clear()
    {
        _vm.Clear();
    }
}
