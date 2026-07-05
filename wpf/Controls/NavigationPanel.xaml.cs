using System.Windows.Controls;
using MaplibreNative.Routing.Core.Models;

namespace MaplibreNative.Routing.Wpf.Controls;

public partial class NavigationPanel : UserControl
{
    public NavigationPanelViewModel ViewModel { get; } = new();

    public NavigationPanel()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    public void Apply(RouteProgress progress) => ViewModel.Apply(progress);
    public void SetHighwayWarning(HighwayWarning? warning) => ViewModel.SetHighwayWarning(warning);
    public void Clear() => ViewModel.Clear();
}
