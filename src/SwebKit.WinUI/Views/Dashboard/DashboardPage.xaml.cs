using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SwebKit.WinUI.ViewModels.Dashboard;
using SwebKit.WinUI.Views.Shared;

namespace SwebKit.WinUI.Views.Dashboard;

public sealed partial class DashboardPage : Page
{
    private bool _initialLoadScheduled;

    public DashboardPageViewModel ViewModel { get; }

    public DashboardPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<DashboardPageViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DeferredPageLoadScheduler.ScheduleOnce(this, ref _initialLoadScheduled, ViewModel.LoadAsync);
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        await ViewModel.DisposeAsync();
    }
}