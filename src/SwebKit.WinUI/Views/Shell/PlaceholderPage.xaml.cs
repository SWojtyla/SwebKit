using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace SwebKit.WinUI.Views.Shell;

public sealed partial class PlaceholderPage : Page
{
    public PlaceholderPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string area)
            AreaTitle.Text = AreaLabel(area);
    }

    private static string AreaLabel(string area) => area switch
    {
        "service-bus" => "Service Bus",
        "aks" => "AKS",
        "redis" => "Redis",
        "storage" => "Storage",
        "pipelines" => "Pipelines",
        "observability" => "Observability",
        "incident-timeline" => "Incident Timeline",
        _ => area,
    };
}
